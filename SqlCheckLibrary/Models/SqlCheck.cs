namespace SqlCheckLibrary.Models
{
    /// <summary>
    /// Represents a single SQL Server health check with enterprise-grade metadata
    /// </summary>
    public class SqlCheck
    {
        /// <summary>
        /// Unique identifier for the check
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the check
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Detailed description of what this check does
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Category (e.g., "Backup", "Security", "Performance")
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Severity level: Critical, Warning, Info
        /// </summary>
        public string Severity { get; set; } = "Warning";

        /// <summary>
        /// SQL query to execute. Should return a single row with single column (0 = pass, 1 = fail)
        /// </summary>
        public string SqlQuery { get; set; } = string.Empty;

        /// <summary>
        /// Expected result (typically 0 for pass)
        /// </summary>
        public int ExpectedValue { get; set; } = 0;

        /// <summary>
        /// Whether this check is enabled
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Recommended action if check fails
        /// </summary>
        public string RecommendedAction { get; set; } = string.Empty;

        /// <summary>
        /// Source of the check (e.g., "sp_Blitz", "sp_triage", "Custom")
        /// </summary>
        public string Source { get; set; } = "Custom";

        /// <summary>
        /// Execution type: Binary (0/1), RowCount, or InfoOnly
        /// </summary>
        public string ExecutionType { get; set; } = "Binary";

        /// <summary>
        /// Row count condition: Equals0, GreaterThan0, LessThan0, Any
        /// </summary>
        public string RowCountCondition { get; set; } = "Equals0";

        /// <summary>
        /// Result interpretation: PassFail, WarningOnly, InfoOnly
        /// </summary>
        public string ResultInterpretation { get; set; } = "PassFail";

        /// <summary>
        /// Priority level (1-5, where 1 is highest priority)
        /// </summary>
        public int Priority { get; set; } = 1;

        /// <summary>
        /// Severity score (1-5, where 5 is most severe)
        /// </summary>
        public int SeverityScore { get; set; } = 1;

        /// <summary>
        /// Weight for scoring calculations (decimal)
        /// </summary>
        public decimal Weight { get; set; } = 0.00m;

        /// <summary>
        /// Expected result when check passes (e.g., "All databases have recent Backups")
        /// </summary>
        public string ExpectedState { get; set; } = string.Empty;

        /// <summary>
        /// What was found when check fails (uses @ placeholder, e.g., "@ databases have no backups")
        /// The @ will be replaced with the actual count/value from query results
        /// </summary>
        public string CheckTriggered { get; set; } = string.Empty;

        /// <summary>
        /// What the passing state looks like (uses @ placeholder, e.g., "All @ databases cleared")
        /// The @ will be replaced with the actual count/value from query results
        /// </summary>
        public string CheckCleared { get; set; } = string.Empty;

        /// <summary>
        /// Detailed recommendation/remediation steps
        /// </summary>
        public string DetailedRemediation { get; set; } = string.Empty;

        /// <summary>
        /// Support type: Reactive, Proactive, or "Reactive, Proactive"
        /// </summary>
        public string SupportType { get; set; } = "Reactive, Proactive";

        /// <summary>
        /// Impact score (1-5)
        /// </summary>
        public int ImpactScore { get; set; } = 3;

        /// <summary>
        /// Additional notes or context
        /// </summary>
        public string AdditionalNotes { get; set; } = string.Empty;
    }
}
