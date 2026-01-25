# 🖥️ SQL Monitor UI - SolarWinds-Style Dashboard

A professional WPF desktop application for monitoring SQL Server health with a modern, clean interface similar to SolarWinds Database Performance Monitor.

## 📸 Features

### Dashboard Overview
- **Real-time Statistics Cards**: Total checks, passed, critical, warnings, and info at a glance
- **Color-coded Status Indicators**: Green (pass), Red (critical), Orange (warning), Blue (info)
- **Category Filtering**: Quick filter by Backup, Security, Performance, Configuration, etc.
- **Search Functionality**: Find specific checks instantly
- **Professional Data Grid**: Sortable, searchable results with detailed information

### Visual Design
- Clean, modern interface with card-based layout
- Color-coded severity indicators
- Drop shadows and hover effects
- Responsive grid layout
- Empty states and loading indicators

## 🚀 Quick Start

### Option 1: Visual Studio

1. **Open Solution**
   ```
   Open SqlHealthCheck.sln in Visual Studio
   ```

2. **Set Startup Project**
   - Right-click on `SqlMonitorUI` project
   - Select "Set as Startup Project"

3. **Run**
   - Press F5 or click Start
   - Enter your SQL Server connection string
   - Click "Run Checks"

### Option 2: Command Line

```bash
cd SqlMonitorUI
dotnet run
```

## 🎨 UI Overview

### Header Bar
- **Connection String Box**: Enter your SQL Server connection
- **Run Checks Button**: Execute all enabled checks
- **Refresh Button**: Re-run checks with current connection
- **Last Updated**: Timestamp of last check run

### Statistics Cards (Top Row)
```
┌─────────────┬─────────────┬─────────────┬─────────────┬─────────────┐
│ TOTAL       │ PASSED      │ CRITICAL    │ WARNING     │ INFO        │
│ CHECKS      │             │             │             │             │
│    12       │     10      │      1      │      1      │      0      │
└─────────────┴─────────────┴─────────────┴─────────────┴─────────────┘
```

### Main Content Area
```
┌────────────────┬──────────────────────────────────────────────────┐
│ Categories     │ Check Results                                    │
│                │ ┌────────────────────────────────────────────┐   │
│ • All (12)     │ │ Search: [         🔍         ]            │   │
│ • Backup (2)   │ └────────────────────────────────────────────┘   │
│ • Security (2) │                                                  │
│ • Performance  │ ● Check Name        Category  Severity  Status  │
│ • Config (4)   │ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  │
│ • Integrity    │ ✅ Full Backup       Backup   Critical  Passed  │
│                │ ❌ SA Account        Security Warning  Failed   │
│                │ ✅ Index Frag        Perf     Warning  Passed   │
│                │                                                  │
└────────────────┴──────────────────────────────────────────────────┘
```

### Status Indicators
- ✅ **Green Circle**: Check passed
- ❌ **Red Circle**: Critical issue
- ⚠️ **Orange Circle**: Warning
- ℹ️ **Blue Circle**: Info

### Severity Badges
- **Critical**: Red background, white text
- **Warning**: Orange background, white text
- **Info**: Blue background, white text

## 🎯 Usage Examples

### Basic Health Check
1. Enter connection string: `Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=true;`
2. Click "Run Checks"
3. View results in the grid

### Filter by Category
1. Click a category in the left sidebar (e.g., "Backup")
2. Results automatically filter to show only that category

### Search for Specific Checks
1. Type in the search box (e.g., "backup" or "index")
2. Results filter as you type

### Monitor Multiple Servers
1. Run checks on Server 1
2. Note the results
3. Change connection string to Server 2
4. Click "Run Checks" again
5. Compare results

## 🔧 Customization

### Change Color Scheme

Edit `App.xaml` resources:

```xml
<!-- Change primary color from blue to your brand color -->
<SolidColorBrush x:Key="PrimaryBrush" Color="#0078D4"/>
<!-- Change to purple: -->
<SolidColorBrush x:Key="PrimaryBrush" Color="#7B2CBF"/>
```

### Add New Statistics Cards

Edit `MainWindow.xaml` Grid.Row="1" section:

```xml
<!-- Add a new stat card -->
<Border Grid.Column="5" Style="{StaticResource CardStyle}">
    <StackPanel>
        <TextBlock Text="FAILED" Style="{StaticResource StatLabelStyle}"/>
        <TextBlock x:Name="FailedChecksText" 
                   Text="0" 
                   Style="{StaticResource StatNumberStyle}"
                   Foreground="Red"/>
    </StackPanel>
</Border>
```

Then update in code-behind:
```csharp
FailedChecksText.Text = _allResults.Count(r => !r.Passed).ToString();
```

### Modify Category Colors

Edit `GetCategoryColor()` method in `MainWindow.xaml.cs`:

```csharp
private Color GetCategoryColor(string category)
{
    return category switch
    {
        "Backup" => Color.FromRgb(16, 124, 16),      // Your color here
        "Security" => Color.FromRgb(216, 59, 1),
        // Add new categories or change colors
        _ => Color.FromRgb(128, 128, 128)
    };
}
```

## 📊 Data Grid Columns

| Column | Description |
|--------|-------------|
| Status Indicator | Color-coded circle (green/red/orange/blue) |
| Check Name | Name of the health check |
| Category | Backup, Security, Performance, etc. |
| Severity | Critical, Warning, or Info badge |
| Status | "Passed" or "Failed" text |
| Details | Message explaining the result |
| Checked | Timestamp when check was executed |

## 🎨 UI Components Explained

### Cards
```csharp
<Border Style="{StaticResource CardStyle}">
    <!-- Your content here -->
</Border>
```
- White background
- Subtle shadow
- Rounded corners
- Padding for spacing

### Modern Buttons
```csharp
<Button Style="{StaticResource ModernButtonStyle}" 
        Content="Click Me" 
        Click="Button_Click"/>
```
- Blue background (customizable)
- White text
- Hover effect
- Rounded corners

### Status Indicators
```csharp
Fill="{Binding StatusColor}"  // Dynamically colored based on result
```

## 🔄 Refresh Behavior

### Auto-refresh (Future Enhancement)
Add to constructor:
```csharp
var timer = new DispatcherTimer();
timer.Interval = TimeSpan.FromMinutes(5);
timer.Tick += async (s, e) => await RunChecksButton_Click(null, null);
timer.Start();
```

### Manual Refresh
Click the refresh button (↻) or "Run Checks" to re-execute all checks.

## 📱 Responsive Design

The UI adapts to different window sizes:
- Minimum recommended: 1200x700
- Cards stack at smaller widths
- DataGrid columns resize proportionally

## 🎯 Keyboard Shortcuts

Add keyboard shortcuts by editing `MainWindow.xaml`:

```xml
<Window.InputBindings>
    <KeyBinding Key="F5" Command="{Binding RunChecksCommand}"/>
    <KeyBinding Key="R" Modifiers="Ctrl" Command="{Binding RefreshCommand}"/>
</Window.InputBindings>
```

## 🐛 Troubleshooting

### Connection Fails
- Verify SQL Server is running
- Check connection string format
- Ensure TrustServerCertificate=true for local dev
- Verify network connectivity and firewall rules

### No Results Show
- Check if checks are enabled in `sql-checks.json`
- Verify repository loaded successfully
- Check Output window for errors

### UI Looks Different
- Ensure .NET 8 Windows Desktop Runtime is installed
- Check Windows DPI scaling settings
- Verify WPF dependencies are restored

## 🚀 Advanced Features

### Export to Excel
Add this method to `MainWindow.xaml.cs`:

```csharp
private void ExportToExcel()
{
    // Use EPPlus or similar library
    var package = new ExcelPackage();
    var worksheet = package.Workbook.Worksheets.Add("Health Check Results");
    
    // Add headers and data
    worksheet.Cells["A1"].Value = "Check Name";
    // ... populate cells
    
    package.SaveAs(new FileInfo("health-check-results.xlsx"));
}
```

### Email Alerts
Add SMTP configuration and send email on critical failures:

```csharp
if (CriticalIssuesText.Text != "0")
{
    var smtp = new SmtpClient("smtp.office365.com");
    // Send alert email
}
```

### Historical Trending
Store results in SQLite database and show trend charts using LiveCharts.

## 📦 Dependencies

- .NET 8.0 Windows Desktop
- Microsoft.Data.SqlClient (SQL connectivity)
- LiveChartsCore.SkiaSharpView.WPF (optional, for charts)

## 🎨 Design Inspiration

This UI takes inspiration from:
- SolarWinds Database Performance Monitor
- Azure Portal
- Modern Windows 11 design language
- Material Design principles

Enjoy your professional SQL monitoring dashboard! 🎉
