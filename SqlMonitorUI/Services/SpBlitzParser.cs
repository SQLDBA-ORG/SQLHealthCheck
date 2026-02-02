using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using SqlCheckLibrary.Models;

namespace SqlCheckLibrary.Services
{
    /// <summary>
    /// Parses sp_Blitz.sql file and extracts checks
    /// </summary>
    public class SpBlitzParser
    {
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

        public class BlitzCheck
        {
            public int CheckID { get; set; }
            public int Priority { get; set; }
            public string FindingsGroup { get; set; } = string.Empty;
            public string Finding { get; set; } = string.Empty;
            public string URL { get; set; } = string.Empty;
        }

        /// <summary>
        /// Parse sp_Blitz.sql file and extract check definitions
        /// </summary>
        public async Task<List<BlitzCheck>> ParseSpBlitzFile(string filePath)
        {
            var checks = new List<BlitzCheck>();
            var content = await ReadAllTextAsync(filePath);


            // Extract all unique CheckIDs first
            var checkIdMatches = Regex.Matches(content, @"CheckID\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            //var results = checkIdMatches.OfType<Match>().Select(m => m.Value);
            var checkIds = checkIdMatches.OfType<Match>().Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(x => x).ToList();

            // For each CheckID, try to find associated information
            foreach (var checkId in checkIds)
            {
                try
                {
                    var check = ExtractCheckInfo(content, checkId);
                    if (check != null)
                    {
                        checks.Add(check);
                    }
                }
                catch
                {
                    // If we can't extract full info, create a minimal check
                    checks.Add(new BlitzCheck
                    {
                        CheckID = checkId,
                        Priority = 100,
                        FindingsGroup = "Unknown",
                        Finding = $"Check {checkId}",
                        URL = "https://www.brentozar.com/blitz/"
                    });
                }
            }

            return checks;
        }

        /// <summary>
        /// Extract detailed check information for a specific CheckID
        /// </summary>
        private BlitzCheck? ExtractCheckInfo(string content, int checkId)
        {
            // Strategy: Find the INSERT INTO #BlitzResults near this CheckID
            // Pattern matches multiple styles of inserts in sp_Blitz
            
            // Look for a section containing this CheckID
            var checkIdPattern = $@"CheckID\s*=\s*{checkId}[^;]{{0,2000}}";
            var match = Regex.Match(content, checkIdPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            
            if (!match.Success)
                return null;

            var section = match.Value;

            // Extract Priority
            var priorityMatch = Regex.Match(section, @"Priority\s*=\s*(\d+)|(\d+)\s+AS\s+Priority", RegexOptions.IgnoreCase);
            var priority = priorityMatch.Success ? 
                int.Parse(priorityMatch.Groups[1].Success ? priorityMatch.Groups[1].Value : priorityMatch.Groups[2].Value) : 100;

            // Extract FindingsGroup
            var groupMatch = Regex.Match(section, @"FindingsGroup\s*=\s*'([^']+)'|'([^']+)'\s+AS\s+FindingsGroup", RegexOptions.IgnoreCase);
            var findingsGroup = groupMatch.Success ?
                (groupMatch.Groups[1].Success ? groupMatch.Groups[1].Value : groupMatch.Groups[2].Value) : "Unknown";

            // Extract Finding
            var findingMatch = Regex.Match(section, @"Finding\s*=\s*'([^']+)'|'([^']+)'\s+AS\s+Finding", RegexOptions.IgnoreCase);
            var finding = findingMatch.Success ?
                (findingMatch.Groups[1].Success ? findingMatch.Groups[1].Value : findingMatch.Groups[2].Value) : $"Check {checkId}";

            // Extract URL
            var urlMatch = Regex.Match(section, @"URL\s*=\s*'([^']+)'|'([^']+)'\s+AS\s+URL", RegexOptions.IgnoreCase);
            var url = urlMatch.Success ?
                (urlMatch.Groups[1].Success ? urlMatch.Groups[1].Value : urlMatch.Groups[2].Value) : "https://www.brentozar.com/blitz/";

            return new BlitzCheck
            {
                CheckID = checkId,
                Priority = priority,
                FindingsGroup = findingsGroup,
                Finding = finding,
                URL = url
            };
        }

        /// <summary>
        /// Convert BlitzCheck to our SqlCheck format with simplified queries
        /// </summary>
        public List<SqlCheck> ConvertToSqlChecks(List<BlitzCheck> blitzChecks, string source = "sp_Blitz")
        {
            var sqlChecks = new List<SqlCheck>();

            foreach (var blitz in blitzChecks)
            {
                var check = new SqlCheck
                {
                    Id = $"{source.ToUpper().Replace("_", "").Replace("SP", "")}_{blitz.CheckID:D3}",
                    Name = blitz.Finding,
                    Description = $"{blitz.Finding} ({source} CheckID {blitz.CheckID}, Priority {blitz.Priority})",
                    Category = MapCategory(blitz.FindingsGroup),
                    Severity = MapSeverity(blitz.Priority),
                    SqlQuery = GenerateSimplifiedQuery(blitz, source),
                    ExpectedValue = 0,
                    Enabled = ShouldEnableByDefault(blitz.Priority),
                    RecommendedAction = $"Review finding: {blitz.Finding}. More info: {blitz.URL}",
                    Source = source
                };

                sqlChecks.Add(check);
            }

            return sqlChecks;
        }

        /// <summary>
        /// Map sp_Blitz FindingsGroup to our categories
        /// </summary>
        private string MapCategory(string findingsGroup)
        {
            return findingsGroup switch
            {
                "Backup" => "Backup",
                "Security" => "Security",
                "Performance" => "Performance",
                "Reliability" => "Reliability",
                "Server Info" => "Configuration",
                "Wait Stats" => "Performance",
                "File Configuration" => "Configuration",
                "Informational" => "Info",
                _ => "Configuration"
            };
        }

        /// <summary>
        /// Map sp_Blitz priority to severity
        /// Priority 1-50 = Critical, 51-100 = Warning, 101+ = Info
        /// </summary>
        private string MapSeverity(int priority)
        {
            if (priority <= 50)
                return "Critical";
            else if (priority <= 100)
                return "Warning";
            else
                return "Info";
        }

        /// <summary>
        /// Enable by default only for priority 1-100
        /// </summary>
        private bool ShouldEnableByDefault(int priority)
        {
            return priority <= 100;
        }

        /// <summary>
        /// Generate a simplified check query based on the check type
        /// This maps known sp_Blitz checks to simplified 0/1 queries
        /// </summary>
        private string GenerateSimplifiedQuery(BlitzCheck blitz, string source)
        {
            // Map known checks to simple queries
            // These are simplified versions of sp_Blitz's complex checks
            var simplifiedQueries = new Dictionary<int, string>
            {
                // BACKUP CHECKS
                [1] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases d LEFT JOIN msdb.dbo.backupset b ON d.name = b.database_name AND b.type = 'D' WHERE d.database_id > 4 AND d.state = 0 AND (b.backup_finish_date IS NULL OR b.backup_finish_date < DATEADD(DAY, -7, GETDATE()))) THEN 1 ELSE 0 END",
                [2] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases d WHERE d.database_id > 4 AND d.recovery_model = 1 AND d.state = 0 AND NOT EXISTS (SELECT 1 FROM msdb.dbo.backupset b WHERE b.database_name = d.name AND b.type = 'L' AND b.backup_finish_date > DATEADD(HOUR, -2, GETDATE()))) THEN 1 ELSE 0 END",
                [3] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM msdb.dbo.backupset WHERE backup_start_date < DATEADD(DAY, -60, GETDATE())) THEN 1 ELSE 0 END",
                [93] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.master_files mf INNER JOIN msdb.dbo.backupset b ON LEFT(mf.physical_name, 3) = LEFT(b.physical_device_name, 3) WHERE mf.database_id > 4 AND b.backup_finish_date >= DATEADD(DAY, -14, GETDATE())) THEN 1 ELSE 0 END",
                
                // PERFORMANCE CHECKS  
                [21] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases WHERE is_auto_shrink_on = 1 AND database_id > 4) THEN 1 ELSE 0 END",
                [50] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.configurations WHERE name = 'max server memory (MB)' AND value_in_use = 2147483647) THEN 1 ELSE 0 END",
                [51] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.configurations c1 INNER JOIN sys.configurations c2 ON c1.value_in_use = c2.value_in_use WHERE c1.name = 'min server memory (MB)' AND c2.name = 'max server memory (MB)' AND c1.value_in_use > 0) THEN 1 ELSE 0 END",
                [90] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases WHERE is_auto_close_on = 1 AND database_id > 4) THEN 1 ELSE 0 END",
                [83] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases WHERE is_auto_create_stats_on = 0 AND database_id > 4) THEN 1 ELSE 0 END",
                [84] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases WHERE is_auto_update_stats_on = 0 AND database_id > 4) THEN 1 ELSE 0 END",
                
                // TEMPDB CHECKS
                [40] = "SELECT CASE WHEN (SELECT COUNT(*) FROM sys.master_files WHERE database_id = 2 AND type = 0) = 1 THEN 1 ELSE 0 END",
                [183] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.master_files WHERE database_id = 2 AND type = 0 GROUP BY size HAVING COUNT(*) != (SELECT COUNT(*) FROM sys.master_files WHERE database_id = 2 AND type = 0)) THEN 1 ELSE 0 END",
                [170] = "SELECT CASE WHEN (SELECT COUNT(*) FROM sys.master_files WHERE database_id = 2 AND type = 0) < (SELECT cpu_count FROM sys.dm_os_sys_info WHERE cpu_count <= 8) THEN 1 ELSE 0 END",
                
                // SECURITY CHECKS
                [71] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.sql_logins WHERE is_policy_checked = 0 AND name NOT IN ('sa', '##MS_PolicyEventProcessingLogin##')) THEN 1 ELSE 0 END",
                [72] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.sql_logins WHERE is_expiration_checked = 0 AND name NOT IN ('sa', '##MS_PolicyEventProcessingLogin##')) THEN 1 ELSE 0 END",
                [73] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = 'sa' AND is_disabled = 0) THEN 1 ELSE 0 END",
                [119] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.dm_database_encryption_keys WHERE encryption_state = 3) AND NOT EXISTS (SELECT 1 FROM sys.certificates WHERE pvt_key_last_backup_date IS NOT NULL) THEN 1 ELSE 0 END",
                
                // CONFIGURATION CHECKS
                [26] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.master_files WHERE growth = 0 AND database_id > 4) THEN 1 ELSE 0 END",
                [27] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.master_files WHERE is_percent_growth = 1 AND database_id > 4) THEN 1 ELSE 0 END",
                [61] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.master_files WHERE database_id IN (1,2,3,4) AND physical_name LIKE 'C:%') THEN 1 ELSE 0 END",
                [62] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.master_files WHERE database_id > 4 AND physical_name LIKE 'C:%') THEN 1 ELSE 0 END",
                [94] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases WHERE compatibility_level < (SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) * 10 + 100) AND database_id > 4) THEN 1 ELSE 0 END",
                [126] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.configurations WHERE name = 'priority boost' AND value_in_use = 1) THEN 1 ELSE 0 END",
                
                // INTEGRITY/CORRUPTION CHECKS
                [89] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM msdb.dbo.suspect_pages WHERE event_type IN (1, 2, 3)) THEN 1 ELSE 0 END",
                [6] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases WHERE page_verify_option_desc != 'CHECKSUM' AND database_id > 4) THEN 1 ELSE 0 END",
                
                // RELIABILITY CHECKS
                [57] = "SELECT CASE WHEN (SELECT status_desc FROM sys.dm_server_services WHERE servicename LIKE 'SQL Server Agent%') != 'Running' THEN 1 ELSE 0 END",
                [67] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases WHERE database_id > 4 AND owner_sid != 0x01) THEN 1 ELSE 0 END",
                
                // VLF CHECK
                [69] = "DECLARE @VLFCount INT; IF OBJECT_ID('tempdb..#VLFInfo') IS NOT NULL DROP TABLE #VLFInfo; CREATE TABLE #VLFInfo (RecoveryUnitID INT, FileID INT, FileSize BIGINT, StartOffset BIGINT, FSeqNo BIGINT, Status INT, Parity INT, CreateLSN NUMERIC(38)); INSERT INTO #VLFInfo EXEC sp_executesql N'DBCC LOGINFO() WITH NO_INFOMSGS'; SELECT @VLFCount = COUNT(*) FROM #VLFInfo; DROP TABLE #VLFInfo; SELECT CASE WHEN @VLFCount > 50 THEN 1 ELSE 0 END",
                
                // INDEX CHECKS  
                [33] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') WHERE avg_fragmentation_in_percent > 30 AND page_count > 1000 AND index_id > 0) THEN 1 ELSE 0 END",
                [34] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.dm_db_missing_index_details mid INNER JOIN sys.dm_db_missing_index_groups mig ON mid.index_handle = mig.index_handle INNER JOIN sys.dm_db_missing_index_group_stats migs ON mig.index_group_handle = migs.group_handle WHERE migs.avg_user_impact > 50) THEN 1 ELSE 0 END",
                
                // EDITION/VERSION CHECKS
                [55] = "SELECT CASE WHEN CAST(SERVERPROPERTY('Edition') AS VARCHAR(100)) LIKE '%Express%' OR CAST(SERVERPROPERTY('Edition') AS VARCHAR(100)) LIKE '%Web%' THEN 1 ELSE 0 END",
                [59] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.dm_os_performance_counters WHERE object_name LIKE '%Deprecated%' AND cntr_value > 0) THEN 1 ELSE 0 END",
                
                // FILE SIZE CHECKS
                [102] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.master_files WHERE max_size = -1 AND database_id > 4 AND type = 0) THEN 1 ELSE 0 END",
                
                // RECOVERY MODEL CHECKS
                [28] = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases WHERE recovery_model = 3 AND database_id > 4) THEN 1 ELSE 0 END",
            };

            if (simplifiedQueries.ContainsKey(blitz.CheckID))
            {
                return simplifiedQueries[blitz.CheckID];
            }

            // Default fallback query - returns 0 (check not implemented yet)
            return $"SELECT 0 AS Result -- {source} CheckID {blitz.CheckID}: {blitz.Finding} (query not yet implemented - see {source}.sql for original query)";
        }

        /// <summary>
        /// Merge new checks with existing checks from repository
        /// </summary>
        public List<SqlCheck> MergeChecks(List<SqlCheck> existingChecks, List<SqlCheck> newChecks)
        {
            var merged = new List<SqlCheck>(existingChecks);

            foreach (var newCheck in newChecks)
            {
                // Check if this check ID already exists
                var existing = merged.FirstOrDefault(c => c.Id == newCheck.Id);
                
                if (existing != null)
                {
                    // Update existing check (keep user's enabled state)
                    existing.Name = newCheck.Name;
                    existing.Description = newCheck.Description;
                    existing.Category = newCheck.Category;
                    existing.Severity = newCheck.Severity;
                    existing.SqlQuery = newCheck.SqlQuery;
                    existing.RecommendedAction = newCheck.RecommendedAction;
                    // Keep existing.Enabled as user set it
                }
                else
                {
                    // Add new check
                    merged.Add(newCheck);
                }
            }

            return merged.OrderBy(c => c.Category).ThenBy(c => c.Id).ToList();
        }

        /// <summary>
        /// Parse sp_triage file and extract check categories
        /// sp_triage works differently - it creates output tables for different check categories
        /// </summary>
        public async Task<List<BlitzCheck>> ParseSpTriageFile(string filePath)
        {
            var checks = new List<BlitzCheck>();
            var content = await ReadAllTextAsync(filePath);

            // sp_triage creates multiple output tables like #output_sqldba_org_sp_triage_[CategoryName]
            var pattern = @"CREATE TABLE #output_sqldba_org_sp_triage_(\w+)";
            var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);

            var checkId = 1;
            foreach (Match match in matches)
            {
                var categoryName = match.Groups[1].Value;
                
                // Convert table name to friendly check name
                var friendlyName = ConvertTriageCategoryToFriendlyName(categoryName);
                var category = MapTriageCategoryToGroup(categoryName);

                checks.Add(new BlitzCheck
                {
                    CheckID = checkId++,
                    Priority = 100, // Default to info level
                    FindingsGroup = category,
                    Finding = friendlyName,
                    URL = "https://github.com/SQLDBA-ORG/sqldba/"
                });
            }

            return checks;
        }

        /// <summary>
        /// Convert sp_triage table names to friendly check names
        /// </summary>
        private string ConvertTriageCategoryToFriendlyName(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return "Review Check";

            // Convert snake_case or PascalCase to Title Case
            var words = Regex.Split(tableName, @"(?<!^)(?=[A-Z])|_");
            var friendly = string.Join(" ", words
                .Where(w => !string.IsNullOrEmpty(w))
                .Select(w => char.ToUpper(w[0]) + (w.Length > 1 ? w.Substring(1).ToLower() : "")));

            return string.IsNullOrWhiteSpace(friendly) ? "Review Check" : $"Review {friendly}";
        }

        /// <summary>
        /// Map sp_triage categories to our standard groups
        /// </summary>
        private string MapTriageCategoryToGroup(string tableName)
        {
            var lower = tableName.ToLower();
            
            if (lower.Contains("backup") || lower.Contains("log"))
                return "Backup";
            if (lower.Contains("security") || lower.Contains("login") || lower.Contains("permission"))
                return "Security";
            if (lower.Contains("index") || lower.Contains("query") || lower.Contains("wait") || 
                lower.Contains("performance") || lower.Contains("cpu") || lower.Contains("memory"))
                return "Performance";
            if (lower.Contains("config") || lower.Contains("setting"))
                return "Configuration";
            if (lower.Contains("heap") || lower.Contains("compression") || lower.Contains("size"))
                return "Storage";
            
            return "Information";
        }

        /// <summary>
        /// Save checks to JSON file
        /// </summary>
        public async Task SaveChecksToFile(List<SqlCheck> checks, string filePath)
        {
            var json = JsonSerializer.Serialize(checks, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            await WriteAllTextAsync(filePath, json);
        }
    }
}
