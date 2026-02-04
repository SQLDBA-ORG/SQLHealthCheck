Summary: Tabbed MainWindow
I've restructured the MainWindow to use a tabbed interface:
New Layout:
Header Bar (Simplified):

Title and status
Connection string / server display
Essential buttons: Connect, Config, Full Audit, Upload, About

Tab Control:
TabContent🔄 Quick ChecksStatistics cards, filter sidebar, results grid, Run Checks button📊 Live MonitorPlaceholder with "Start Live Monitor" button (opens LiveMonitoringWindow)⚙️ Manage ChecksHidden by default (can be shown if needed)
Changes Made:
MainWindow.xaml:

Converted to TabControl with styled tabs
Quick Checks tab contains all the original check functionality
Live Monitor tab has a placeholder with Start button
Cleaner header with fewer buttons (moved Live Monitor and Run Checks to tabs)
Smaller, more compact cards
Run Checks button moved inline with the stats cards

MainWindow.xaml.cs:

Added MainTabControl_SelectionChanged handler
Added StartLiveMonitorButton_Click handler
Updated UpdateServerDisplayText to enable/disable Live Monitor button
Removed RefreshButton references (no longer in UI)
Updated LastUpdateText format

Button Changes:
Before (Header)After (Header)ConnectConnectChecks(removed - can use Config)ScriptsConfig (renamed)Live Monitor(moved to tab)Run Quick Checks(moved to tab)Full AuditFull AuditUploadUploadAboutAbout (smaller)Refresh(removed - use Run Checks in tab)
