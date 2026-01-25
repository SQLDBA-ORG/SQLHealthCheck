# 🚀 Version 2.0 - Multi-Server UI & Interactive Filtering

## 📋 Version History

| Version | Description |
|---------|-------------|
| 1.9 | Enterprise multi-server backend, MVVM architecture, script manager |
| 2.0 | Multi-server UI, interactive filtering, config persistence, server panel |

---

## ✅ What's New in Version 2.0

### 1. Multi-Server Connection Dialog
Enhanced connection dialog supporting multiple servers.

**Features:**
- **Multi-line server input** - Enter one server per line
- **Parallel execution checkbox** - Run checks simultaneously (default: on)
- **Sequential option** - Uncheck for one-at-a-time execution
- **Test all connections** - Tests each server and shows per-server results
- **Bulk connection strings** - Generates connection string for each server

**UI Changes:**
- Server name textbox now multi-line with scrolling
- Height increased to accommodate multiple entries
- Help text shows example format
- "Execution Options" section with parallel checkbox

**Usage:**
```
Enter servers (one per line):
  SERVER01
  SERVER02\INSTANCE
  192.168.1.100
```

### 2. Server Column in Results Grid
Results now show which server each check ran on.

**Features:**
- New "Server" column in DataGrid (after status indicator)
- Search filter includes server name
- Results sorted by Server, then Category, then Check Name

### 3. Interactive Statistics Cards (Clickable Filters)
All statistics cards are now clickable to filter results.

**Cards:**
| Card | Click Action | Highlight Color |
|------|--------------|-----------------|
| INSTANCES | Clears server filter | - |
| TOTAL CHECKS | Shows all results | Light blue |
| PASSED | Shows only passed | Light green |
| CRITICAL | Shows only critical failures | Light red |
| WARNING | Shows only warnings | Light orange |
| INFO | Shows only info items | Light blue |

**Features:**
- Visual highlight shows active filter
- Filters combine with category/server/search
- Clicking different card switches filter
- Clicking same card or TOTAL clears filter

### 4. Server Filter Panel (Left Sidebar)
New server list below Categories for filtering by server.

**Features:**
- Lists all servers from results
- Shows check count per server
- Color indicates status:
  - 🟢 Green = All checks passed
  - 🔴 Red = Has failures
- "All" option when multiple servers
- Click to filter results to that server

**Location:** Left sidebar, below Categories section

### 5. Configuration File Persistence
Server list automatically saved and loaded.

**File:** `SqlHealthMonitor.config` (in app directory)

**Features:**
- Auto-saves when servers changed via Connect dialog
- Auto-loads on app startup
- File watcher detects external edits
- Reloads automatically when config file changes

**Config Format (JSON):**
```json
{
  "Servers": [
    "SERVER01",
    "SERVER02\\INSTANCE",
    "192.168.1.100"
  ],
  "RunInParallel": true,
  "UseWindowsAuth": true,
  "DefaultDatabase": "master",
  "EncryptConnection": true,
  "TrustServerCertificate": true
}
```

**Behavior:**
1. App starts → reads config → populates server list
2. User changes servers → saves to config
3. External edit to config → app detects → reloads servers

### 6. INSTANCES Statistics Card
New card showing count of SQL Server instances checked.

**Features:**
- Shows number of unique servers in results
- Purple color (#6B69D6)
- Click to clear server filter
- Updates after each check run

---

## 🔧 Technical Implementation

### New Files
```
SqlMonitorUI/
├── AppConfig.cs          # Configuration manager with file watching
```

### Modified Files
```
SqlMonitorUI/
├── ConnectionDialog.xaml      # Multi-line server input, parallel checkbox
├── ConnectionDialog.xaml.cs   # Multi-server handling, test all
├── MainWindow.xaml            # Clickable cards, server panel, instances card
├── MainWindow.xaml.cs         # Filter logic, config integration, server list
```

### AppConfig.cs
Singleton configuration manager with:

```csharp
// Singleton access
var config = AppConfig.Instance;

// Properties
config.Servers          // List<string> - Server names
config.RunInParallel    // bool - Parallel execution
config.UseWindowsAuth   // bool - Windows Authentication
config.DefaultDatabase  // string - Default database

// Events
config.ConfigChanged += (sender, args) => {
    if (args.Source == ConfigChangeSource.File)
    {
        // External file change detected
        ReloadServers(args.Servers);
    }
};

// File watching
// Automatically watches SqlHealthMonitor.config for changes
// Debounces rapid changes (500ms)
// Thread-safe loading/saving
```

### Filter System
Multiple filters can be combined:

```csharp
private bool FilterResults(object obj)
{
    // 1. Status filter (from card clicks)
    if (_statusFilter == "Passed" && !result.Passed) return false;
    if (_statusFilter == "Critical" && (result.Passed || result.Severity != "Critical")) return false;
    // etc.

    // 2. Server filter (from server list)
    if (selectedServer != "All" && result.ServerName != selectedServer) return false;

    // 3. Category filter (from category list)
    if (selectedCategory != "All" && result.Category != selectedCategory) return false;

    // 4. Search filter (from search box)
    if (!string.IsNullOrEmpty(searchText))
        return result.CheckName.Contains(searchText) || ...;

    return true;
}
```

### Parallel vs Sequential Execution

```csharp
// Parallel (default)
if (_runInParallel && _connectionStrings.Count > 1)
{
    var tasks = _connectionStrings.Select(async (connStr, index) =>
    {
        using var runner = new CheckRunner(connStr);
        return await runner.RunChecksAsync(checks);
    });
    await Task.WhenAll(tasks);
}

// Sequential
else
{
    foreach (var connStr in _connectionStrings)
    {
        using var runner = new CheckRunner(connStr);
        await runner.RunChecksAsync(checks);
    }
}
```

---

## 🎯 Usage Workflows

### Multi-Server Health Check
1. Click **Connect** button
2. Enter servers (one per line):
   ```
   PROD-SQL01
   PROD-SQL02
   DEV-SQL01
   ```
3. Enable/disable **Run checks in parallel**
4. Click **Test Connection(s)** to verify
5. Click **Connect**
6. Click **Run Checks**
7. View results with Server column
8. Filter by clicking server in left panel

### Filtering Results
1. Run checks across multiple servers
2. Click **CRITICAL** card → see only critical issues
3. Click server **PROD-SQL01** in left panel → see only that server
4. Click category **Backup** → see only backup checks
5. Type in search → further refine
6. Click **TOTAL CHECKS** → clear status filter
7. Click **INSTANCES** → clear server filter

### External Config Editing
1. Run app once to create config
2. Edit `SqlHealthMonitor.config` in text editor:
   ```json
   {
     "Servers": ["NEW-SERVER01", "NEW-SERVER02"]
   }
   ```
3. Save file
4. App automatically reloads server list

---

## 📊 UI Layout

```
┌─────────────────────────────────────────────────────────────────────────┐
│  SQL Server Health Monitor                    [Connect] [Run] [Full]    │
├─────────────────────────────────────────────────────────────────────────┤
│  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐                 │
│  │INST. │ │TOTAL │ │PASSED│ │CRIT. │ │WARN. │ │INFO  │  ← Clickable    │
│  │  3   │ │  45  │ │  38  │ │  2   │ │  3   │ │  2   │                 │
│  └──────┘ └──────┘ └──────┘ └──────┘ └──────┘ └──────┘                 │
├────────────────┬────────────────────────────────────────────────────────┤
│ Categories     │  [Search...]                                           │
│ ● All (45)     │ ┌────────────────────────────────────────────────────┐ │
│ ● Backup (12)  │ │ ● │ Server │ Check Name │ Category │ Sev │ Status │ │
│ ● Security (8) │ │ ● │ SQL01  │ Backup Age │ Backup   │ Cri │ Failed │ │
│ ● Perf (15)    │ │ ● │ SQL02  │ Backup Age │ Backup   │ War │ Failed │ │
│                │ │ ● │ SQL01  │ DB Owner   │ Security │ Inf │ Passed │ │
│ Servers        │ │   │  ...   │    ...     │   ...    │ ... │  ...   │ │
│ ● All (45)     │ └────────────────────────────────────────────────────┘ │
│ 🟢 SQL01 (15)  │                                                        │
│ 🔴 SQL02 (18)  │                                                        │
│ 🟢 SQL03 (12)  │  Last updated: 1/25/2026 2:30 PM (3 servers)          │
└────────────────┴────────────────────────────────────────────────────────┘
```

---

## 🔒 Security Notes

- **Passwords never stored** in config file
- **Windows Auth settings** saved (UseWindowsAuth flag)
- **SQL Auth** requires re-entering password each session
- **Config file** contains server names only, no credentials

---

## 💡 Tips

1. **Test connections first** - Use "Test Connection(s)" before running checks
2. **Use parallel for speed** - Multiple servers run much faster in parallel
3. **Use sequential for debugging** - Easier to track issues one server at a time
4. **Edit config externally** - Quick way to update server list for all users
5. **Combine filters** - Narrow down to specific server + category + severity
6. **Watch the colors** - Server list shows red/green for quick health overview

---

## Summary

### Version 2.0 Highlights
✅ **Multi-server UI** - Enter multiple servers, run checks on all  
✅ **Clickable stat cards** - Filter by Passed/Critical/Warning/Info  
✅ **Server filter panel** - Filter by individual server  
✅ **Config persistence** - Server list saved and auto-loaded  
✅ **File watching** - External config edits detected automatically  
✅ **Server column** - See which server each result came from  
✅ **Parallel execution** - Fast multi-server checks  
✅ **Combined filtering** - Status + Server + Category + Search  

All features are implemented and ready for use!
