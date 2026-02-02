using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Data.SqlClient;

namespace SqlCheckLibrary.Services
{
    /// <summary>
    /// Enterprise connection string builder with security best practices.
    /// Supports Windows Authentication (recommended) and SQL Server Authentication.
    /// </summary>
    public static class ConnectionStringBuilder
    {
        /// <summary>
        /// Build a connection string using Windows Authentication (Integrated Security).
        /// This is the preferred enterprise method using Active Directory credentials.
        /// </summary>
        public static string BuildWithIntegratedSecurity(
            string server,
            string? database = null,
            bool encrypt = true,
            bool trustServerCertificate = false,
            int connectTimeout = 30,
            int commandTimeout = 30)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                IntegratedSecurity = true,
                Encrypt = encrypt,
                TrustServerCertificate = trustServerCertificate,
                ConnectTimeout = connectTimeout,
                CommandTimeout = commandTimeout,
                ApplicationName = "SQL Health Monitor",
                // Best practices
                MultipleActiveResultSets = true,
                Pooling = true,
                MinPoolSize = 1,
                MaxPoolSize = 100
            };

            if (!string.IsNullOrWhiteSpace(database))
            {
                builder.InitialCatalog = database;
            }

            return builder.ConnectionString;
        }

        /// <summary>
        /// Build a connection string using SQL Server Authentication.
        /// Use only when Windows Authentication is not available.
        /// Password should be retrieved from secure storage at runtime.
        /// </summary>
        public static string BuildWithSqlAuth(
            string server,
            string username,
            string password,
            string? database = null,
            bool encrypt = true,
            bool trustServerCertificate = false,
            int connectTimeout = 30,
            int commandTimeout = 30)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Username is required for SQL Authentication", nameof(username));

            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password is required for SQL Authentication", nameof(password));

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                UserID = username,
                Password = password,
                IntegratedSecurity = false,
                Encrypt = encrypt,
                TrustServerCertificate = trustServerCertificate,
                ConnectTimeout = connectTimeout,
                CommandTimeout = commandTimeout,
                ApplicationName = "SQL Health Monitor",
                // Best practices
                MultipleActiveResultSets = true,
                Pooling = true,
                MinPoolSize = 1,
                MaxPoolSize = 100
            };

            if (!string.IsNullOrWhiteSpace(database))
            {
                builder.InitialCatalog = database;
            }

            return builder.ConnectionString;
        }

        /// <summary>
        /// Parse and validate an existing connection string
        /// </summary>
        public static ConnectionInfo ParseConnectionString(string connectionString)
        {
            var info = new ConnectionInfo();

            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                info.Server = builder.DataSource;
                info.Database = builder.InitialCatalog;
                info.UseIntegratedSecurity = builder.IntegratedSecurity;
                info.Username = builder.UserID;
                info.Encrypt = builder.Encrypt;
                info.TrustServerCertificate = builder.TrustServerCertificate;
                info.IsValid = true;
            }
            catch (Exception ex)
            {
                info.IsValid = false;
                info.ValidationError = ex.Message;
            }

            return info;
        }

        /// <summary>
        /// Get a sanitized connection string safe for logging (removes password)
        /// </summary>
        public static string GetSanitizedForLogging(string connectionString)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                if (!string.IsNullOrWhiteSpace(builder.Password))
                {
                    builder.Password = "********";
                }
                return builder.ConnectionString;
            }
            catch
            {
                return "[Invalid connection string]";
            }
        }

        /// <summary>
        /// Validate connection string security settings and return warnings
        /// </summary>
        public static ConnectionSecurityValidation ValidateSecurity(string connectionString)
        {
            var validation = new ConnectionSecurityValidation();

            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);

                // Check for integrated security (recommended)
                if (!builder.IntegratedSecurity)
                {
                    validation.Warnings.Add("SQL Authentication is used instead of Windows Authentication (Integrated Security).");
                    validation.Warnings.Add("Recommendation: Use Integrated Security=SSPI when possible.");
                }

                // Check encryption
                if (!builder.Encrypt)
                {
                    validation.Warnings.Add("Connection encryption is disabled.");
                    validation.Warnings.Add("Recommendation: Set Encrypt=True for production environments.");
                }

                // Check TrustServerCertificate
                if (builder.TrustServerCertificate && builder.Encrypt)
                {
                    validation.Warnings.Add("TrustServerCertificate is enabled, which may expose connections to man-in-the-middle attacks.");
                    validation.Warnings.Add("Recommendation: Use proper certificates and set TrustServerCertificate=False.");
                }

                // Check for hardcoded password in integrated security mode
                if (builder.IntegratedSecurity && !string.IsNullOrWhiteSpace(builder.Password))
                {
                    validation.Warnings.Add("Password provided with Integrated Security enabled (will be ignored).");
                }

                validation.IsSecure = validation.Warnings.Count == 0;
            }
            catch (Exception ex)
            {
                validation.IsValid = false;
                validation.ValidationError = ex.Message;
            }

            return validation;
        }

        /// <summary>
        /// Build connection string from ConnectionInfo object
        /// </summary>
        public static string BuildFromInfo(ConnectionInfo info, string? password = null)
        {
            if (info.UseIntegratedSecurity)
            {
                return BuildWithIntegratedSecurity(
                    info.Server,
                    info.Database,
                    info.Encrypt,
                    info.TrustServerCertificate);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(password))
                    throw new ArgumentException("Password is required for SQL Authentication");

                return BuildWithSqlAuth(
                    info.Server,
                    info.Username ?? "",
                    password,
                    info.Database,
                    info.Encrypt,
                    info.TrustServerCertificate);
            }
        }
    }

    /// <summary>
    /// Parsed connection information
    /// </summary>
    public class ConnectionInfo
    {
        public string Server { get; set; } = string.Empty;
        public string? Database { get; set; }
        public bool UseIntegratedSecurity { get; set; } = true;
        public string? Username { get; set; }
        public bool Encrypt { get; set; } = true;
        public bool TrustServerCertificate { get; set; } = false;
        public bool IsValid { get; set; } = true;
        public string? ValidationError { get; set; }
    }

    /// <summary>
    /// Security validation results
    /// </summary>
    public class ConnectionSecurityValidation
    {
        public bool IsValid { get; set; } = true;
        public bool IsSecure { get; set; } = false;
        public string? ValidationError { get; set; }
        public List<string> Warnings { get; set; } = new();
    }
}
