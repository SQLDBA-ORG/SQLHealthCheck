# 📝 How to Modify SQL Checks

## Where Are the Checks?

The first time you run SqlMonitorUI, it creates a file called **`sql-checks.json`** in the same folder as the executable.

### Finding the File

**If running from Visual Studio (F5):**
```
SqlMonitorUI\bin\Debug\net8.0-windows\sql-checks.json
```

**If running from command line (`dotnet run`):**
```
SqlMonitorUI\sql-checks.json
```

**After publishing:**
```
(wherever you published)\sql-checks.json
```

## How to Modify Checks

### Step 1: Run the App Once
This creates the `sql-checks.json` file with 12 default checks.

### Step 2: Close the App

### Step 3: Edit the JSON File
Open `sql-checks.json` in any text editor (Notepad, VS Code, Visual Studio, etc.)

### Step 4: Make Your Changes
See examples below!

### Step 5: Run the App Again
Your changes will be loaded automatically.

## 📋 Example: Change a Threshold

**Change backup age from 7 days to 3 days:**

Find this check in `sql-checks.json`:
```json
{
  "id": "BACKUP_001",
  "name": "Full Backup Recency",
  "description": "Checks if any database hasn't had a full backup in the last 7 days",
  "category": "Backup",
  "severity": "Critical",
  "sqlQuery": "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases d LEFT JOIN msdb.dbo.backupset b ON d.name = b.database_name AND b.type = 'D' WHERE d.database_id > 4 AND d.state = 0 AND (b.backup_finish_date IS NULL OR b.backup_finish_date < DATEADD(DAY, -7, GETDATE()))) THEN 1 ELSE 0 END",
  "expectedValue": 0,
  "enabled": true,
  "recommendedAction": "Schedule full backups for databases that haven't been backed up in 7+ days"
}
```

Change `-7` to `-3`:
```json
"sqlQuery": "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases d LEFT JOIN msdb.dbo.backupset b ON d.name = b.database_name AND b.type = 'D' WHERE d.database_id > 4 AND d.state = 0 AND (b.backup_finish_date IS NULL OR b.backup_finish_date < DATEADD(DAY, -3, GETDATE()))) THEN 1 ELSE 0 END",
```

Also update the description:
```json
"description": "Checks if any database hasn't had a full backup in the last 3 days",
```

## ➕ Example: Add a New Check

Add this to the array in `sql-checks.json`:

```json
{
  "id": "CUSTOM_001",
  "name": "Page Verify CHECKSUM",
  "description": "Ensures all databases use PAGE_VERIFY CHECKSUM for corruption detection",
  "category": "Reliability",
  "severity": "Warning",
  "sqlQuery": "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases WHERE page_verify_option <> 2 AND database_id > 4) THEN 1 ELSE 0 END",
  "expectedValue": 0,
  "enabled": true,
  "recommendedAction": "Set PAGE_VERIFY = CHECKSUM: ALTER DATABASE [DbName] SET PAGE_VERIFY CHECKSUM"
}
```

**Important:** Don't forget the comma between checks!

## ❌ Example: Disable a Check

Set `enabled` to `false`:

```json
{
  "id": "SECURITY_001",
  "name": "SA Account Enabled",
  "enabled": false,    <-- Change this
  ...
}
```

The check will still be in the file but won't run.

## 🎨 Example: Change Severity

Make a check more or less important:

```json
{
  "id": "CONFIG_001",
  "name": "Auto Close Enabled",
  "severity": "Critical",    <-- Change from "Warning" to "Critical"
  ...
}
```

Valid severities: `Critical`, `Warning`, `Info`

## 🔍 Writing Your Own Check SQL

### Rule: Return 0 (pass) or 1 (fail)

**Template:**
```sql
SELECT CASE 
    WHEN [your condition that indicates a problem]
    THEN 1  -- Failed
    ELSE 0  -- Passed
END
```

### Example Checks You Can Add

**1. Check for Databases Without Regular Backups:**
```json
{
  "id": "BACKUP_003",
  "name": "Database Never Backed Up",
  "description": "Finds databases that have NEVER had a backup",
  "category": "Backup",
  "severity": "Critical",
  "sqlQuery": "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases d WHERE d.database_id > 4 AND d.state = 0 AND NOT EXISTS (SELECT 1 FROM msdb.dbo.backupset b WHERE b.database_name = d.name)) THEN 1 ELSE 0 END",
  "expectedValue": 0,
  "enabled": true,
  "recommendedAction": "Perform a full backup immediately"
}
```

**2. Check for Databases in Simple Recovery Mode:**
```json
{
  "id": "RECOVERY_001",
  "name": "Production DB in Simple Recovery",
  "description": "Production databases should use FULL recovery model",
  "category": "Backup",
  "severity": "Critical",
  "sqlQuery": "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases WHERE recovery_model = 3 AND name LIKE '%prod%' AND database_id > 4) THEN 1 ELSE 0 END",
  "expectedValue": 0,
  "enabled": true,
  "recommendedAction": "ALTER DATABASE [DbName] SET RECOVERY FULL"
}
```

**3. Check for Old Statistics:**
```json
{
  "id": "STATS_001",
  "name": "Statistics Not Updated",
  "description": "Finds statistics not updated in 7+ days",
  "category": "Performance",
  "severity": "Warning",
  "sqlQuery": "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.stats s CROSS APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) sp WHERE sp.last_updated < DATEADD(DAY, -7, GETDATE()) AND sp.rows > 10000) THEN 1 ELSE 0 END",
  "expectedValue": 0,
  "enabled": true,
  "recommendedAction": "Update statistics: UPDATE STATISTICS [TableName]"
}
```

**4. Check for Blocking:**
```json
{
  "id": "BLOCKING_001",
  "name": "Active Blocking",
  "description": "Detects sessions blocking others for 30+ seconds",
  "category": "Performance",
  "severity": "Critical",
  "sqlQuery": "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.dm_exec_requests WHERE blocking_session_id <> 0 AND wait_time > 30000) THEN 1 ELSE 0 END",
  "expectedValue": 0,
  "enabled": true,
  "recommendedAction": "Investigate with sp_WhoIsActive or Activity Monitor"
}
```

**5. Check for Unlimited File Growth:**
```json
{
  "id": "GROWTH_002",
  "name": "Unlimited File Growth",
  "description": "Database files should have max size limits",
  "category": "Configuration",
  "severity": "Warning",
  "sqlQuery": "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.master_files WHERE max_size = -1 AND database_id > 4 AND type = 0) THEN 1 ELSE 0 END",
  "expectedValue": 0,
  "enabled": true,
  "recommendedAction": "Set MAXSIZE on data files to prevent disk space issues"
}
```

## 📂 JSON File Structure

```json
[
  {
    "id": "UNIQUE_ID",
    "name": "Check Display Name",
    "description": "What this check does",
    "category": "Backup|Security|Performance|Configuration|Integrity",
    "severity": "Critical|Warning|Info",
    "sqlQuery": "SELECT CASE WHEN ... THEN 1 ELSE 0 END",
    "expectedValue": 0,
    "enabled": true,
    "recommendedAction": "What to do if it fails"
  },
  {
    "id": "NEXT_CHECK",
    ...
  }
]
```

## 🔄 How Changes Are Applied

1. **App starts** → Reads `sql-checks.json`
2. **You click "Run Checks"** → Runs all `enabled: true` checks
3. **Results display** → Shows check name, category, severity

Changes to the JSON file take effect **immediately** the next time you click "Run Checks". No need to recompile!

## 💡 Pro Tips

### Test Your SQL First
Before adding to the JSON, test in SQL Server Management Studio:
```sql
-- Your check SQL here
SELECT CASE 
    WHEN EXISTS (...)
    THEN 1  -- Problem found
    ELSE 0  -- All good
END
```

Should return either `0` or `1`.

### Use Categories to Organize
Create custom categories:
```json
"category": "Compliance",
"category": "Custom",
"category": "Daily Checks",
```

They'll show up in the sidebar automatically!

### Keep IDs Unique
Use a naming pattern:
- `BACKUP_001`, `BACKUP_002`, etc.
- `CUSTOM_001`, `CUSTOM_002`, etc.
- `PERF_001`, `PERF_002`, etc.

### Back Up Your Checks
Copy `sql-checks.json` somewhere safe before making major changes!

## 🚨 Common Mistakes

### ❌ Forgetting Commas
```json
{
  "id": "CHECK_001",
  ...
}   <-- Missing comma here!
{
  "id": "CHECK_002",
```

### ❌ SQL Returning Multiple Rows/Columns
```sql
-- Bad - returns multiple columns
SELECT name, state FROM sys.databases

-- Good - returns single value
SELECT CASE WHEN EXISTS (...) THEN 1 ELSE 0 END
```

### ❌ Invalid JSON
Use a JSON validator if unsure: https://jsonlint.com/

### ❌ SQL Syntax Errors
Test in SSMS first!

## 🎯 Quick Workflow

1. **Decide** what you want to check
2. **Write SQL** in SSMS that returns 0/1
3. **Copy SQL** into a new check in `sql-checks.json`
4. **Save** the file
5. **Run the app** and click "Run Checks"
6. **Verify** it works!

## 📍 Where to Find sql-checks.json

### When Debugging in Visual Studio
```
YourProject\SqlMonitorUI\bin\Debug\net8.0-windows\sql-checks.json
```

### Pro Tip: Create a Default Location
Put this in your user folder and point to it:
```
C:\Users\YourName\.sqlmonitor\sql-checks.json
```

Then modify `CheckRepository.cs` to look there first.

## 🔧 Advanced: Multiple Check Files

Want different checks for different servers? You can:

1. Create `sql-checks-prod.json`, `sql-checks-dev.json`, etc.
2. Modify the code to accept a parameter for which file to load
3. Or just swap files before running

Happy monitoring! 🎉
