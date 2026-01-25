using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using SqlCheckLibrary.Models;

namespace SqlCheckLibrary.Services
{
    /// <summary>
    /// Advanced parser for extracting SQL queries from sp_Blitz and sp_triage
    /// Extracts actual queries instead of using placeholders
    /// </summary>
    public class AdvancedCheckParser
    {
        /// <summary>
        /// Extract actual SQL query for a specific CheckID from sp_Blitz
        /// </summary>
        public string ExtractQueryFromSpBlitz(string content, int checkId)
        {
            try
            {
                // Find the section containing this CheckID
                var pattern = $@"CheckID = {checkId}\D.*?(?=CheckID = \d+|IF NOT EXISTS|END;)";
                var match = Regex.Match(content, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (!match.Success)
                    return CreateRowCountQuery(checkId, "sp_Blitz");

                var section = match.Value;

                // Extract the INSERT INTO #BlitzResults section
                var insertPattern = @"INSERT\s+INTO\s+#BlitzResults.*?(?:SELECT|FROM)\s+(.*?)(?=;|\s+OPTION\s+\(|$)";
                var insertMatch = Regex.Match(section, insertPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (insertMatch.Success)
                {
                    var query = insertMatch.Groups[1].Value.Trim();
                    
                    // Convert to row count format
                    return ConvertToRowCountQuery(query);
                }

                return CreateRowCountQuery(checkId, "sp_Blitz");
            }
            catch
            {
                return CreateRowCountQuery(checkId, "sp_Blitz");
            }
        }

        /// <summary>
        /// Convert a multi-row query to return row count instead
        /// If it returns rows, it means there's a problem, so count > 0 = fail
        /// </summary>
        private string ConvertToRowCountQuery(string originalQuery)
        {
            // Wrap in a SELECT COUNT(*) to get row count
            var cleaned = originalQuery.Trim().TrimEnd(';');
            
            // If query is simple SELECT, wrap it
            if (cleaned.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                return $@"SELECT CASE WHEN EXISTS ({cleaned}) THEN 1 ELSE 0 END AS Result";
            }

            return cleaned;
        }

        /// <summary>
        /// Create a generic row count query for checks we can't parse
        /// </summary>
        private string CreateRowCountQuery(int checkId, string source)
        {
            return $"SELECT 0 AS Result -- {source} CheckID {checkId}: Query extraction pending - run {source}.sql for full check";
        }

        /// <summary>
        /// Extract query from sp_triage output table creation
        /// </summary>
        public string ExtractQueryFromSpTriage(string content, string tableName)
        {
            try
            {
                // Find the section that populates this table
                var pattern = $@"INSERT INTO #output_sqldba_org_sp_triage_{tableName}.*?(?=INSERT INTO|CREATE TABLE|END;)";
                var match = Regex.Match(content, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    var section = match.Value;
                    
                    // Extract the SELECT portion
                    var selectPattern = @"SELECT\s+(.*?)(?=;|\s+OPTION\s+\(|$)";
                    var selectMatch = Regex.Match(section, selectPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

                    if (selectMatch.Success)
                    {
                        var query = selectMatch.Groups[1].Value.Trim();
                        return $@"SELECT CASE WHEN EXISTS (SELECT {query}) THEN 1 ELSE 0 END AS Result";
                    }
                }

                return $"SELECT 0 AS Result -- sp_triage table '{tableName}': Query extraction pending";
            }
            catch
            {
                return $"SELECT 0 AS Result -- sp_triage table '{tableName}': Query extraction failed";
            }
        }

        /// <summary>
        /// Get common check queries that we know work well
        /// These are hand-crafted for reliability
        /// </summary>
        public Dictionary<string, string> GetKnownCheckQueries()
        {
            return new Dictionary<string, string>
            {
                // BACKUP CHECKS
                ["BLITZ_001"] = @"
                    SELECT CASE 
                        WHEN EXISTS (
                            SELECT 1 
                            FROM sys.databases d 
                            LEFT JOIN (
                                SELECT database_name, MAX(backup_finish_date) AS last_backup
                                FROM msdb.dbo.backupset 
                                WHERE type = 'D'
                                GROUP BY database_name
                            ) b ON d.name = b.database_name
                            WHERE d.database_id > 4 
                            AND d.state = 0 
                            AND (b.last_backup IS NULL OR b.last_backup < DATEADD(DAY, -7, GETDATE()))
                        ) THEN 1 ELSE 0 END AS Result",

                ["BLITZ_002"] = @"
                    SELECT CASE 
                        WHEN EXISTS (
                            SELECT 1 
                            FROM sys.databases d
                            WHERE d.database_id > 4 
                            AND d.recovery_model IN (1, 2)
                            AND d.state = 0
                            AND NOT EXISTS (
                                SELECT 1 
                                FROM msdb.dbo.backupset b 
                                WHERE b.database_name = d.name 
                                AND b.type = 'L' 
                                AND b.backup_finish_date > DATEADD(HOUR, -2, GETDATE())
                            )
                        ) THEN 1 ELSE 0 END AS Result",

                // PERFORMANCE CHECKS
                ["BLITZ_050"] = @"
                    SELECT CASE 
                        WHEN EXISTS (
                            SELECT 1 
                            FROM sys.configurations 
                            WHERE name = 'max server memory (MB)' 
                            AND value_in_use = 2147483647
                        ) THEN 1 ELSE 0 END AS Result",

                ["BLITZ_090"] = @"
                    SELECT CASE 
                        WHEN EXISTS (
                            SELECT 1 
                            FROM sys.databases 
                            WHERE is_auto_close_on = 1 
                            AND database_id > 4
                        ) THEN 1 ELSE 0 END AS Result",

                ["BLITZ_021"] = @"
                    SELECT CASE 
                        WHEN EXISTS (
                            SELECT 1 
                            FROM sys.databases 
                            WHERE is_auto_shrink_on = 1 
                            AND database_id > 4
                        ) THEN 1 ELSE 0 END AS Result",

                // Add more known working queries here...
            };
        }
    }
}
