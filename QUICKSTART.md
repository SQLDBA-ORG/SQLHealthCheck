# 🚀 QUICK START - 2 Minutes to Running

## Option 1: Visual Studio (Easiest)

1. **Open** `SqlHealthCheck.sln` in Visual Studio
2. **Update** connection string in `SqlCheckDemo/Program.cs` (line 13)
3. **Press F5** to run

## Option 2: Command Line

```bash
# Navigate to the solution folder
cd SqlHealthCheck

# Build everything
dotnet build

# Run with default connection (localhost)
cd SqlCheckDemo
dotnet run

# Or run with your own connection string
dotnet run "Server=YOUR_SERVER;Database=master;Integrated Security=true;TrustServerCertificate=true;"
```

## What You'll See

```
=== SQL Server Health Check Demo ===

Loaded 12 checks from sql-checks.json

Testing connection...
✅ Connected successfully

Running checks...

[Backup]
------------------------------------------------------------
✅ Full Backup Recency
✅ Transaction Log Backup Recency

[Configuration]
------------------------------------------------------------
✅ Auto Close Enabled
✅ Auto Shrink Enabled
❌ TempDB File Count [Info]
   Check failed. Expected: 0, Got: 1. Add more TempDB data files...
✅ Percentage Growth Settings

[Integrity]
------------------------------------------------------------
✅ Database Corruption Detected

[Performance]
------------------------------------------------------------
❌ High Index Fragmentation [Warning]
   Check failed. Expected: 0, Got: 1. Consider rebuilding...
✅ Missing Index Recommendations
✅ Excessive VLF Count

[Security]
------------------------------------------------------------
✅ SA Account Enabled
✅ Weak Password Policies

=== Summary ===
Total Checks: 12
Passed: 10
Failed: 2
```

## Now Customize It!

The first time you run, it creates `sql-checks.json` in the SqlCheckDemo folder.

**Edit that file** to:
- ✏️ Change thresholds (e.g., backup age from 7 to 3 days)
- ➕ Add new checks (see USAGE.md for examples)
- 🔇 Disable checks you don't care about
- 📝 Update SQL queries for your environment

**No recompile needed** - just edit the JSON and run again!

## File Structure

```
SqlHealthCheck/
├── SqlCheckLibrary/          # The reusable library
│   ├── Models/              # SqlCheck and CheckResult classes
│   └── Services/            # CheckRunner and CheckRepository
├── SqlCheckDemo/            # Console app demo
│   └── Program.cs           # Change connection string here
└── sql-checks.json          # Auto-generated on first run - EDIT THIS!
```

## Integration Examples

### In Your Own Code

```csharp
using SqlCheckLibrary.Services;

var repo = new CheckRepository();
await repo.LoadChecksAsync();

var runner = new CheckRunner("your-connection-string");
var results = await runner.RunChecksAsync(repo.GetEnabledChecks());

// Do whatever you want with results!
foreach (var fail in results.Where(r => !r.Passed))
{
    Console.WriteLine($"Issue: {fail.CheckName}");
    SendAlert(fail); // Your alert logic
}
```

### Multi-Server Monitoring

```csharp
var servers = new[] { "sql01", "sql02", "sql03" };

foreach (var server in servers)
{
    var connStr = $"Server={server};Database=master;Integrated Security=true;TrustServerCertificate=true;";
    var runner = new CheckRunner(connStr);
    var results = await runner.RunChecksAsync(repo.GetEnabledChecks());
    
    SaveToDatabase(server, results); // Store in monitoring DB
}
```

## Enjoy! 🎉

That's it. **Super simple.** All checks are in JSON, easy to modify, zero magic.

Go plant those trees! 🌳
