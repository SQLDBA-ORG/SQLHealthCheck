# Practical Usage Examples

## Example 1: Quick Check Script

```csharp
using SqlCheckLibrary.Services;

// Super simple - just check and report
var repo = new CheckRepository();
await repo.LoadChecksAsync();

var runner = new CheckRunner("Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=true;");
var results = await runner.RunChecksAsync(repo.GetEnabledChecks());

var failures = results.Where(r => !r.Passed).ToList();
if (failures.Any())
{
    Console.WriteLine($"Found {failures.Count} issues!");
    foreach (var f in failures)
        Console.WriteLine($"- {f.CheckName}");
}
```

## Example 2: Adding a Custom Check to JSON

Here's how to check for specific database settings:

```json
{
  "id": "CUSTOM_001",
  "name": "Page Verify Checksum",
  "description": "Ensures all databases use PAGE_VERIFY CHECKSUM for corruption detection",
  "category": "Reliability",
  "severity": "Warning",
  "sqlQuery": "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases WHERE page_verify_option <> 2 AND database_id > 4) THEN 1 ELSE 0 END",
  "expectedValue": 0,
  "enabled": true,
  "recommendedAction": "Set PAGE_VERIFY = CHECKSUM on all databases: ALTER DATABASE [DbName] SET PAGE_VERIFY CHECKSUM"
}
```

Add this to your `sql-checks.json` array and it'll run automatically!

## Example 3: Check Only Critical Items

```csharp
var criticalChecks = repo.GetAllChecks()
    .Where(c => c.Severity == "Critical")
    .ToList();

var results = await runner.RunChecksAsync(criticalChecks);
```

## Example 4: Email Alert on Failures

```csharp
var results = await runner.RunChecksAsync(repo.GetEnabledChecks());
var critical = results.Where(r => !r.Passed && r.Severity == "Critical").ToList();

if (critical.Any())
{
    var message = string.Join("\n", critical.Select(c => $"{c.CheckName}: {c.Message}"));
    
    // Send email (pseudo-code)
    await SendEmailAsync(
        to: "dba-team@company.com",
        subject: $"SQL Server Health Alert - {critical.Count} Critical Issues",
        body: message
    );
}
```

## Example 5: Daily Scheduled Check

Windows Task Scheduler or cron job:

```bash
# Run daily at 6 AM
cd C:\SqlHealthCheck\SqlCheckDemo
dotnet run "Server=prod-sql;Integrated Security=true;TrustServerCertificate=true;" >> C:\Logs\sql-health.log
```

## Example 6: Multi-Server Checks

```csharp
var servers = new[]
{
    "Server=sql01;Integrated Security=true;TrustServerCertificate=true;",
    "Server=sql02;Integrated Security=true;TrustServerCertificate=true;",
    "Server=sql03;Integrated Security=true;TrustServerCertificate=true;"
};

var repo = new CheckRepository();
await repo.LoadChecksAsync();

foreach (var connStr in servers)
{
    var serverName = new SqlConnectionStringBuilder(connStr).DataSource;
    Console.WriteLine($"\nChecking {serverName}...");
    
    var runner = new CheckRunner(connStr);
    var results = await runner.RunChecksAsync(repo.GetEnabledChecks());
    
    var failed = results.Count(r => !r.Passed);
    Console.WriteLine($"  {failed} issues found");
}
```

## Example 7: Save Results to Database

```csharp
// Create a monitoring database table first:
/*
CREATE TABLE SqlHealthCheckResults (
    Id INT IDENTITY PRIMARY KEY,
    ServerName NVARCHAR(100),
    CheckId NVARCHAR(50),
    CheckName NVARCHAR(200),
    Category NVARCHAR(50),
    Passed BIT,
    ExecutedAt DATETIME2
)
*/

var results = await runner.RunChecksAsync(repo.GetEnabledChecks());

using var conn = new SqlConnection("Server=monitoring-db;Database=Monitoring;...");
await conn.OpenAsync();

foreach (var result in results)
{
    var cmd = new SqlCommand(@"
        INSERT INTO SqlHealthCheckResults 
        (ServerName, CheckId, CheckName, Category, Passed, ExecutedAt)
        VALUES 
        (@server, @id, @name, @category, @passed, @executed)", conn);
    
    cmd.Parameters.AddWithValue("@server", Environment.MachineName);
    cmd.Parameters.AddWithValue("@id", result.CheckId);
    cmd.Parameters.AddWithValue("@name", result.CheckName);
    cmd.Parameters.AddWithValue("@category", result.Category);
    cmd.Parameters.AddWithValue("@passed", result.Passed);
    cmd.Parameters.AddWithValue("@executed", result.ExecutedAt);
    
    await cmd.ExecuteNonQueryAsync();
}
```

## Example 8: More Real-World Checks to Add

### Check for Databases in Simple Recovery
```json
{
  "id": "RECOVERY_001",
  "name": "Production Database in Simple Recovery",
  "description": "Production databases should use FULL recovery model",
  "category": "Backup",
  "severity": "Critical",
  "sqlQuery": "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases WHERE recovery_model = 3 AND name LIKE '%prod%' AND database_id > 4) THEN 1 ELSE 0 END",
  "expectedValue": 0,
  "enabled": true,
  "recommendedAction": "Change to FULL recovery: ALTER DATABASE [DbName] SET RECOVERY FULL"
}
```

### Check for Large Database Files Without Growth Limits
```json
{
  "id": "GROWTH_002",
  "name": "Unlimited File Growth",
  "description": "Database files should have max size limits to prevent disk space issues",
  "category": "Configuration",
  "severity": "Warning",
  "sqlQuery": "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.master_files WHERE max_size = -1 AND database_id > 4 AND type = 0) THEN 1 ELSE 0 END",
  "expectedValue": 0,
  "enabled": true,
  "recommendedAction": "Set MAXSIZE on database files to prevent uncontrolled growth"
}
```

### Check for Old Statistics
```json
{
  "id": "STATS_001",
  "name": "Statistics Not Updated Recently",
  "description": "Statistics older than 7 days can cause poor query plans",
  "category": "Performance",
  "severity": "Warning",
  "sqlQuery": "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.stats s CROSS APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) sp WHERE sp.last_updated < DATEADD(DAY, -7, GETDATE()) AND sp.rows > 10000) THEN 1 ELSE 0 END",
  "expectedValue": 0,
  "enabled": true,
  "recommendedAction": "Update statistics manually or ensure auto-update stats is working"
}
```

### Check for Blocking
```json
{
  "id": "BLOCKING_001",
  "name": "Active Blocking Chains",
  "description": "Detects sessions blocking others for more than 30 seconds",
  "category": "Performance",
  "severity": "Critical",
  "sqlQuery": "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.dm_exec_requests WHERE blocking_session_id <> 0 AND wait_time > 30000) THEN 1 ELSE 0 END",
  "expectedValue": 0,
  "enabled": true,
  "recommendedAction": "Investigate blocking sessions with sp_who2 or sp_WhoIsActive"
}
```

## Tips for Writing Good Checks

1. **Keep it simple**: Return 0 or 1, nothing else
2. **Be specific**: Check one thing per check
3. **Use thresholds wisely**: Don't alert on every tiny issue
4. **Test first**: Run manually before adding to production checks
5. **Document well**: Good descriptions help future you
6. **Consider cost**: Some DMV queries are expensive - use LIMITED scans where possible

## Common SQL Patterns

### Existence Check
```sql
SELECT CASE 
    WHEN EXISTS (SELECT 1 FROM some_table WHERE condition)
    THEN 1  -- Problem found
    ELSE 0  -- All good
END
```

### Threshold Check
```sql
SELECT CASE 
    WHEN (SELECT COUNT(*) FROM some_table WHERE condition) > 10
    THEN 1  -- Too many
    ELSE 0  -- Acceptable
END
```

### Date Check
```sql
SELECT CASE 
    WHEN (SELECT MAX(last_backup) FROM backups) < DATEADD(DAY, -7, GETDATE())
    THEN 1  -- Too old
    ELSE 0  -- Recent enough
END
```

That's it! Keep it simple, test your checks, and happy monitoring! 🎯
