using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlCheckLibrary.Models;

namespace SqlCheckLibrary.Services
{
    /// <summary>
    /// Enterprise-grade SQL check executor with multi-server support and resource management
    /// </summary>
    public class CheckRunner : IDisposable
    {
        private readonly string _connectionString;
        private bool _disposed = false;
        private static readonly SemaphoreSlim _connectionThrottle = new SemaphoreSlim(10, 10);
        private const int CLEANUP_INTERVAL = 20;

        #region File IO Helper
        
        private static Task WriteAllTextAsync(string path, string contents)
        {
            return Task.Run(() => File.WriteAllText(path, contents));
        }

        #endregion

        public CheckRunner(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        #region Connection Testing

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GetServerNameAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = new SqlCommand("SELECT @@SERVERNAME", connection);
            var result = await command.ExecuteScalarAsync();
            return result?.ToString() ?? "UNKNOWN";
        }

        public async Task<string> GetServerVersionAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = new SqlCommand("SELECT @@VERSION", connection);
            var result = await command.ExecuteScalarAsync();
            return result?.ToString() ?? "Unknown";
        }

        #endregion

        #region Single Check Execution

        public async Task<CheckResult> RunCheckAsync(SqlCheck check)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CheckRunner));

            var result = new CheckResult
            {
                CheckId = check.Id,
                CheckName = check.Name,
                Category = check.Category,
                Severity = check.Severity,
                ExpectedValue = check.ExpectedValue,
                ExecutedAt = DateTime.Now
            };

            await _connectionThrottle.WaitAsync();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new SqlCommand(check.SqlQuery, connection);
                command.CommandTimeout = 30;

                var executionType = check.ExecutionType?.ToLower() ?? "binary";

                switch (executionType)
                {
                    case "rowcount":
                        await ExecuteRowCountCheckAsync(check, command, result);
                        break;

                    case "infoonly":
                        await ExecuteInfoOnlyCheckAsync(check, command, result);
                        break;

                    default:
                        await ExecuteBinaryCheckAsync(check, command, result);
                        break;
                }

                // Apply placeholder replacement for message
                result.Message = PlaceholderService.FormatCheckMessage(
                    check,
                    result.Passed,
                    result.ActualValue);
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.Message = $"Check execution failed: {ex.Message}";
            }
            finally
            {
                _connectionThrottle.Release();
            }

            return result;
        }

        private async Task ExecuteBinaryCheckAsync(SqlCheck check, SqlCommand command, CheckResult result)
        {
            var resultValue = await command.ExecuteScalarAsync();

            if (resultValue != null && int.TryParse(resultValue.ToString(), out int actualValue))
            {
                result.ActualValue = actualValue;
                result.Passed = actualValue == check.ExpectedValue;
            }
            else
            {
                result.Passed = false;
                result.Message = "Query did not return a valid numeric result";
            }
        }

        private async Task ExecuteRowCountCheckAsync(SqlCheck check, SqlCommand command, CheckResult result)
        {
            int rowCount = 0;

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                rowCount++;
            }

            result.ActualValue = rowCount;

            var condition = check.RowCountCondition?.ToLower() ?? "equals0";

            result.Passed = condition switch
            {
                "equals0" => rowCount == 0,
                "greaterthan0" => rowCount > 0,
                "lessthan0" => rowCount < 0,
                "any" => true,
                _ => rowCount == check.ExpectedValue
            };
        }

        private async Task ExecuteInfoOnlyCheckAsync(SqlCheck check, SqlCommand command, CheckResult result)
        {
            var resultValue = await command.ExecuteScalarAsync();

            if (resultValue != null && int.TryParse(resultValue.ToString(), out int actualValue))
            {
                result.ActualValue = actualValue;
            }

            result.Passed = true;
        }

        #endregion

        #region Batch Check Execution

        public async Task<List<CheckResult>> RunChecksAsync(IEnumerable<SqlCheck> checks)
        {
            return await RunChecksAsync(checks, null, CancellationToken.None);
        }

        public async Task<List<CheckResult>> RunChecksAsync(
            IEnumerable<SqlCheck> checks,
            IProgress<(int current, int total, string name)>? progress,
            CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CheckRunner));

            var results = new List<CheckResult>();
            var checksList = checks.ToList();

            try
            {
                const int batchSize = 5;
                int checksProcessed = 0;

                for (int i = 0; i < checksList.Count; i += batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = checksList.Skip(i).Take(batchSize);
                    var batchTasks = batch.Select(async check =>
                    {
                        var result = await RunCheckAsync(check);
                        Interlocked.Increment(ref checksProcessed);
                        progress?.Report((checksProcessed, checksList.Count, check.Name));
                        return result;
                    });

                    var batchResults = await Task.WhenAll(batchTasks);
                    results.AddRange(batchResults);

                    // Periodic cleanup
                    if (checksProcessed % CLEANUP_INTERVAL == 0)
                    {
                        ResourceManager.SuggestCleanup();
                    }
                }

                return results;
            }
            finally
            {
                ResourceManager.SuggestCleanup();
            }
        }

        #endregion

        #region Multi-Server Support

        /// <summary>
        /// Run checks across multiple servers in parallel
        /// </summary>
        public static async Task<MultiServerCheckResults> RunChecksOnMultipleServersAsync(
            IEnumerable<ServerConnection> servers,
            IEnumerable<SqlCheck> checks,
            IProgress<(string server, int current, int total)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var results = new MultiServerCheckResults();
            var serverList = servers.ToList();
            var checksList = checks.ToList();

            // Run all servers in parallel
            var serverTasks = serverList.Select(async server =>
            {
                var serverResult = new ServerCheckResults
                {
                    ServerName = server.ServerName,
                    ConnectionString = server.ConnectionString
                };

                try
                {
                    using var runner = new CheckRunner(server.ConnectionString);

                    // Test connection first
                    if (!await runner.TestConnectionAsync())
                    {
                        serverResult.ConnectionSuccessful = false;
                        serverResult.ErrorMessage = "Connection failed";
                        return serverResult;
                    }

                    serverResult.ConnectionSuccessful = true;
                    serverResult.ActualServerName = await runner.GetServerNameAsync();

                    // Run checks with progress
                    var serverProgress = new Progress<(int current, int total, string name)>(p =>
                    {
                        progress?.Report((server.ServerName, p.current, p.total));
                    });

                    serverResult.Results = await runner.RunChecksAsync(
                        checksList,
                        serverProgress,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    serverResult.ConnectionSuccessful = false;
                    serverResult.ErrorMessage = ex.Message;
                }

                return serverResult;
            });

            var allServerResults = await Task.WhenAll(serverTasks);
            results.ServerResults.AddRange(allServerResults);

            // Build grouped results by check
            results.GroupedByCheck = GroupResultsByCheck(results.ServerResults, checksList);

            return results;
        }

        private static List<CheckGroupedResult> GroupResultsByCheck(
            List<ServerCheckResults> serverResults,
            List<SqlCheck> checks)
        {
            var grouped = new List<CheckGroupedResult>();

            foreach (var check in checks)
            {
                var checkGroup = new CheckGroupedResult
                {
                    CheckId = check.Id,
                    CheckName = check.Name,
                    Category = check.Category,
                    Severity = check.Severity
                };

                foreach (var serverResult in serverResults.Where(s => s.ConnectionSuccessful))
                {
                    var result = serverResult.Results?.FirstOrDefault(r => r.CheckId == check.Id);
                    if (result != null)
                    {
                        checkGroup.ServerResults.Add(new ServerCheckResultSummary
                        {
                            ServerName = serverResult.ActualServerName ?? serverResult.ServerName,
                            Passed = result.Passed,
                            Message = result.Message,
                            ActualValue = result.ActualValue
                        });

                        if (result.Passed)
                            checkGroup.PassedCount++;
                        else
                            checkGroup.FailedCount++;
                    }
                }

                grouped.Add(checkGroup);
            }

            return grouped;
        }

        #endregion

        #region CSV Export

        public async Task<string> ExportResultsToCsvAsync(
            List<CheckResult> results,
            string outputFolder,
            string? serverName = null)
        {
            serverName ??= await GetServerNameAsync();
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var fileName = $"{serverName}_HealthCheck_{timestamp}.csv";
            var filePath = Path.Combine(outputFolder, fileName);

            var csv = new StringBuilder();

            // Header
            csv.AppendLine("CheckId,CheckName,Category,Severity,Passed,ActualValue,Message,ExecutedAt");

            // Rows
            foreach (var result in results)
            {
                csv.AppendLine($"{EscapeCsv(result.CheckId)}," +
                              $"{EscapeCsv(result.CheckName)}," +
                              $"{EscapeCsv(result.Category)}," +
                              $"{EscapeCsv(result.Severity)}," +
                              $"{result.Passed}," +
                              $"{result.ActualValue}," +
                              $"{EscapeCsv(result.Message)}," +
                              $"{result.ExecutedAt:yyyy-MM-dd HH:mm:ss}");
            }

            await WriteAllTextAsync(filePath, csv.ToString());
            return filePath;
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Cleanup managed resources
                ResourceManager.SuggestCleanup();
            }

            _disposed = true;
        }

        #endregion
    }

    #region Multi-Server Models

    /// <summary>
    /// Represents a server connection for multi-server operations
    /// </summary>
    public class ServerConnection
    {
        public string ServerName { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
        public bool UseIntegratedSecurity { get; set; } = true;
        public string? Username { get; set; }
        // Password should be retrieved at runtime from secure storage, not stored here
    }

    /// <summary>
    /// Results from running checks on multiple servers
    /// </summary>
    public class MultiServerCheckResults
    {
        public List<ServerCheckResults> ServerResults { get; set; } = new();
        public List<CheckGroupedResult> GroupedByCheck { get; set; } = new();

        public int TotalServers => ServerResults.Count;
        public int SuccessfulServers => ServerResults.Count(s => s.ConnectionSuccessful);
        public int FailedServers => ServerResults.Count(s => !s.ConnectionSuccessful);
    }

    /// <summary>
    /// Results from a single server
    /// </summary>
    public class ServerCheckResults
    {
        public string ServerName { get; set; } = string.Empty;
        public string? ActualServerName { get; set; }
        public string ConnectionString { get; set; } = string.Empty;
        public bool ConnectionSuccessful { get; set; }
        public string? ErrorMessage { get; set; }
        public List<CheckResult>? Results { get; set; }

        public int TotalChecks => Results?.Count ?? 0;
        public int PassedChecks => Results?.Count(r => r.Passed) ?? 0;
        public int FailedChecks => Results?.Count(r => !r.Passed) ?? 0;
    }

    /// <summary>
    /// Results grouped by check across all servers
    /// </summary>
    public class CheckGroupedResult
    {
        public string CheckId { get; set; } = string.Empty;
        public string CheckName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public List<ServerCheckResultSummary> ServerResults { get; set; } = new();
        public int PassedCount { get; set; }
        public int FailedCount { get; set; }
        public bool AllPassed => FailedCount == 0;
    }

    /// <summary>
    /// Summary of a check result for a specific server
    /// </summary>
    public class ServerCheckResultSummary
    {
        public string ServerName { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public string Message { get; set; } = string.Empty;
        public int ActualValue { get; set; }
    }

    #endregion
}
