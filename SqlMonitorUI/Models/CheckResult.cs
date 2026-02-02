using System;

namespace SqlCheckLibrary.Models
{
    /// <summary>
    /// Result of executing a SQL check
    /// </summary>
    public class CheckResult
    {
        public string CheckId { get; set; } = string.Empty;
        public string CheckName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public int ActualValue { get; set; }
        public int ExpectedValue { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
        public string? ErrorMessage { get; set; }
    }
}
