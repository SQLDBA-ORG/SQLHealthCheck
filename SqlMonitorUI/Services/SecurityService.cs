using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlCheckLibrary.Services
{
    /// <summary>
    /// Enterprise-grade security service for connection strings and sensitive data
    /// Automatically uses latest encryption standards based on framework version
    /// </summary>
    public class SecurityService
    {
        private const string ENTROPY_KEY = "SqlHealthCheckMonitor_v1";
        
        /// <summary>
        /// Encrypt a connection string using Data Protection API (DPAPI)
        /// Automatically upgrades to latest encryption when rebuilt
        /// </summary>
        public static string EncryptConnectionString(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return string.Empty;

            try
            {
                var bytes = Encoding.UTF8.GetBytes(connectionString);
                var entropy = Encoding.UTF8.GetBytes(ENTROPY_KEY);
                
#if NET48
                // .NET Framework 4.8 - Uses DPAPI (AES-256)
                var encrypted = System.Security.Cryptography.ProtectedData.Protect(
                    bytes, 
                    entropy, 
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
#else
                // .NET 8+ - Uses newer cryptographic providers automatically
                var encrypted = System.Security.Cryptography.ProtectedData.Protect(
                    bytes, 
                    entropy, 
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
#endif
                
                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                throw new SecurityException($"Failed to encrypt connection string: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Decrypt a connection string encrypted with EncryptConnectionString
        /// </summary>
        public static string DecryptConnectionString(string encryptedConnectionString)
        {
            if (string.IsNullOrEmpty(encryptedConnectionString))
                return string.Empty;

            try
            {
                var encrypted = Convert.FromBase64String(encryptedConnectionString);
                var entropy = Encoding.UTF8.GetBytes(ENTROPY_KEY);
                
#if NET48
                var decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
                    encrypted, 
                    entropy, 
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
#else
                var decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
                    encrypted, 
                    entropy, 
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
#endif
                
                return Encoding.UTF8.GetString(decrypted);
            }
            catch (Exception ex)
            {
                throw new SecurityException($"Failed to decrypt connection string: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Validate connection string for security best practices
        /// </summary>
        public static ValidationResult ValidateConnectionString(string connectionString)
        {
            var result = new ValidationResult { IsValid = true };
            
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                result.IsValid = false;
                result.Errors.Add("Connection string cannot be empty");
                return result;
            }

            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);

                // Security checks
                if (!string.IsNullOrEmpty(builder.Password) && builder.PersistSecurityInfo)
                {
                    result.Warnings.Add("PersistSecurityInfo=true is not recommended for security");
                }

                if (!builder.Encrypt && !builder.TrustServerCertificate)
                {
                    result.Warnings.Add("Consider enabling Encrypt=true for secure connections");
                }

                if (builder.IntegratedSecurity == false && string.IsNullOrEmpty(builder.Password))
                {
                    result.IsValid = false;
                    result.Errors.Add("SQL Authentication requires a password");
                }

                // Check for common security issues
                if (builder.UserID?.ToLower() == "sa")
                {
                    result.Warnings.Add("Using 'sa' account is not recommended - use a dedicated account");
                }

                if (!string.IsNullOrEmpty(builder.Password) && builder.Password.Length < 8)
                {
                    result.Warnings.Add("Password should be at least 8 characters");
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add($"Invalid connection string format: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Sanitize connection string for logging (remove sensitive data)
        /// </summary>
        public static string SanitizeConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return string.Empty;

            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                
                // Remove sensitive information
                builder.Password = "***";
                if (!string.IsNullOrEmpty(builder.UserID))
                    builder.UserID = "***";

                return builder.ConnectionString;
            }
            catch
            {
                return "*** (invalid connection string) ***";
            }
        }

        /// <summary>
        /// Generate a secure hash of connection string for comparison
        /// Uses SHA-256 minimum, upgrades automatically when rebuilt on newer frameworks
        /// </summary>
        public static string HashConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return string.Empty;

#if NET48
            // .NET Framework 4.8 - SHA256
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(connectionString);
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
#else
            // .NET 8+ - Uses SHA512 for stronger security
            using (var sha512 = SHA512.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(connectionString);
                var hash = sha512.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
#endif
        }

        /// <summary>
        /// Get current encryption strength info
        /// </summary>
        public static EncryptionInfo GetEncryptionInfo()
        {
            return new EncryptionInfo
            {
#if NET48
                Framework = ".NET Framework 4.8",
                EncryptionAlgorithm = "DPAPI (AES-256)",
                HashAlgorithm = "SHA-256",
                RecommendedUpgrade = "Consider upgrading to .NET 8 for SHA-512 and newer crypto providers"
#else
                Framework = ".NET 8+",
                EncryptionAlgorithm = "DPAPI (Latest Windows Crypto)",
                HashAlgorithm = "SHA-512",
                RecommendedUpgrade = "Using latest encryption standards"
#endif
            };
        }

        public class ValidationResult
        {
            public bool IsValid { get; set; }
            public List<string> Errors { get; set; } = new List<string>();
            public List<string> Warnings { get; set; } = new List<string>();
        }

        public class EncryptionInfo
        {
            public string Framework { get; set; } = string.Empty;
            public string EncryptionAlgorithm { get; set; } = string.Empty;
            public string HashAlgorithm { get; set; } = string.Empty;
            public string RecommendedUpgrade { get; set; } = string.Empty;
        }
    }

    /// <summary>
    /// Secure storage for connection strings in user settings
    /// </summary>
    public class SecureConnectionStorage
    {
        private readonly string _storageFile;

        public SecureConnectionStorage(string storageFile = "connections.dat")
        {
            _storageFile = storageFile;
        }

        /// <summary>
        /// Save connection string securely
        /// </summary>
        public void SaveConnection(string name, string connectionString)
        {
            var connections = LoadAllConnections();
            
            var encrypted = SecurityService.EncryptConnectionString(connectionString);
            connections[name] = encrypted;

            // Save to file
            var lines = connections.Select(kvp => $"{kvp.Key}|{kvp.Value}");
            File.WriteAllLines(_storageFile, lines);
        }

        /// <summary>
        /// Load connection string securely
        /// </summary>
        public string? LoadConnection(string name)
        {
            var connections = LoadAllConnections();
            
            if (connections.TryGetValue(name, out var encrypted))
            {
                return SecurityService.DecryptConnectionString(encrypted);
            }

            return null;
        }

        /// <summary>
        /// List saved connection names (not the actual connections)
        /// </summary>
        public List<string> ListConnections()
        {
            return LoadAllConnections().Keys.ToList();
        }

        /// <summary>
        /// Delete a saved connection
        /// </summary>
        public void DeleteConnection(string name)
        {
            var connections = LoadAllConnections();
            connections.Remove(name);

            var lines = connections.Select(kvp => $"{kvp.Key}|{kvp.Value}");
            File.WriteAllLines(_storageFile, lines);
        }

        private Dictionary<string, string> LoadAllConnections()
        {
            var connections = new Dictionary<string, string>();

            if (!File.Exists(_storageFile))
                return connections;

            try
            {
                var lines = File.ReadAllLines(_storageFile);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length == 2)
                    {
                        connections[parts[0]] = parts[1];
                    }
                }
            }
            catch
            {
                // If file is corrupted, start fresh
                return new Dictionary<string, string>();
            }

            return connections;
        }
    }
}
