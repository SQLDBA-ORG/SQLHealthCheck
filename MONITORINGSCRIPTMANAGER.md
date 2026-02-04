Summary: Script Manager Enhanced with Live Monitoring Config

New Features:
1. Enhanced ScriptManagerWindow (now has 2 tabs):
TabPurpose📜 Diagnostic ScriptsOriginal script management (sp_Blitz, sp_triage, etc.)📊 Live Monitoring QueriesNEW - Manage live monitoring SQL queries
2. Live Monitoring Queries Tab Features:

Global Settings: Refresh Interval (ms), Default Query Timeout (sec)
Query Grid showing all 6 monitoring queries:

Metrics, Sessions, Blocking, TopQueries, DriveLatency, ServerDetails


Per-query settings: Enabled, Description, Timeout, Refresh Every N Ticks
Edit SQL button - Opens dedicated SQL editor window
Reload from File - Reloads config if edited externally
Open Config File - Opens in Notepad for manual editing

3. New LiveQueryEditorWindow:

Full SQL editor with monospace font
Settings for Enabled, Timeout, Refresh interval
Reset to Default button to restore original SQL
Validation before saving

Files Added/Modified:
FileStatusScriptManagerWindow.xamlModified - Added TabControl with 2 tabsScriptManagerWindow.xaml.csModified - Added live config managementLiveQueryEditorWindow.xamlNEW - SQL editor dialogLiveQueryEditorWindow.xaml.csNEW - Editor code-behindLiveMonitoringConfig.csExisting - Config class with file watching
How to Use:

Open Script Manager from the main menu
Click the 📊 Live Monitoring Queries tab
Edit settings directly in the grid, or click Edit SQL to modify queries
Click 💾 Save All to save both scripts and live monitoring config
Changes take effect immediately in the Live Monitoring window (auto-reload)