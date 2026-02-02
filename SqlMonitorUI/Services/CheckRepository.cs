using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SqlCheckLibrary.Models;

namespace SqlCheckLibrary.Services
{
    /// <summary>
    /// Loads and manages SQL checks from JSON file
    /// </summary>
    public class CheckRepository
    {
        private readonly string _checksFilePath;
        private List<SqlCheck> _checks = new();

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

        public CheckRepository(string checksFilePath = "sql-checks.json")
        {
            // Ensure path is in app directory for persistence
            if (!Path.IsPathRooted(checksFilePath))
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                _checksFilePath = Path.Combine(appDir, checksFilePath);
            }
            else
            {
                _checksFilePath = checksFilePath;
            }
        }

        /// <summary>
        /// Load checks from JSON file
        /// </summary>
        public async Task LoadChecksAsync()
        {
            if (!File.Exists(_checksFilePath))
            {
                // Create default checks file if it doesn't exist
                await CreateDefaultChecksFileAsync();
            }

            var json = await ReadAllTextAsync(_checksFilePath);
            _checks = JsonSerializer.Deserialize<List<SqlCheck>>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true,
                WriteIndented = true 
            }) ?? new List<SqlCheck>();
        }

        /// <summary>
        /// Get all checks
        /// </summary>
        public List<SqlCheck> GetAllChecks() => _checks;

        /// <summary>
        /// Get checks by category
        /// </summary>
        public List<SqlCheck> GetChecksByCategory(string category) 
            => _checks.Where(c => c.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();

        /// <summary>
        /// Get enabled checks only
        /// </summary>
        public List<SqlCheck> GetEnabledChecks() 
            => _checks.Where(c => c.Enabled).ToList();

        /// <summary>
        /// Save checks back to JSON file
        /// </summary>
        public async Task SaveChecksAsync()
        {
            var json = JsonSerializer.Serialize(_checks, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            await WriteAllTextAsync(_checksFilePath, json);
        }

        /// <summary>
        /// Create a default checks file with sample checks
        /// </summary>
        private async Task CreateDefaultChecksFileAsync()
        {
            _checks = GetDefaultChecks();
            await SaveChecksAsync();
        }

        /// <summary>
        /// Get default set of checks (from sp_blitz and SQL Tiger Team)
        /// </summary>
        private List<SqlCheck> GetDefaultChecks()
        {
            return new List<SqlCheck>
            {
                new SqlCheck
                {
                    Id = "BACKUP_001",
                    Name = "Full Backup Recency",
                    Description = "Checks if any database hasn't had a full backup in the last 7 days",
                    Category = "Backup",
                    Severity = "Critical",
                    SqlQuery = @"
                        SELECT CASE 
                            WHEN EXISTS (
                                SELECT 1 
                                FROM sys.databases d
                                LEFT JOIN msdb.dbo.backupset b ON d.name = b.database_name AND b.type = 'D'
                                WHERE d.database_id > 4 
                                AND d.state = 0
                                AND (b.backup_finish_date IS NULL OR b.backup_finish_date < DATEADD(DAY, -7, GETDATE()))
                            ) THEN 1
                            ELSE 0
                        END",
                    ExpectedValue = 0,
                    Enabled = true,
                    RecommendedAction = "Schedule full backups for databases that haven't been backed up in 7+ days"
                },
                new SqlCheck
                {
                    Id = "BACKUP_002",
                    Name = "Transaction Log Backup Recency",
                    Description = "Checks if any database in FULL recovery hasn't had a log backup in 2 hours",
                    Category = "Backup",
                    Severity = "Critical",
                    SqlQuery = @"
                        SELECT CASE 
                            WHEN EXISTS (
                                SELECT 1 
                                FROM sys.databases d
                                LEFT JOIN msdb.dbo.backupset b ON d.name = b.database_name AND b.type = 'L'
                                WHERE d.database_id > 4 
                                AND d.recovery_model = 1
                                AND d.state = 0
                                AND (b.backup_finish_date IS NULL OR b.backup_finish_date < DATEADD(HOUR, -2, GETDATE()))
                            ) THEN 1
                            ELSE 0
                        END",
                    ExpectedValue = 0,
                    Enabled = true,
                    RecommendedAction = "Configure transaction log backups for databases in FULL recovery model"
                },
                new SqlCheck
                {
                    Id = "CORRUPTION_001",
                    Name = "Database Corruption Detected",
                    Description = "Checks for suspect pages indicating corruption",
                    Category = "Integrity",
                    Severity = "Critical",
                    SqlQuery = @"
                        SELECT CASE 
                            WHEN EXISTS (SELECT 1 FROM msdb.dbo.suspect_pages WHERE event_type IN (1, 2, 3))
                            THEN 1
                            ELSE 0
                        END",
                    ExpectedValue = 0,
                    Enabled = true,
                    RecommendedAction = "Run DBCC CHECKDB immediately and restore from backups if needed"
                },
                new SqlCheck
                {
                    Id = "PERFORMANCE_001",
                    Name = "High Index Fragmentation",
                    Description = "Checks if any indexes have fragmentation > 30%",
                    Category = "Performance",
                    Severity = "Warning",
                    SqlQuery = @"
                        SELECT CASE 
                            WHEN EXISTS (
                                SELECT 1 
                                FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ips
                                WHERE ips.avg_fragmentation_in_percent > 30
                                AND ips.page_count > 1000
                                AND ips.index_id > 0
                            ) THEN 1
                            ELSE 0
                        END",
                    ExpectedValue = 0,
                    Enabled = true,
                    RecommendedAction = "Consider rebuilding or reorganizing fragmented indexes"
                },
                new SqlCheck
                {
                    Id = "PERFORMANCE_002",
                    Name = "Missing Index Recommendations",
                    Description = "Checks if there are missing index recommendations with high impact",
                    Category = "Performance",
                    Severity = "Info",
                    SqlQuery = @"
                        SELECT CASE 
                            WHEN EXISTS (
                                SELECT 1 
                                FROM sys.dm_db_missing_index_details mid
                                INNER JOIN sys.dm_db_missing_index_groups mig ON mid.index_handle = mig.index_handle
                                INNER JOIN sys.dm_db_missing_index_group_stats migs ON mig.index_group_handle = migs.group_handle
                                WHERE migs.avg_user_impact > 50
                            ) THEN 1
                            ELSE 0
                        END",
                    ExpectedValue = 0,
                    Enabled = true,
                    RecommendedAction = "Review missing index recommendations and create appropriate indexes"
                },
                new SqlCheck
                {
                    Id = "SECURITY_001",
                    Name = "SA Account Enabled",
                    Description = "Checks if the SA account is enabled",
                    Category = "Security",
                    Severity = "Warning",
                    SqlQuery = @"
                        SELECT CASE 
                            WHEN EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = 'sa' AND is_disabled = 0)
                            THEN 1
                            ELSE 0
                        END",
                    ExpectedValue = 0,
                    Enabled = true,
                    RecommendedAction = "Disable the SA account and use named admin accounts instead"
                },
                new SqlCheck
                {
                    Id = "SECURITY_002",
                    Name = "Weak Password Policies",
                    Description = "Checks if password policy enforcement is disabled for SQL logins",
                    Category = "Security",
                    Severity = "Warning",
                    SqlQuery = @"
                        SELECT CASE 
                            WHEN EXISTS (
                                SELECT 1 
                                FROM sys.sql_logins 
                                WHERE is_policy_checked = 0 
                                AND name NOT IN ('sa', '##MS_PolicyEventProcessingLogin##')
                            ) THEN 1
                            ELSE 0
                        END",
                    ExpectedValue = 0,
                    Enabled = true,
                    RecommendedAction = "Enable password policy enforcement for all SQL logins"
                },
                new SqlCheck
                {
                    Id = "CONFIG_001",
                    Name = "Auto Close Enabled",
                    Description = "Checks if any database has AUTO_CLOSE enabled (performance issue)",
                    Category = "Configuration",
                    Severity = "Warning",
                    SqlQuery = @"
                        SELECT CASE 
                            WHEN EXISTS (SELECT 1 FROM sys.databases WHERE is_auto_close_on = 1 AND database_id > 4)
                            THEN 1
                            ELSE 0
                        END",
                    ExpectedValue = 0,
                    Enabled = true,
                    RecommendedAction = "Disable AUTO_CLOSE on all user databases"
                },
                new SqlCheck
                {
                    Id = "CONFIG_002",
                    Name = "Auto Shrink Enabled",
                    Description = "Checks if any database has AUTO_SHRINK enabled (causes fragmentation)",
                    Category = "Configuration",
                    Severity = "Warning",
                    SqlQuery = @"
                        SELECT CASE 
                            WHEN EXISTS (SELECT 1 FROM sys.databases WHERE is_auto_shrink_on = 1 AND database_id > 4)
                            THEN 1
                            ELSE 0
                        END",
                    ExpectedValue = 0,
                    Enabled = true,
                    RecommendedAction = "Disable AUTO_SHRINK on all user databases"
                },
                new SqlCheck
                {
                    Id = "TEMPDB_001",
                    Name = "TempDB File Count",
                    Description = "Checks if TempDB has fewer files than CPU cores (up to 8)",
                    Category = "Configuration",
                    Severity = "Info",
                    SqlQuery = @"
                        DECLARE @cpu_count INT = (SELECT cpu_count FROM sys.dm_os_sys_info);
                        DECLARE @target_files INT = CASE WHEN @cpu_count > 8 THEN 8 ELSE @cpu_count END;
                        DECLARE @current_files INT = (SELECT COUNT(*) FROM sys.master_files WHERE database_id = 2 AND type = 0);
                        
                        SELECT CASE 
                            WHEN @current_files < @target_files THEN 1
                            ELSE 0
                        END",
                    ExpectedValue = 0,
                    Enabled = true,
                    RecommendedAction = "Add more TempDB data files to match CPU cores (up to 8 files)"
                },
                new SqlCheck
                {
                    Id = "GROWTH_001",
                    Name = "Percentage Growth Settings",
                    Description = "Checks if any database files use percentage-based growth (not recommended)",
                    Category = "Configuration",
                    Severity = "Warning",
                    SqlQuery = @"
                        SELECT CASE 
                            WHEN EXISTS (
                                SELECT 1 
                                FROM sys.master_files 
                                WHERE is_percent_growth = 1 
                                AND database_id > 4
                            ) THEN 1
                            ELSE 0
                        END",
                    ExpectedValue = 0,
                    Enabled = true,
                    RecommendedAction = "Change database file growth from percentage to fixed MB increments"
                },
                new SqlCheck
                {
                    Id = "VLF_001",
                    Name = "Excessive VLF Count",
                    Description = "Checks if any database has excessive Virtual Log Files (>50)",
                    Category = "Performance",
                    Severity = "Warning",
                    SqlQuery = @"
                        IF OBJECT_ID('tempdb..#VLFInfo') IS NOT NULL DROP TABLE #VLFInfo;
                        CREATE TABLE #VLFInfo (
                            RecoveryUnitID INT,
                            FileID INT,
                            FileSize BIGINT,
                            StartOffset BIGINT,
                            FSeqNo BIGINT,
                            Status INT,
                            Parity INT,
                            CreateLSN NUMERIC(38)
                        );
                        
                        INSERT INTO #VLFInfo
                        EXEC sp_executesql N'DBCC LOGINFO() WITH NO_INFOMSGS';
                        
                        SELECT CASE 
                            WHEN COUNT(*) > 50 THEN 1
                            ELSE 0
                        END
                        FROM #VLFInfo;
                        
                        DROP TABLE #VLFInfo;",
                    ExpectedValue = 0,
                    Enabled = true,
                    RecommendedAction = "Shrink and re-grow log file to reduce VLF count"
                }
            };
        }
    }
}
