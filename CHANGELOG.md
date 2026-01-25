# 🔄 Changelog

## Version 2.0 (Latest) - Multi-Server UI & Interactive Filtering! 🖥️
**Major UI Enhancements:**
- **Multi-Server Connection Dialog** - Enter multiple servers (one per line), test all at once
- **Parallel Execution Checkbox** - Choose parallel or sequential execution
- **Server Column in Results** - See which server each check result came from
- **Clickable Statistics Cards** - Click PASSED/CRITICAL/WARNING/INFO to filter results
- **Server Filter Panel** - New left sidebar section listing all servers with health indicators
- **INSTANCES Card** - New statistics card showing count of SQL instances checked

**Configuration Persistence:**
- **AppConfig.cs** - Singleton configuration manager
- **SqlHealthMonitor.config** - JSON config file in app directory
- **Auto-Save** - Server list saved when changed via Connect dialog
- **Auto-Load** - Server list loaded on app startup
- **File Watching** - Detects external config file edits and reloads automatically

**Interactive Filtering:**
- Click any statistics card to filter results by that status
- Cards highlight when filter is active (green/red/orange/blue)
- Server list shows color indicators (🟢 green = healthy, 🔴 red = has failures)
- Filters combine: Status + Server + Category + Search text
- Click TOTAL CHECKS to clear status filter
- Click INSTANCES to clear server filter

**New Files:**
- `SqlMonitorUI/AppConfig.cs` - Configuration manager with file watching

**Modified Files:**
- `ConnectionDialog.xaml/.cs` - Multi-line server input, parallel checkbox, test all
- `MainWindow.xaml` - Clickable cards, server panel, instances card, server column
- `MainWindow.xaml.cs` - Filter handlers, config integration, server list population

**See IMPLEMENTATION-V20-MULTISERVER-UI.md for complete details!**

## Version 1.9 - Enterprise Multi-Server + MVVM Architecture! 🏢
**Major Enterprise Features:**
- **Multi-Server Support** - Run checks across multiple SQL servers in parallel using Task.WhenAll()
- **Connection Dialog** - Enterprise-grade connection management with Windows Auth/SQL Auth
- **Script Manager** - Configure and manage embedded diagnostic scripts (sp_Blitz, sp_triage)
- **Bulk Edit Dialog** - Edit multiple checks at once (Category, Severity, Priority, etc.)
- **Complete Health Check Runner** - Execute all scripts and export results to CSV
- **Memory Monitoring** - Background timer monitors memory pressure and triggers cleanup
- **Progress Window** - Visual progress tracking for long-running operations

**Security Improvements:**
- **Windows Authentication (Recommended)** - Uses Integrated Security=SSPI
- **SQL Server Authentication** - For environments without AD
- **Connection String Builder** - Enterprise patterns for secure connection building
- **Security Validation** - Warns about insecure connection settings
- **Password Protection** - Passwords not stored, re-entered each session

**Architecture Improvements:**
- **CommunityToolkit.Mvvm** - Added for enterprise MVVM patterns
- **Server GC Configuration** - Better memory management for enterprise scenarios
- **Proper IDisposable** - All resources properly disposed with using statements
- **Parallel Processing** - Task.WhenAll() for multi-server operations
- **Connection Throttling** - SemaphoreSlim limits concurrent connections

**New UI Components:**
- ConnectionDialog - Configure server, auth type, encryption options
- BulkEditDialog - Bulk edit Category, Severity, Priority, Enabled, ExecutionType
- ScriptManagerWindow - Manage sp_Blitz, sp_triage, custom scripts
- ProgressWindow - Visual progress for health checks

**New Services:**
- ConnectionStringBuilder - Enterprise connection string patterns
- CompleteHealthCheckRunner - Script execution with CSV export
- Multi-server models (ServerConnection, MultiServerCheckResults, etc.)

**See IMPLEMENTATION-V19-ENTERPRISE.md for complete details!**

## Version 1.8 - Enterprise Resource Management + Enhanced Fields! 🏢
**Enterprise-Grade Features:**
- **15+ New SqlCheck Fields** - Priority, Weight, Impact scores, detailed states
- **@ Placeholder System** - Intelligent replacement: "@ databases" → "3 databases"
- **Resource Manager** - Memory optimization, GC tuning, pressure monitoring
- **Enterprise CheckRunner** - Connection pooling, throttling, proper disposal
- **Placeholder Service** - Smart pluralization, creative messages, recommendations

**New SqlCheck Fields:**
- Priority (1-5), SeverityScore (1-5), Weight (decimal)
- ExecutionType, RowCountCondition, ResultInterpretation
- CheckTriggered & CheckCleared (with @ placeholders)
- DetailedRemediation, SupportType, ImpactScore
- ExpectedState, AdditionalNotes

**Resource Management:**
- Automatic garbage collection optimization
- Memory pressure monitoring
- Safe disposal patterns
- Connection throttling (max 10 concurrent)
- Batch processing with periodic cleanup

**Placeholder Magic:**
- "@ databases have no backups" + count=3 → "3 databases have no backups"
- "@ server(s)" + count=1 → "1 server" (smart pluralization!)
- "@ server(s)" + count=10 → "10 servers"
- Creative messages with emoji based on severity

**Memory Management:**
- Server GC configuration support
- Large object heap compaction
- Memory statistics tracking
- Automatic cleanup every 20 checks
- DisposableScope for safe resource handling

**Enterprise Standards Applied:**
- Zero Trust security model
- Proper disposal patterns (IDisposable)
- Connection pooling
- Async/await throughout
- DRY principle
- Comprehensive error handling

**New Services:**
- ResourceManager.cs - Memory and GC optimization
- PlaceholderService.cs - Intelligent @ replacement
- CheckRunner.Enterprise.cs - Production-grade executor

**See IMPLEMENTATION-V18-ENTERPRISE.md for complete details!**

## Version 1.7 - Query Testing + Bulk Operations!
**Implemented Features:**
- Live Query Testing with results grid
- View Execution Code functionality
- Execution Type field (Binary/RowCount)
- Pass/Fail indicators
- Code Viewer window

**Features Ready to Implement:**
- Bulk Edit Dialog (code provided)
- Script Manager (code provided)
- Complete Health Check runner (code provided)
- CSV export with timestamps (code provided)

**New Models:**
- ScriptConfiguration - For managing embedded diagnostic scripts
- ExecutionType property added to SqlCheck (Binary/RowCount)

**New Windows:**
- Enhanced SqlQueryEditorWindow with test panel and results grid
- CodeViewerWindow for viewing execution SQL
- BulkEditDialog (code provided in implementation guide)
- ScriptManagerWindow (code provided in implementation guide)
- ProgressWindow for health check execution

**Services:**
- CompleteHealthCheckRunner - Runs scripts and exports CSV (code provided)
- Enhanced query execution with DataTable support
- CSV export with proper escaping

**See IMPLEMENTATION-GUIDE-V17.md for complete implementation details!**

## Version 1.6 - Check Manager + Real SQL Queries!
**Major Features:**
- **Check Manager UI** - Full visual editor for all health checks
- **Real SQL Extraction** - No more placeholders! Extracts actual queries
- **Import Dialog** - Select which scripts to import individually
- **Source Filtering** - Filter checks by source with radio buttons
- **Visual SQL Editor** - Edit queries with dedicated editor window
- **Export to JSON** - Export filtered or all checks

**Check Manager Features:**
- View all checks in editable data grid
- Filter by source with radio buttons (All, sp_Blitz, sp_triage, Custom)
- Add/Edit/Delete checks visually
- Edit SQL queries in dedicated editor window
- Enable/disable checks with checkboxes
- Export configurations to JSON
- Import/merge from multiple scripts at once

**Import Improvements:**
- Select sp_Blitz and/or sp_triage independently
- Browse for each script separately
- Preview what will be imported
- Merge intelligently with existing checks
- Extract actual SQL queries instead of placeholders

**SQL Query Improvements:**
- Extracts real queries from sp_Blitz script
- Wraps multi-row results in EXISTS for 0/1 output
- Hand-crafted queries for common checks
- Row count handling for complex queries
- Better query formatting and readability

**Added Files:**
- CheckManagerWindow.xaml/cs - Main check management UI
- ImportChecksDialog.xaml/cs - Multi-script import dialog
- SqlQueryEditorWindow.xaml/cs - Query editor
- AdvancedCheckParser.cs - Improved SQL extraction

## Version 1.5 - Enterprise Security + Windows Server 2016+ Support!
**Major Features:**
- **Windows Server 2016+ Compatibility** - Multi-targeting for .NET Framework 4.8 and .NET 8
- **Auto-Upgrading Security** - Encryption automatically uses latest standards when rebuilt
- **Enterprise Security Service** - Connection string encryption, validation, secure storage
- **Multi-Target Builds** - One codebase, works on Server 2016 through 2025+

**Security Features:**
- Connection string encryption using Windows DPAPI
- SHA-256 (net48) / SHA-512 (net8.0) hashing
- Automatic security upgrades when rebuilt on newer frameworks
- Connection string validation with security warnings
- Secure storage for saved connections
- TLS/SSL support for SQL connections

**Compatibility:**
- net48 target for Windows Server 2016/2019
- net8.0 target for Windows Server 2022/2025
- Works on Windows 10/11 desktop
- Self-contained deployment option
- No code changes needed for security upgrades

**Added:**
- SecurityService class with encryption, hashing, validation
- SecureConnectionStorage for encrypted connection persistence
- DEPLOYMENT-GUIDE.md with full compatibility documentation
- Framework-specific security implementations
- Package auto-upgrade with wildcard versions (5.*)

## Version 1.4 - sp_triage Support + Source Tracking!
**New Features:**
- **sp_triage Support!** Import checks from sp_triage.sql (35+ check categories)
- **Source Tracking** - Each check now shows its source (sp_Blitz, sp_triage, or Custom)
- **Source Column** in results grid shows where each check came from
- **Auto-Detection** - Automatically detects which script you're importing
- **Unified Import Button** - "Import Checks" button handles both scripts

**Added:**
- sp_triage parser extracts 35+ output table categories as checks
- Source field in SqlCheck model
- Source column in UI data grid
- Smart script detection from filename
- Support for multiple check sources in one app

**Improved:**
- Import dialog now shows source being imported
- Results clearly indicate which script a check came from
- Better organization when mixing checks from different sources

## Version 1.3 - sp_Blitz Import Feature!
**New Feature:** Import ALL 210+ sp_Blitz Checks Automatically
- Added "📥 Import sp_Blitz" button to UI
- Automatically parses sp_Blitz.sql and extracts ALL 210+ checks
- Improved parser catches all CheckIDs (1-2301)
- Smart merging preserves your custom settings
- ~34 checks have working queries immediately
- ~176 are placeholders you can implement
- See IMPORT-SP-BLITZ-GUIDE.md for full documentation

**Added:**
- SpBlitzParser.cs service for parsing sp_Blitz.sql
- File dialog to select sp_Blitz.sql
- Import confirmation with summary
- Automatic merging with existing checks
- Priority-to-severity mapping (1-50=Critical, 51-100=Warning, 101+=Info)

## Version 1.2
**Fixed:** XAML Parse Exception with DropShadowEffect
- Removed DropShadowEffect from CardStyle (App.xaml)
- Removed DropShadowEffect from Header border (MainWindow.xaml)
- UI will now work on all Windows systems without graphics errors

## Version 1.1
**Fixed:** NuGet Package Restore Issue
- Added nuget.config to ensure NuGet.org is used
- Added FIX-NUGET.md guide for package issues

## Version 1.0
**Initial Release**
- WPF Dashboard UI with SolarWinds-style interface
- Console application for automation
- 12 pre-built SQL Server health checks
- JSON-based check configuration
- Complete documentation

---

## Known Issues
None currently! 🎉

## If You Experience Any Issues

1. Check TROUBLESHOOTING.md
2. Check FIX-NUGET.md for package issues
3. Check IMPORT-SP-BLITZ-GUIDE.md for import issues
4. Try running from command line: `dotnet run --project SqlMonitorUI`
