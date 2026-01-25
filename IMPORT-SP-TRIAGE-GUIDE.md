# 📥 Importing sp_triage Checks - Complete Guide

## What Is sp_triage?

**sp_triage** is a comprehensive SQL Server diagnostic tool created by Adrian Sullivan (sqldba.org). Unlike sp_Blitz which focuses on specific checks, sp_triage creates detailed output tables covering 35+ different diagnostic categories.

## sp_Blitz vs sp_triage

| Feature | sp_Blitz | sp_triage |
|---------|----------|-----------|
| **Creator** | Brent Ozar Unlimited | Adrian Sullivan (SQLDBA.ORG) |
| **Approach** | Specific checks (210+) | Output categories (35+) |
| **Focus** | Pass/Fail checks | Detailed diagnostics |
| **Best For** | Quick health checks | Deep analysis |
| **Our Support** | ~34 working queries | Placeholder categories |

## How to Import sp_triage

### Step 1: Get sp_triage.sql

Download from:
https://github.com/SQLDBA-ORG/sqldba/

Or use the file you uploaded!

### Step 2: Click "📥 Import Checks"

The button now supports BOTH scripts!

### Step 3: Select the File

Navigate to your sp_triage.sql file and open it.

### Step 4: Auto-Detection

The app will automatically detect it's sp_triage based on the filename.

If the filename doesn't contain "sp_triage" or "sqldba", you'll be asked:
```
Click YES if this is sp_Blitz.sql
Click NO if this is sp_triage.sql
```

### Step 5: Review Summary

```
Found 35 checks in sp_triage

Source: sp_triage
New checks to add: 35
Existing checks to update: 0

Working queries: 0
Placeholders: 35

Total checks after import: 245

Continue with import?
```

### Step 6: Confirm

Click **Yes** to import.

## What Gets Imported from sp_triage

sp_triage creates 35+ output tables for different diagnostic areas:

### Storage & Space (8 categories)
- ✅ Heap Tables
- ✅ Database Sizes
- ✅ Log Space Usage
- ✅ Compression States
- ✅ Index Usage Stats
- ✅ File Sizes and Growth
- ✅ VLF Counts
- ✅ Space Analysis

### Performance (10 categories)
- ✅ Query Statistics
- ✅ Missing Indexes
- ✅ Index Fragmentation
- ✅ Wait Statistics (Important Waits)
- ✅ Wait Statistics (Ignorable Waits)
- ✅ CPU Usage
- ✅ Memory Usage
- ✅ Query Plans
- ✅ Execution Stats
- ✅ Performance Counters

### Configuration (6 categories)
- ✅ Server Configuration
- ✅ Database Settings
- ✅ Trace Flags
- ✅ Configuration Defaults
- ✅ Database Options
- ✅ Service Settings

### Security & Operations (5 categories)
- ✅ Login Information
- ✅ Permissions
- ✅ Security Settings
- ✅ Job History
- ✅ Error Log Analysis

### Information (6+ categories)
- ✅ Server Info
- ✅ Database Properties
- ✅ Build Information
- ✅ Version Details
- ✅ Instance Configuration
- ✅ And more...

## Important: sp_triage Checks Are Placeholders

**Reality Check:** sp_triage doesn't work like sp_Blitz. It creates detailed output tables with hundreds of rows of diagnostic data, not simple pass/fail checks.

**Our Approach:**
1. Import each output category as a "check"
2. All return placeholder queries (0 = not implemented)
3. You can implement specific checks based on the categories
4. Or run sp_triage separately and use our app for sp_Blitz checks

### Example: Implementing a sp_triage Check

Let's say you want to check for heap tables:

**Original sp_triage output:**
```sql
-- Creates #output_sqldba_org_sp_triage_HeapTable
-- WITH columns for database, schema, table, row count, etc.
```

**Your Implementation:**
```json
{
  "id": "TRIAGE_001",
  "name": "Review Heap Table",
  "source": "sp_triage",
  "sqlQuery": "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE type = 0 AND object_id IN (SELECT object_id FROM sys.objects WHERE type = 'U')) THEN 1 ELSE 0 END",
  "expectedValue": 0,
  "enabled": true
}
```

## Source Column in Results

After importing both scripts, your results will show:

| Check Name | Category | Severity | Status | **Source** |
|------------|----------|----------|--------|------------|
| Full Backup Recency | Backup | Critical | Failed | **sp_Blitz** |
| Review Heap Table | Storage | Info | Passed | **sp_triage** |
| My Custom Check | Performance | Warning | Passed | **Custom** |

This helps you know which script each check came from!

## Mixing sp_Blitz and sp_triage

**Yes, you can import BOTH!**

1. Import sp_Blitz (210+ checks, ~34 working)
2. Import sp_triage (35+ categories, all placeholders)
3. Total: 245+ checks
4. Source column shows which is which
5. Enable/disable individually

### Recommended Workflow

1. **Start with sp_Blitz** - Import and run the ~34 working checks
2. **Review results** - Fix critical issues
3. **Import sp_triage** - Get the categories for reference
4. **Implement selectively** - Add queries for sp_triage checks you need
5. **Run both** - Mix and match based on your needs

## Example: Full Import of Both

```json
[
  {
    "id": "BLITZ_001",
    "name": "Backup Hasn't Happened Recently",
    "source": "sp_Blitz",
    "sqlQuery": "SELECT CASE WHEN ...",
    "enabled": true
  },
  {
    "id": "BLITZ_002",
    "name": "Transaction Log Backup",
    "source": "sp_Blitz",
    "sqlQuery": "SELECT CASE WHEN ...",
    "enabled": true
  },
  ...210 more sp_Blitz checks...
  {
    "id": "TRIAGE_001",
    "name": "Review Heap Table",
    "source": "sp_triage",
    "sqlQuery": "SELECT 0 AS Result -- (not yet implemented)",
    "enabled": false
  },
  {
    "id": "TRIAGE_002",
    "name": "Review Database Size",
    "source": "sp_triage",
    "sqlQuery": "SELECT 0 AS Result -- (not yet implemented)",
    "enabled": false
  },
  ...35 more sp_triage categories...
]
```

## Implementing sp_triage Checks

### Strategy 1: Convert Categories to Simple Checks

Pick important sp_triage categories and create simple pass/fail checks:

**Heap Tables:**
```sql
-- sp_triage shows ALL heap tables
-- Our check: Do heap tables exist?
SELECT CASE 
  WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE type = 0) 
  THEN 1 ELSE 0 
END
```

**VLF Count:**
```sql
-- sp_triage shows VLF counts per database
-- Our check: Any database with >50 VLFs?
DECLARE @VLFCount INT;
-- (VLF counting logic)
SELECT CASE WHEN @VLFCount > 50 THEN 1 ELSE 0 END
```

### Strategy 2: Run sp_triage Separately

1. Keep sp_triage as a separate diagnostic tool
2. Run it when you need deep analysis
3. Use our app for sp_Blitz checks only
4. Reference sp_triage output tables when needed

### Strategy 3: Hybrid Approach

1. Import sp_triage for reference (all disabled)
2. Enable and implement only the categories you care about
3. Leave others as placeholders for future use

## Tips for Success

### 🎯 Use sp_Blitz for Daily Checks
- sp_Blitz has working queries
- Great for automated monitoring
- Clear pass/fail results

### 📊 Use sp_triage for Deep Dives
- Run when you need details
- Great for troubleshooting
- Comprehensive diagnostic data

### 🔄 Mix Both in Our App
- Import both scripts
- Filter by Source in results
- Disable sp_triage placeholders
- Implement sp_triage checks as needed

## Frequently Asked Questions

### Can I import both scripts?
**Yes!** The app handles both. Source column shows which is which.

### Which should I import first?
**sp_Blitz** - It has working queries. sp_triage is mostly placeholders.

### Will sp_triage checks actually run?
**Not without implementation.** They're placeholders. You need to add queries.

### Can I delete sp_triage checks after import?
**Yes!** Edit sql-checks.json and remove checks you don't want.

### How do I know which source a check came from?
**Look at the Source column** in the results grid!

## Summary

✅ **sp_triage Support** - Import 35+ diagnostic categories  
✅ **Source Tracking** - Know which script each check came from  
✅ **Mix Both Scripts** - Use sp_Blitz + sp_triage together  
✅ **Smart Detection** - Auto-detects which script you're importing  
✅ **Flexible Implementation** - Implement sp_triage checks as needed  

**Import sp_triage to get the categories, then implement the checks you need!** 🎉
