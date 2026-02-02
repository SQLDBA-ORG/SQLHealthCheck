namespace SqlCheckLibrary.Models
{
    /// <summary>
    /// Configuration for an embedded diagnostic script
    /// </summary>
    public class ScriptConfiguration
    {
        /// <summary>
        /// Unique identifier for the script
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the script
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description of what this script does
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Relative path to the script file
        /// </summary>
        public string ScriptPath { get; set; } = string.Empty;

        /// <summary>
        /// Execution parameters (e.g., "@Debug = 1")
        /// </summary>
        public string ExecutionParameters { get; set; } = string.Empty;

        /// <summary>
        /// SQL query to run after ExecutionParameters to get CSV output data.
        /// If empty, the ExecutionParameters result is used for CSV export.
        /// </summary>
        public string SqlQueryForOutput { get; set; } = string.Empty;

        /// <summary>
        /// Whether to run this script during complete health check
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Timeout in seconds (0 = no timeout)
        /// </summary>
        public int TimeoutSeconds { get; set; } = 300;

        /// <summary>
        /// Category for organization
        /// </summary>
        public string Category { get; set; } = "Diagnostic";

        /// <summary>
        /// Order to execute (lower runs first)
        /// </summary>
        public int ExecutionOrder { get; set; } = 100;

        /// <summary>
        /// Whether to export results to CSV
        /// </summary>
        public bool ExportToCsv { get; set; } = true;
    }
}
