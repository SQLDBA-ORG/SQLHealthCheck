# SQL Server Health Check Library

A dead-simple, JSON-based SQL Server health check library. All checks return 0 (pass) or 1 (fail).

## 🎯 Two Ways to Use

### 1️⃣ **WPF Desktop UI** (NEW! 🎨)
Professional monitoring dashboard with SolarWinds-style interface
- Visual health status indicators
- Real-time statistics cards
- Category filtering and search
- Modern, clean design

**Quick Start:**
```bash
cd SqlMonitorUI
dotnet run
```

See [SqlMonitorUI/README.md](SqlMonitorUI/README.md) for full UI documentation.

### 2️⃣ **Console Application / Library**
Simple command-line tool or integrate into your own apps
- Lightweight and fast
- Perfect for automation and scripts
- Easy to integrate

**Quick Start:**
```bash
cd SqlCheckDemo
dotnet run
```

## Quick Start - Choose Your Path

### 1. Build the solution
```bash
# This builds both the library, console app, AND the WPF UI
dotnet build
```

### 2. Run the WPF UI (Recommended for Visual Monitoring)
```bash
cd SqlMonitorUI
dotnet run
```

Or run the console demo:
```bash
cd SqlCheckDemo
dotnet run
```

Or with custom connection string:
```bash
dotnet run "Server=myserver;Database=master;Integrated Security=true;TrustServerCertificate=true;"
```

## How It Works

### The Check Model
Each check has:
- **Name**: Display name
- **SQL Query**: Query that returns 0 (pass) or 1 (fail)
- **Expected Value**: Usually 0 (meaning pass)
- **Category**: Backup, Security, Performance, etc.
- **Severity**: Critical, Warning, Info
- **Enabled**: true/false

### Adding Your Own Checks

Just edit `sql-checks.json`:

```json
{
  "id": "BACKUP_001",
  "name": "Full Backup Recency",
  "description": "Checks if databases haven't been backed up in 7 days",
  "category": "Backup",
  "severity": "Critical",
  "sqlQuery": "SELECT CASE WHEN EXISTS (...) THEN 1 ELSE 0 END",
  "expectedValue": 0,
  "enabled": true,
  "recommendedAction": "Schedule backups!"
}
```

### Writing SQL Checks

Your SQL must return a **single integer** (0 or 1):

```sql
-- ✅ Good - Returns 0 or 1
SELECT CASE 
    WHEN EXISTS (SELECT 1 FROM sys.databases WHERE is_auto_close_on = 1)
    THEN 1  -- Fail
    ELSE 0  -- Pass
END

-- ❌ Bad - Returns multiple rows or columns
SELECT * FROM sys.databases

-- ❌ Bad - Returns text
SELECT 'Failed'
```

## Pre-Built Checks

The library includes 12 checks from sp_blitz and SQL Tiger Team best practices:

### Backup (2 checks)
- Full backup recency (7 days)
- Transaction log backup recency (2 hours)

### Integrity (1 check)
- Database corruption detection

### Performance (3 checks)
- High index fragmentation (>30%)
- Missing index recommendations
- Excessive VLF count (>50)

### Security (2 checks)
- SA account enabled
- Weak password policies

### Configuration (4 checks)
- AUTO_CLOSE enabled
- AUTO_SHRINK enabled
- TempDB file count vs CPU cores
- Percentage-based growth settings

## Using in Your Own Projects

### Option 1: Reference the Library
```csharp
using SqlCheckLibrary.Services;

var repository = new CheckRepository();
await repository.LoadChecksAsync();

var runner = new CheckRunner("your-connection-string");
var results = await runner.RunChecksAsync(repository.GetEnabledChecks());

foreach (var result in results.Where(r => !r.Passed))
{
    Console.WriteLine($"❌ {result.CheckName}: {result.Message}");
}
```

### Option 2: Copy the Classes
Just copy the Models and Services folders into your project. Zero dependencies except Microsoft.Data.SqlClient.

## Customization Ideas

### Add New Check Categories
Edit the JSON and add checks with category "Availability", "Compliance", etc.

### Change Thresholds
Update the SQL queries. Example - change backup age from 7 to 3 days:
```sql
-- Find this in sql-checks.json
DATEADD(DAY, -7, GETDATE())

-- Change to
DATEADD(DAY, -3, GETDATE())
```

### Disable Checks
Set `"enabled": false` in the JSON

### Add Instance-Level Checks
Create checks that query server-wide DMVs instead of database-specific ones

### Export Results
The `CheckResult` model serializes to JSON easily:
```csharp
var json = JsonSerializer.Serialize(results);
await File.WriteAllTextAsync("results.json", json);
```

## Tips from sp_blitz / SQL Tiger Team

- **Always test on non-production first** - Some checks can be resource-intensive
- **Run during maintenance windows** - Index fragmentation checks especially
- **Review false positives** - Some "failures" might be intentional in your environment
- **Trend over time** - Store results and track improvements
- **Prioritize Critical severity** - Fix these first

## Advanced Usage

### Run Specific Categories Only
```csharp
var backupChecks = repository.GetChecksByCategory("Backup");
var results = await runner.RunChecksAsync(backupChecks);
```

### Custom Check Repository
```csharp
var checks = new List<SqlCheck>
{
    new SqlCheck
    {
        Id = "CUSTOM_001",
        Name = "My Custom Check",
        SqlQuery = "SELECT 0", // Your SQL here
        // ... other properties
    }
};

var runner = new CheckRunner(connectionString);
var results = await runner.RunChecksAsync(checks);
```

### Save Results to Database
```csharp
// After running checks
foreach (var result in results)
{
    // Insert into your monitoring database
    await SaveToMonitoringDb(result);
}
```

## License

Use this however you want. Based on best practices from:
- Brent Ozar's sp_blitz (MIT License)
- Microsoft SQL Tiger Team (MIT License)

## Next Steps

Plant those trees! 🌳🌲🌴

Then:
1. Point at your SQL Server
2. Run the checks
3. Edit `sql-checks.json` to add your own checks
4. Build something awesome with it
