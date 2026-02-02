using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SqlCheckLibrary.Services
{
    /// <summary>
    /// Service for intelligent placeholder replacement in check messages
    /// Handles @ placeholders with row counts and creative formatting
    /// </summary>
    public static class PlaceholderService
    {
        private const string PLACEHOLDER = "@";

        /// <summary>
        /// Replace @ placeholder with actual value from query results
        /// Examples:
        /// - "@ databases have no backups" + count=3 → "3 databases have no backups"
        /// - "Default Config Changed on @ databases" + count=15 → "Default Config Changed on 15 databases"
        /// - "@ server(s)" + count=1 → "1 server"
        /// - "@ server(s)" + count=5 → "5 servers"
        /// </summary>
        public static string ReplacePlaceholder(string template, int count)
        {
            if (string.IsNullOrEmpty(template) || !template.Contains(PLACEHOLDER))
                return template;

            var result = template;

            // Replace @ with count
            result = result.Replace(PLACEHOLDER, count.ToString());

            // Handle (s) pluralization
            result = HandlePluralization(result, count);

            return result;
        }

        /// <summary>
        /// Replace @ placeholder with custom value (not just numbers)
        /// </summary>
        public static string ReplacePlaceholder(string template, string value)
        {
            if (string.IsNullOrEmpty(template) || !template.Contains(PLACEHOLDER))
                return template;

            return template.Replace(PLACEHOLDER, value);
        }

        /// <summary>
        /// Format check message based on result
        /// </summary>
        public static string FormatCheckMessage(
            SqlCheckLibrary.Models.SqlCheck check,
            bool passed,
            int? rowCount = null,
            string? customValue = null)
        {
            string message;

            if (passed)
            {
                // Use CheckCleared or ExpectedState
                message = !string.IsNullOrEmpty(check.CheckCleared)
                    ? check.CheckCleared
                    : check.ExpectedState;
            }
            else
            {
                // Use CheckTriggered
                message = check.CheckTriggered;
            }

            // Replace placeholder
            if (rowCount.HasValue && !string.IsNullOrEmpty(message))
            {
                message = ReplacePlaceholder(message, rowCount.Value);
            }
            else if (!string.IsNullOrEmpty(customValue) && !string.IsNullOrEmpty(message))
            {
                message = ReplacePlaceholder(message, customValue);
            }

            // Fallback to generic message
            if (string.IsNullOrEmpty(message))
            {
                message = passed
                    ? "Check passed"
                    : $"Check failed: {check.Name}";
            }

            return message;
        }

        /// <summary>
        /// Intelligently handle (s) pluralization
        /// Examples:
        /// - "5 database(s)" → "5 databases"
        /// - "1 database(s)" → "1 database"
        /// - "3 server(s)" → "3 servers"
        /// - "1 server(s)" → "1 server"
        /// </summary>
        private static string HandlePluralization(string text, int count)
        {
            if (count == 1)
            {
                // Remove (s) for singular
                text = Regex.Replace(text, @"\(s\)", "", RegexOptions.IgnoreCase);
            }
            else
            {
                // Replace (s) with s for plural
                text = Regex.Replace(text, @"\(s\)", "s", RegexOptions.IgnoreCase);
            }

            return text;
        }

        /// <summary>
        /// Get creative message based on severity and count
        /// </summary>
        public static string GetCreativeMessage(
            string baseMessage,
            int count,
            string severity,
            string category)
        {
            var message = ReplacePlaceholder(baseMessage, count);

            // Add emoji or emphasis based on severity
            var prefix = severity.ToLower() switch
            {
                "critical" => "🔴 CRITICAL: ",
                "warning" => "⚠️ WARNING: ",
                "info" => "ℹ️ INFO: ",
                _ => ""
            };

            // Add context based on count
            var suffix = count switch
            {
                0 => " ✓",
                1 => "",
                > 100 => " (extensive)",
                > 50 => " (many items)",
                > 10 => " (significant)",
                _ => ""
            };

            return $"{prefix}{message}{suffix}";
        }

        /// <summary>
        /// Extract count from message with @ placeholder
        /// Reverse operation - extract what @ was replaced with
        /// </summary>
        public static int? ExtractCountFromMessage(string original, string replaced)
        {
            if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(replaced))
                return null;

            // Find where @ was in original
            var atIndex = original.IndexOf(PLACEHOLDER);
            if (atIndex < 0)
                return null;

            // Extract the numeric part from replaced at that position
            var match = Regex.Match(
                replaced.Substring(atIndex),
                @"^(\d+)");

            if (match.Success && int.TryParse(match.Groups[1].Value, out int count))
                return count;

            return null;
        }

        /// <summary>
        /// Build detailed message with recommendations
        /// </summary>
        public static string BuildDetailedMessage(
            SqlCheckLibrary.Models.SqlCheck check,
            bool passed,
            int? rowCount = null)
        {
            var mainMessage = FormatCheckMessage(check, passed, rowCount);

            if (passed)
            {
                return mainMessage;
            }

            // Failed - add recommendations
            var details = new List<string> { mainMessage };

            if (!string.IsNullOrEmpty(check.DetailedRemediation))
            {
                details.Add("");
                details.Add("Recommended Actions:");
                details.Add(check.DetailedRemediation);
            }
            else if (!string.IsNullOrEmpty(check.RecommendedAction))
            {
                details.Add("");
                details.Add("Recommendation:");
                details.Add(check.RecommendedAction);
            }

            if (check.Priority <= 2)
            {
                details.Add("");
                details.Add("⚡ HIGH PRIORITY - Take immediate action");
            }

            return string.Join(Environment.NewLine, details);
        }

        /// <summary>
        /// Generate summary statistics message
        /// </summary>
        public static string GenerateSummary(List<(string message, int count)> items)
        {
            if (items == null || items.Count == 0)
                return "No issues found";

            var total = items.Sum(x => x.count);
            var details = items
                .Where(x => x.count > 0)
                .Select(x => $"  • {ReplacePlaceholder(x.message, x.count)}")
                .ToList();

            if (details.Count == 0)
                return "All checks passed ✓";

            var summary = new List<string>
            {
                $"Found {total} issue(s):",
                ""
            };

            summary.AddRange(details);

            return string.Join(Environment.NewLine, summary);
        }
    }
}
