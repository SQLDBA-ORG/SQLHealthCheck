using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlCheckLibrary.Models;

namespace SqlCheckLibrary.Services
{
    /// <summary>
    /// Runs complete health check diagnostics including embedded scripts and exports to CSV
    /// </summary>
    public class CompleteHealthCheckRunner : IDisposable
    {
        private readonly string _connectionString;
        private const string SCRIPT_CONFIG_FILE = "script-configurations.json";

        #region File IO Helpers

        private static Task<string> ReadAllTextAsync(string path)
        {
            return Task.Run(() => File.ReadAllText(path));
        }

        private static Task WriteAllTextAsync(string path, string contents)
        {
            return Task.Run(() => File.WriteAllText(path, contents));
        }

        #endregion
        private bool _disposed = false;

        public CompleteHealthCheckRunner(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        #region Script Configuration Management

        /// <summary>
        /// Load script configurations from JSON file
        /// </summary>
        public async Task<List<ScriptConfiguration>> LoadScriptConfigurationsAsync()
        {
            if (!File.Exists(SCRIPT_CONFIG_FILE))
                return new List<ScriptConfiguration>();

            var json = await ReadAllTextAsync(SCRIPT_CONFIG_FILE);
            return JsonSerializer.Deserialize<List<ScriptConfiguration>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<ScriptConfiguration>();
        }

        /// <summary>
        /// Save script configurations to JSON file
        /// </summary>
        public async Task SaveScriptConfigurationsAsync(List<ScriptConfiguration> scripts)
        {
            var json = JsonSerializer.Serialize(scripts, new JsonSerializerOptions { WriteIndented = true });
            await WriteAllTextAsync(SCRIPT_CONFIG_FILE, json);
        }

        /// <summary>
        /// Scan scripts folder and create configurations for discovered scripts
        /// </summary>
        public async Task<List<ScriptConfiguration>> ScanScriptsFolderAsync(string scriptsFolder = "scripts")
        {
            var scripts = new List<ScriptConfiguration>();

            if (!Directory.Exists(scriptsFolder))
            {
                Directory.CreateDirectory(scriptsFolder);
                return scripts;
            }

            var sqlFiles = Directory.GetFiles(scriptsFolder, "*.sql");
            int order = 1;

            foreach (var file in sqlFiles)
            {
                var fileName = Path.GetFileName(file);
                var name = Path.GetFileNameWithoutExtension(file);

                scripts.Add(new ScriptConfiguration
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Description = $"Diagnostic script: {fileName}",
                    ScriptPath = fileName,
                    Enabled = true,
                    ExecutionOrder = order++,
                    TimeoutSeconds = 300,
                    ExportToCsv = true,
                    ExecutionParameters = GetDefaultParameters(name)
                });
            }

            return scripts;
        }

        private string GetDefaultParameters(string scriptName)
        {
            var nameLower = scriptName.ToLower();

            if (nameLower.Contains("sp_blitz"))
                return "@CheckUserDatabaseObjects = 0, @CheckProcedureCache = 0";

            if (nameLower.Contains("sp_blitzindex"))
                return "@Mode = 0";

            if (nameLower.Contains("sp_blitzfirst"))
                return "@Seconds = 5, @ExpertMode = 1";

            if (nameLower.Contains("sp_triage") || nameLower.Contains("sqldba"))
                return "";

            return "";
        }

        #endregion

        #region Health Check Execution

        /// <summary>
        /// Run a single script and export results - used for multi-server execution
        /// </summary>
        public async Task RunSingleScriptAsync(
            ScriptConfiguration script,
            string outputFolder,
            string serverNameOverride = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CompleteHealthCheckRunner));

            var serverName = serverNameOverride ?? await GetServerNameAsync();
            // Sanitize server name for filename
            serverName = serverName.Replace("\\", "_").Replace("/", "_").Replace(":", "_");
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

            Directory.CreateDirectory(outputFolder);

            try
            {
                await ExecuteScriptAndExportAsync(script, serverName, timestamp, outputFolder);
            }
            catch (Exception ex)
            {
                var errorFile = Path.Combine(
                    outputFolder,
                    $"{serverName}_{script.Name}_ERROR_{timestamp}.txt");
                await WriteAllTextAsync(errorFile,
                    $"Error executing {script.Name} on {serverName}:\n{ex}\n\nConnection: {ConnectionStringBuilder.GetSanitizedForLogging(_connectionString)}");
                throw;
            }
        }

        /// <summary>
        /// Run complete health check with all configured scripts
        /// </summary>
        public async Task RunCompleteHealthCheckAsync(
            List<ScriptConfiguration> scripts,
            string outputFolder,
            Action<string, int>? progressCallback = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CompleteHealthCheckRunner));

            var serverName = await GetServerNameAsync();
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

            // Ensure output folder exists
            Directory.CreateDirectory(outputFolder);

            var enabledScripts = scripts
                .Where(s => s.Enabled)
                .OrderBy(s => s.ExecutionOrder)
                .ToList();

            for (int i = 0; i < enabledScripts.Count; i++)
            {
                var script = enabledScripts[i];
                var progress = (int)((i + 1) * 100.0 / enabledScripts.Count);

                progressCallback?.Invoke($"Executing {script.Name}...", progress);

                try
                {
                    await ExecuteScriptAndExportAsync(
                        script,
                        serverName,
                        timestamp,
                        outputFolder);
                }
                catch (Exception ex)
                {
                    // Log error but continue with other scripts
                    var errorFile = Path.Combine(
                        outputFolder,
                        $"{serverName}_{script.Name}_ERROR_{timestamp}.txt");
                    await WriteAllTextAsync(errorFile,
                        $"Error executing {script.Name}:\n{ex}\n\nConnection: {ConnectionStringBuilder.GetSanitizedForLogging(_connectionString)}");
                }
            }
        }

        private async Task ExecuteScriptAndExportAsync(
            ScriptConfiguration script,
            string serverName,
            string timestamp,
            string outputFolder)
        {
            // Read script file
            var scriptPath = Path.Combine("scripts", script.ScriptPath);
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Script not found: {scriptPath}");

            var scriptContent = await ReadAllTextAsync(scriptPath);

            // Execute the script to install the procedure
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                // Split script on GO batch separators using regex (GO must be on its own line)
                var batches = System.Text.RegularExpressions.Regex.Split(
                    scriptContent,
                    @"^\s*GO\s*$",
                    System.Text.RegularExpressions.RegexOptions.Multiline |
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                foreach (string batch in batches)
                {
                    string trimmed = batch.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    try
                    {
                        using (var cmd = new SqlCommand(trimmed, connection))
                        {
                            cmd.CommandTimeout = script.TimeoutSeconds > 0 ? script.TimeoutSeconds : 300;
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                    catch (SqlException ex) when (ex.Number == 2714 || ex.Number == 15233)
                    {
                        // 2714 = Object already exists, 15233 = Property doesn't exist
                        // Continue with next batch
                    }
                    catch (Exception)
                    {
                        // Log but continue - some batches may fail due to version differences
                    }
                }

                try
                {
                    // Execute with parameters if specified
                    if (!string.IsNullOrWhiteSpace(script.ExecutionParameters))
                    {
                        //EXEC {Path.GetFileNameWithoutExtension(script.ScriptPath)} 
                        var execCommand = $"{script.ExecutionParameters}";

                        using (var command = new SqlCommand(execCommand, connection))
                        {
                            command.CommandTimeout = script.TimeoutSeconds > 0
                                ? script.TimeoutSeconds
                                : 300;
                            var reader = await command.ExecuteReaderAsync();
                            //using (var reader = await command.ExecuteReaderAsync())
                            //{
                            //var resultSetIndex = 0;
                            //
                            //do
                            //{
                            //    var dataTable = new DataTable();
                            //    dataTable.Load(reader);
                            //
                            //    if (script.ExportToCsv && dataTable.Rows.Count > 0)
                            //    {
                            //        var fileName = resultSetIndex == 0
                            //            ? $"{serverName}_{script.Name}_{timestamp}.csv"
                            //            : $"{serverName}_{script.Name}_{resultSetIndex}_{timestamp}.csv";
                            //
                            //        var filePath = Path.Combine(outputFolder, fileName);
                            //        await ExportToCsvAsync(dataTable, filePath);
                            //    }
                            //
                            //    resultSetIndex++;
                            //}
                            //while (!reader.IsClosed && reader.NextResult());
                            //}
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Some issue with execution, try to extract at least
                    var errorFile = Path.Combine(
                       outputFolder,
                       $"{serverName}_{script.Name}_ERROR_{timestamp}.txt");
                    await WriteAllTextAsync(errorFile,
                        $"Error executing {script.Name}:\n{ex}\n\nCommand: " + script.ExecutionParameters);
                }
                try
                {
                    // Execute with parameters if specified
                    if (!string.IsNullOrWhiteSpace(script.SqlQueryForOutput))
                    {
                        //EXEC {Path.GetFileNameWithoutExtension(script.ScriptPath)} 
                        var execCommand = $"{script.SqlQueryForOutput}";

                        using (var command = new SqlCommand(execCommand, connection))
                        {
                            command.CommandTimeout = script.TimeoutSeconds > 0
                                ? script.TimeoutSeconds
                                : 300;

                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                var resultSetIndex = 0;

                                do
                                {
                                    var dataTable = new DataTable();
                                    dataTable.Load(reader);

                                    if (script.ExportToCsv && dataTable.Rows.Count > 0)
                                    {
                                        var fileName = resultSetIndex == 0
                                            ? $"{serverName}_{script.Name}_{timestamp}.csv"
                                            : $"{serverName}_{script.Name}_{resultSetIndex}_{timestamp}.csv";

                                        var filePath = Path.Combine(outputFolder, fileName);
                                        await ExportToCsvAsync(dataTable, filePath);
                                    }

                                    resultSetIndex++;
                                }
                                while (!reader.IsClosed && reader.NextResult());
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Some issue with execution, try to extract at least
                    var errorFile = Path.Combine(
                       outputFolder,
                       $"{serverName}_{script.Name}_ERROR_{timestamp}.txt");
                    await WriteAllTextAsync(errorFile,
                        $"Error executing {script.Name}:\n{ex}\n\nCommand: " + script.ExecutionParameters);
                }
            }
        }

        #endregion

        #region CSV Export

        private async Task ExportToCsvAsync(DataTable dataTable, string filePath)
        {
            var csv = new StringBuilder();

            // Header
            var headers = dataTable.Columns.Cast<DataColumn>()
                .Select(column => EscapeCsv(column.ColumnName));
            csv.AppendLine(string.Join(",", headers));

            // Rows
            foreach (DataRow row in dataTable.Rows)
            {
                var values = row.ItemArray.Select(field => EscapeCsv(field?.ToString() ?? ""));
                csv.AppendLine(string.Join(",", values));
            }

            await WriteAllTextAsync(filePath, csv.ToString());
        }

        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }

        #endregion

        #region Server Info

        public async Task<string> GetServerNameAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = new SqlCommand("SELECT @@SERVERNAME", connection);
            var result = await command.ExecuteScalarAsync();
            return result?.ToString()?.Replace("\\", "_") ?? "UNKNOWN";
        }

        public async Task<ServerDiagnosticInfo> GetServerDiagnosticInfoAsync()
        {
            var info = new ServerDiagnosticInfo();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Server name
            using (var cmd = new SqlCommand("SELECT @@SERVERNAME", connection))
            {
                info.ServerName = (await cmd.ExecuteScalarAsync())?.ToString() ?? "Unknown";
            }

            // Version
            using (var cmd = new SqlCommand("SELECT @@VERSION", connection))
            {
                info.Version = (await cmd.ExecuteScalarAsync())?.ToString() ?? "Unknown";
            }

            // Edition
            using (var cmd = new SqlCommand("SELECT SERVERPROPERTY('Edition')", connection))
            {
                info.Edition = (await cmd.ExecuteScalarAsync())?.ToString() ?? "Unknown";
            }

            // Product Level
            using (var cmd = new SqlCommand("SELECT SERVERPROPERTY('ProductLevel')", connection))
            {
                info.ProductLevel = (await cmd.ExecuteScalarAsync())?.ToString() ?? "Unknown";
            }

            // Database count
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE database_id > 4", connection))
            {
                info.UserDatabaseCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            return info;
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
                ResourceManager.SuggestCleanup();
            }

            _disposed = true;
        }

        #endregion
    }

    /// <summary>
    /// Server diagnostic information
    /// </summary>
    public class ServerDiagnosticInfo
    {
        public string ServerName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Edition { get; set; } = string.Empty;
        public string ProductLevel { get; set; } = string.Empty;
        public int UserDatabaseCount { get; set; }
    }
}