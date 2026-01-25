# ⚙️ Check Manager - Complete Guide

## What Is The Check Manager?

The **Check Manager** is a visual editor that lets you:
- ✅ View ALL health checks in one place
- ✅ Edit checks without touching JSON
- ✅ Filter by source (sp_Blitz, sp_triage, Custom)
- ✅ Add/Delete checks visually
- ✅ Edit SQL queries in dedicated editor
- ✅ Export/Import configurations
- ✅ Enable/disable checks with one click

**No more editing JSON files manually!**

## How to Open

1. Run SqlMonitorUI
2. Click **"⚙️ Manage Checks"** button in header
3. Check Manager window opens

## Main Interface

```
┌─────────────────────────────────────────────────────┐
│  Manage SQL Health Checks                           │
├─────────────────────────────────────────────────────┤
│  📥 Import  📤 Export  ➕ Add  🗑️ Delete            │
├─────────────────────────────────────────────────────┤
│  Filter: [All] [sp_Blitz] [sp_triage] [Custom]     │
├─────────────────────────────────────────────────────┤
│  ┌───┬─────┬──────┬────────┬─────────┬──────┬───┐  │
│  │☑│ ID  │ Name │ Source │Category│Sev...│SQL│  │
│  ├───┼─────┼──────┼────────┼─────────┼──────┼───┤  │
│  │☑│BLITZ│Backu…│sp_Blitz│Backup  │Crit..│...│  │
│  │☑│BLITZ│Auto …│sp_Blitz│Perform…│Warn..│...│  │
│  │☐│TRIAG│Heap …│sp_tria…│Storage │Info …│...│  │
│  │☑│CUSTO│My Ch…│Custom  │Custom  │Warn..│...│  │
│  └───┴─────┴──────┴────────┴─────────┴──────┴───┘  │
├─────────────────────────────────────────────────────┤
│                         💾 Save Changes    [Close]  │
└─────────────────────────────────────────────────────┘
```

## Features

### 1. Filter by Source

**Radio Buttons:**
- **All Sources** - Shows all 245+ checks
- **sp_Blitz** - Shows only Brent Ozar's 210+ checks
- **sp_triage** - Shows only Adrian Sullivan's 35+ checks
- **Custom** - Shows only your custom checks

**Live Counter:**
Shows how many checks are displayed (e.g., "210 checks")

### 2. Import Checks

**Click "📥 Import Checks"**

Opens dialog with two sections:

```
┌────────────────────────────────────────┐
│ ☑ Import sp_Blitz Checks (210+)       │
│   File: [sp_Blitz.sql     ] [Browse]   │
├────────────────────────────────────────┤
│ ☑ Import sp_triage Checks (35+)       │
│   File: [sp_triage.sql    ] [Browse]   │
└────────────────────────────────────────┘
     [Import Selected]  [Cancel]
```

**Steps:**
1. Check which scripts to import
2. Browse to select each file
3. Click "Import Selected"
4. Checks are merged with existing ones
5. Click "Save Changes" to persist

**Smart Merging:**
- New checks: Added
- Existing checks: Updated (preserves your enabled/disabled state)
- No duplicates
- Source tag preserved

### 3. Edit Checks

**Editable Columns:**
- ✅ **Enabled** - Click checkbox to enable/disable
- ✅ **Name** - Double-click to edit
- ✅ **Category** - Double-click to edit
- ✅ **Severity** - Dropdown: Critical, Warning, Info
- ✅ **Description** - Double-click to edit

**Read-Only Columns:**
- **ID** - Can't change (identifies the check)
- **Source** - Can't change (sp_Blitz, sp_triage, or Custom)

**SQL Query:**
- Click **"Edit SQL"** button to open query editor

### 4. Edit SQL Queries

**Click "Edit SQL" button for any check:**

Opens SQL Query Editor:

```
┌──────────────────────────────────────┐
│ Check ID: BLITZ_001                  │
│ Name: Full Backup Recency            │
├──────────────────────────────────────┤
│ ✓ Query must return 0 or 1           │
│ ✓ Use CASE WHEN ... THEN 1 ELSE 0    │
│ ✓ Wrap multi-row in EXISTS           │
├──────────────────────────────────────┤
│  SELECT CASE WHEN EXISTS (           │
│      SELECT 1 FROM sys.databases     │
│      WHERE ...                       │
│  ) THEN 1 ELSE 0 END                 │
│                                      │
├──────────────────────────────────────┤
│ Recommended Action:                  │
│ [Schedule backups for databases...]  │
├──────────────────────────────────────┤
│   🧪 Test  💾 Save  [Cancel]         │
└──────────────────────────────────────┘
```

**Query Rules:**
1. Must return single row, single column
2. Value must be 0 (pass) or 1 (fail)
3. Use `SELECT CASE WHEN ... THEN 1 ELSE 0 END AS Result`

**For Multi-Row Queries:**
```sql
-- BAD - Returns multiple rows
SELECT name FROM sys.databases WHERE...

-- GOOD - Returns 0 or 1
SELECT CASE 
    WHEN EXISTS (
        SELECT name FROM sys.databases WHERE...
    ) 
    THEN 1 ELSE 0 
END AS Result
```

### 5. Add Custom Check

**Click "➕ Add Check":**

Creates new check:
- ID: `CUSTOM_20260123142530` (timestamp)
- Name: "New Check"
- Source: "Custom"
- Enabled: false (disabled by default)
- SQL: Placeholder query

**Then:**
1. Edit the name
2. Change category/severity
3. Click "Edit SQL" to add your query
4. Enable the check
5. Click "Save Changes"

### 6. Delete Checks

**Select one or more checks:**
- Click a row to select
- Ctrl+Click for multiple
- Shift+Click for range

**Click "🗑️ Delete Selected":**
- Shows confirmation
- Deletes selected checks
- Click "Save Changes" to persist

**Warning:** Deletion is permanent after saving!

### 7. Export to JSON

**Click "📤 Export to JSON":**

**Exports:**
- Current filter (if filtered by source)
- All checks (if "All Sources" selected)

**Use cases:**
- Backup before making changes
- Share configurations
- Version control
- Deploy to other servers

**File name:**
`sql-checks-export-20260123-142530.json`

### 8. Save Changes

**Click "💾 Save Changes":**

Writes to `sql-checks.json`:
- All modifications
- New checks
- Deletions
- Enabled/disabled states

**Then reloads repository** so main window sees changes.

## Workflows

### Workflow 1: Import Both Scripts

1. Click "📥 Import Checks"
2. Check both boxes
3. Browse to sp_Blitz.sql
4. Browse to sp_triage.sql
5. Click "Import Selected"
6. Wait for import (245+ checks)
7. Click "Save Changes"
8. Close Check Manager
9. Run checks in main window

### Workflow 2: Review sp_Blitz Checks

1. Click "⚙️ Manage Checks"
2. Click "sp_Blitz" radio button
3. See only 210 sp_Blitz checks
4. Disable noisy ones (uncheck Enabled)
5. Edit SQL for specific checks
6. Click "Save Changes"

### Workflow 3: Create Custom Check

1. Click "➕ Add Check"
2. Edit Name: "Check TempDB Size"
3. Edit Category: "Storage"
4. Change Severity: "Warning"
5. Click "Edit SQL"
6. Enter query:
```sql
SELECT CASE 
    WHEN EXISTS (
        SELECT 1 
        FROM sys.master_files 
        WHERE database_id = 2 
        AND size * 8 / 1024 > 10000
    ) 
    THEN 1 ELSE 0 
END AS Result
```
7. Save query
8. Enable check
9. Save changes
10. Run in main window

### Workflow 4: Export for Backup

1. Click "All Sources" radio
2. Click "📤 Export to JSON"
3. Save to `backups/sql-checks-backup-2026-01-23.json`
4. Store safely

Later if needed:
- Delete `sql-checks.json`
- Rename backup to `sql-checks.json`
- Reload app

### Workflow 5: Clean Up Placeholders

1. Filter to "sp_triage" (35 checks)
2. Select all (Ctrl+A)
3. Check which have placeholder SQL
4. Keep ones you implemented
5. Delete the rest
6. Save changes

### Workflow 6: Deploy to Another Server

Server A:
1. Configure all checks
2. Export to JSON
3. Copy file to Server B

Server B:
1. Copy JSON to app folder
2. Rename to `sql-checks.json`
3. Open Check Manager
4. Verify checks loaded
5. Adjust connection string
6. Run checks

## Tips & Tricks

### Quick Enable/Disable

**Enable all sp_Blitz checks:**
1. Filter to "sp_Blitz"
2. Select all (Ctrl+A)
3. Check first row's Enabled checkbox
4. All selected rows get enabled
5. Save

**Disable all sp_triage:**
1. Filter to "sp_triage"
2. Select all
3. Uncheck Enabled
4. Save

### Find Specific Check

Use Ctrl+F in the data grid to search by:
- ID
- Name
- Category
- Description

### Sort by Column

Click column headers to sort:
- Severity (Critical → Warning → Info)
- Source (alphabetical)
- Name
- Category

### Copy/Paste

**Copy check details:**
1. Select a check
2. Ctrl+C (copies row)
3. Paste into Excel/Notepad

**Doesn't copy SQL** - use "Edit SQL" to view/copy queries

### Undo Changes

**Before saving:**
- Close without saving
- Click "No" when prompted

**After saving:**
- Restore from backup export
- Or re-import from sp_Blitz/sp_triage

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| Ctrl+A | Select all checks |
| Ctrl+Click | Select multiple |
| Shift+Click | Select range |
| Delete | Delete selected (after confirmation) |
| Ctrl+F | Find in grid |
| F5 | Refresh (reload from file) |
| Ctrl+S | Save changes |
| Escape | Close window |

## Common Tasks

### "I want to use only sp_Blitz checks"

1. Open Check Manager
2. Filter to "sp_triage"
3. Select all
4. Click Delete
5. Filter to "Custom"
6. Delete if unwanted
7. Save

### "I want to disable all Info checks"

No automated way yet. Manual:
1. Sort by Severity
2. Select all Info checks
3. Uncheck Enabled
4. Save

### "I want to export only enabled checks"

1. Enable desired checks
2. Can't filter by enabled/disabled yet
3. Export all
4. Manually remove disabled from JSON

### "I broke something, how do I restore?"

**If you have export:**
1. Close app
2. Delete `sql-checks.json`
3. Rename your export to `sql-checks.json`
4. Reopen app

**If no export:**
1. Delete `sql-checks.json`
2. Reopen app (creates defaults)
3. Re-import sp_Blitz/sp_triage
4. Reconfigure

### "How do I see the actual SQL?"

1. Find check in grid
2. Click "Edit SQL" button
3. View in editor
4. Copy if needed
5. Test in SSMS
6. Close editor

## Limitations

❌ **Can't filter by Enabled/Disabled** (yet)
❌ **Can't test queries** from editor (use SSMS)
❌ **Can't bulk-edit queries** (one at a time)
❌ **Can't reorder checks** (sorted by columns only)
❌ **No undo/redo** (use exports for backups)

## Future Features (Planned)

- Query testing directly in editor
- Bulk operations (enable/disable by category)
- Check validation (syntax checking)
- Query templates
- Duplicate check
- Check history/versioning

## Troubleshooting

### "Changes not saving"

- Make sure you clicked "💾 Save Changes"
- Check file permissions on `sql-checks.json`
- Try running as Administrator

### "Import button greyed out"

- You must browse to at least one script file
- Check at least one checkbox
- Select valid .sql files

### "Checks disappeared after save"

- JSON file may be corrupted
- Restore from export backup
- Check Windows Event Log for errors

### "Can't edit SQL query"

- Click "Edit SQL" button (not double-click)
- Row must be selected
- Try restarting app

## Best Practices

1. **Export before major changes** - Always backup
2. **Test queries in SSMS first** - Before adding to checks
3. **Use meaningful names** - "Check TempDB Size" not "Check 1"
4. **Document custom checks** - Put details in Description
5. **Disable rather than delete** - Easier to re-enable later
6. **Keep sources separate** - Use Source filter when editing
7. **Save frequently** - Don't lose work

## Summary

✅ **Visual editing** - No more JSON manipulation  
✅ **Source filtering** - View sp_Blitz, sp_triage, or Custom separately  
✅ **Multi-script import** - Import both scripts at once  
✅ **SQL editor** - Dedicated window for query editing  
✅ **Export/Import** - Easy backup and deployment  
✅ **Intelligent merging** - Updates without breaking existing checks  

**Manage all 245+ checks visually!** 🎉
