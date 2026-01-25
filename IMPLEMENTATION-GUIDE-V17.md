# 🚀 Version 1.7 - Implementation Guide

## ✅ Features Implemented

### 1. Enhanced SQL Query Editor with Test Execution ✅
- **View Execution Code** button shows exact SQL that will run
- **Test Query** button executes against live SQL Server
- **Results Grid** displays query output in real-time
- **Execution Type** dropdown: Binary (0/1) or RowCount
- **Pass/Fail Indicator** shows if test passed
- **Connection String** passed from main window

**Files Created:**
- SqlQueryEditorWindow.xaml (updated with test panel)
- SqlQueryEditorWindow.xaml.cs (with async test execution)
- CodeViewerWindow.xaml/cs (for viewing execution code)

### 2. Execution Type Field ✅
- Added `ExecutionType` property to SqlCheck model
- Options: "Binary" or "RowCount"
- Binary: Expects 0/1 result
- RowCount: Counts rows (0 rows = pass)

**Files Updated:**
- SqlCheck.cs (ExecutionType property added)
- SqlQueryEditorWindow (dropdown for selection)

### 3. Script Configuration Model ✅
- ScriptConfiguration.cs created
- Ready for embedded scripts
- Configurable execution parameters
- CSV export capability

## 🔧 Features To Complete

### 1. Bulk Edit Operations

**Add to CheckManagerWindow.xaml:**

```xml
<!-- Add after Delete button -->
<Button x:Name="BulkEditButton" 
        Content="⚡ Bulk Edit" 
        Click="BulkEditButton_Click"
        Padding="12,8"
        Margin="10,0,0,0"
        Background="#6B69D6"
        Foreground="White"
        BorderThickness="0"
        FontSize="13"
        Cursor="Hand"/>
```

**Add to CheckManagerWindow.xaml.cs:**

```csharp
private void BulkEditButton_Click(object sender, RoutedEventArgs e)
{
    var selected = ChecksDataGrid.SelectedItems.Cast<SqlCheck>().ToList();
    
    if (selected.Count == 0)
    {
        MessageBox.Show("Please select checks to bulk edit.", "Bulk Edit", 
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    var bulkEditor = new BulkEditDialog(selected);
    if (bulkEditor.ShowDialog() == true)
    {
        // Apply bulk changes
        foreach (var check in selected)
        {
            if (bulkEditor.ChangeCategory)
                check.Category = bulkEditor.Category;
            if (bulkEditor.ChangeSeverity)
                check.Severity = bulkEditor.Severity;
            if (bulkEditor.ChangeEnabled)
                check.Enabled = bulkEditor.Enabled;
            if (bulkEditor.ChangeExecutionType)
                check.ExecutionType = bulkEditor.ExecutionType;
        }

        ApplyFilter(); // Refresh grid
        MessageBox.Show($"Bulk edited {selected.Count} checks.\n\nClick 'Save Changes' to persist.",
            "Bulk Edit Complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
```

**Create BulkEditDialog.xaml:**

```xml
<Window x:Class="SqlMonitorUI.BulkEditDialog"
        Title="Bulk Edit Checks" 
        Height="400" 
        Width="500">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" 
                   Text="Apply Changes to Selected Checks" 
                   FontSize="18" 
                   FontWeight="SemiBold" 
                   Margin="0,0,0,20"/>

        <StackPanel Grid.Row="1" Spacing="15">
            <!-- Category -->
            <StackPanel>
                <CheckBox x:Name="ChangeCategoryCheck" 
                          Content="Change Category"
                          Checked="ChangeCheck_Changed"
                          Unchecked="ChangeCheck_Changed"/>
                <ComboBox x:Name="CategoryCombo"
                          IsEnabled="False"
                          Margin="20,5,0,0">
                    <ComboBoxItem Content="Backup"/>
                    <ComboBoxItem Content="Security"/>
                    <ComboBoxItem Content="Performance"/>
                    <ComboBoxItem Content="Configuration"/>
                    <ComboBoxItem Content="Integrity"/>
                    <ComboBoxItem Content="Reliability"/>
                    <ComboBoxItem Content="Storage"/>
                    <ComboBoxItem Content="Custom"/>
                </ComboBox>
            </StackPanel>

            <!-- Severity -->
            <StackPanel>
                <CheckBox x:Name="ChangeSeverityCheck" 
                          Content="Change Severity"
                          Checked="ChangeCheck_Changed"
                          Unchecked="ChangeCheck_Changed"/>
                <ComboBox x:Name="SeverityCombo"
                          IsEnabled="False"
                          Margin="20,5,0,0">
                    <ComboBoxItem Content="Critical"/>
                    <ComboBoxItem Content="Warning"/>
                    <ComboBoxItem Content="Info"/>
                </ComboBox>
            </StackPanel>

            <!-- Enabled -->
            <StackPanel>
                <CheckBox x:Name="ChangeEnabledCheck" 
                          Content="Change Enabled State"
                          Checked="ChangeCheck_Changed"
                          Unchecked="ChangeCheck_Changed"/>
                <ComboBox x:Name="EnabledCombo"
                          IsEnabled="False"
                          Margin="20,5,0,0">
                    <ComboBoxItem Content="Enabled" Tag="True"/>
                    <ComboBoxItem Content="Disabled" Tag="False"/>
                </ComboBox>
            </StackPanel>

            <!-- Execution Type -->
            <StackPanel>
                <CheckBox x:Name="ChangeExecutionTypeCheck" 
                          Content="Change Execution Type"
                          Checked="ChangeCheck_Changed"
                          Unchecked="ChangeCheck_Changed"/>
                <ComboBox x:Name="ExecutionTypeCombo"
                          IsEnabled="False"
                          Margin="20,5,0,0">
                    <ComboBoxItem Content="Binary (0/1)" Tag="Binary"/>
                    <ComboBoxItem Content="Row Count" Tag="RowCount"/>
                </ComboBox>
            </StackPanel>
        </StackPanel>

        <StackPanel Grid.Row="2" 
                    Orientation="Horizontal" 
                    HorizontalAlignment="Right"
                    Margin="0,20,0,0">
            <Button Content="Apply" 
                    Click="ApplyButton_Click"
                    Padding="15,10"
                    Margin="0,0,10,0"
                    Background="#107C10"
                    Foreground="White"/>
            <Button Content="Cancel" 
                    Click="CancelButton_Click"
                    Padding="15,10"
                    Background="#666666"
                    Foreground="White"/>
        </StackPanel>
    </Grid>
</Window>
```

### 2. Script Manager for Embedded Scripts

**Create ScriptManagerWindow.xaml:**

```xml
<Window x:Class="SqlMonitorUI.ScriptManagerWindow"
        Title="Script Manager - Embedded Diagnostic Scripts" 
        Height="600" 
        Width="900">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Header -->
        <TextBlock Grid.Row="0" 
                   Text="Manage Diagnostic Scripts" 
                   FontSize="20" 
                   FontWeight="SemiBold" 
                   Margin="0,0,0,15"/>

        <!-- Toolbar -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,15">
            <Button Content="📂 Scan Scripts Folder"
                    Click="ScanScriptsButton_Click"
                    Padding="12,8"
                    Margin="0,0,10,0"
                    Background="#0078D4"
                    Foreground="White"/>
            
            <Button Content="➕ Add Script"
                    Click="AddScriptButton_Click"
                    Padding="12,8"
                    Margin="0,0,10,0"
                    Background="#107C10"
                    Foreground="White"/>
            
            <Button Content="🗑️ Remove Selected"
                    Click="RemoveScriptButton_Click"
                    Padding="12,8"
                    Background="#D83B01"
                    Foreground="White"/>
        </StackPanel>

        <!-- Scripts Grid -->
        <DataGrid Grid.Row="2"
                  x:Name="ScriptsDataGrid"
                  AutoGenerateColumns="False"
                  CanUserAddRows="False">
            <DataGrid.Columns>
                <DataGridCheckBoxColumn Header="Enabled" 
                                        Binding="{Binding Enabled}"/>
                <DataGridTextColumn Header="Name" 
                                    Binding="{Binding Name}" 
                                    Width="200"/>
                <DataGridTextColumn Header="Script Path" 
                                    Binding="{Binding ScriptPath}" 
                                    Width="250"/>
                <DataGridTextColumn Header="Parameters" 
                                    Binding="{Binding ExecutionParameters}" 
                                    Width="150"/>
                <DataGridTextColumn Header="Order" 
                                    Binding="{Binding ExecutionOrder}" 
                                    Width="60"/>
                <DataGridCheckBoxColumn Header="Export CSV" 
                                        Binding="{Binding ExportToCsv}"/>
                <DataGridTemplateColumn Header="Actions" Width="100">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <Button Content="Edit" 
                                    Click="EditScriptButton_Click"
                                    Tag="{Binding}"/>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
            </DataGrid.Columns>
        </DataGrid>

        <!-- Bottom Buttons -->
        <StackPanel Grid.Row="3" 
                    Orientation="Horizontal" 
                    HorizontalAlignment="Right"
                    Margin="0,15,0,0">
            <Button Content="💾 Save Configuration"
                    Click="SaveButton_Click"
                    Padding="15,10"
                    Margin="0,0,10,0"
                    Background="#107C10"
                    Foreground="White"/>
            <Button Content="Close"
                    Click="CloseButton_Click"
                    Padding="15,10"
                    Background="#666666"
                    Foreground="White"/>
        </StackPanel>
    </Grid>
</Window>
```

### 3. Complete Health Check Runner

**Add to MainWindow.xaml (after Run Checks button):**

```xml
<Button x:Name="RunCompleteHealthCheckButton" 
        Content="🏥 Run Complete Health Check" 
        Style="{StaticResource ModernButtonStyle}"
        Click="RunCompleteHealthCheckButton_Click"
        Margin="0,0,5,0"
        ToolTip="Run all embedded diagnostic scripts and export to CSV"/>
```

**Add to MainWindow.xaml.cs:**

```csharp
private async void RunCompleteHealthCheckButton_Click(object sender, RoutedEventArgs e)
{
    var connectionString = ConnectionStringTextBox.Text.Trim();
    
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        MessageBox.Show("Please enter a connection string.", 
            "Connection Required", 
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    // Load script configurations
    var scriptRunner = new CompleteHealthCheckRunner(connectionString);
    var scripts = await scriptRunner.LoadScriptConfigurationsAsync();

    if (scripts.Count == 0)
    {
        MessageBox.Show("No scripts configured. Please configure scripts in Script Manager first.",
            "No Scripts", MessageBoxButton.OK, MessageBoxImage.Information);
        return;
    }

    var result = MessageBox.Show(
        $"This will execute {scripts.Count} diagnostic script(s):\n\n" +
        string.Join("\n", scripts.Select(s => $"• {s.Name}")) +
        "\n\nResults will be exported to CSV in the output folder.\n\nContinue?",
        "Confirm Complete Health Check",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);

    if (result != MessageBoxResult.Yes)
        return;

    RunCompleteHealthCheckButton.IsEnabled = false;

    try
    {
        var progress = new ProgressWindow();
        progress.Show();

        var outputFolder = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, 
            "output");
        
        Directory.CreateDirectory(outputFolder);

        await scriptRunner.RunCompleteHealthCheckAsync(
            scripts,
            outputFolder,
            progress.UpdateProgress);

        progress.Close();

        MessageBox.Show(
            $"Complete health check finished!\n\n" +
            $"Results exported to:\n{outputFolder}",
            "Health Check Complete",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        // Open output folder
        System.Diagnostics.Process.Start("explorer.exe", outputFolder);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Error running health check: {ex.Message}",
            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
    finally
    {
        RunCompleteHealthCheckButton.IsEnabled = true;
    }
}
```

### 4. Complete Health Check Runner Service

**Create CompleteHealthCheckRunner.cs:**

```csharp
using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using SqlCheckLibrary.Models;

namespace SqlCheckLibrary.Services
{
    public class CompleteHealthCheckRunner
    {
        private readonly string _connectionString;
        private const string SCRIPT_CONFIG_FILE = "script-configurations.json";

        public CompleteHealthCheckRunner(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<List<ScriptConfiguration>> LoadScriptConfigurationsAsync()
        {
            if (!File.Exists(SCRIPT_CONFIG_FILE))
                return new List<ScriptConfiguration>();

            var json = await File.ReadAllTextAsync(SCRIPT_CONFIG_FILE);
            return JsonSerializer.Deserialize<List<ScriptConfiguration>>(json) 
                   ?? new List<ScriptConfiguration>();
        }

        public async Task RunCompleteHealthCheckAsync(
            List<ScriptConfiguration> scripts,
            string outputFolder,
            Action<string, int> progressCallback)
        {
            var serverName = await GetServerNameAsync();
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

            var enabledScripts = scripts
                .Where(s => s.Enabled)
                .OrderBy(s => s.ExecutionOrder)
                .ToList();

            for (int i = 0; i < enabledScripts.Count; i++)
            {
                var script = enabledScripts[i];
                var progress = (int)((i + 1) * 100.0 / enabledScripts.Count);
                
                progressCallback?.Invoke($"Executing {script.Name}...", progress);

                try
                {
                    await ExecuteScriptAndExportAsync(
                        script, 
                        serverName, 
                        timestamp, 
                        outputFolder);
                }
                catch (Exception ex)
                {
                    // Log error but continue
                    var errorFile = Path.Combine(
                        outputFolder, 
                        $"{serverName}_{script.Name}_ERROR_{timestamp}.txt");
                    await File.WriteAllTextAsync(errorFile, ex.ToString());
                }
            }
        }

        private async Task ExecuteScriptAndExportAsync(
            ScriptConfiguration script,
            string serverName,
            string timestamp,
            string outputFolder)
        {
            // Read script file
            var scriptPath = Path.Combine("scripts", script.ScriptPath);
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Script not found: {scriptPath}");

            var scriptContent = await File.ReadAllTextAsync(scriptPath);

            // Execute script
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                using (var command = new SqlCommand(scriptContent, connection))
                {
                    command.CommandTimeout = script.TimeoutSeconds > 0 
                        ? script.TimeoutSeconds 
                        : 300;

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        var resultSetIndex = 0;
                        
                        do
                        {
                            var dataTable = new DataTable();
                            dataTable.Load(reader);

                            if (script.ExportToCsv && dataTable.Rows.Count > 0)
                            {
                                var fileName = $"{serverName}_{script.Name}_" +
                                             $"{resultSetIndex}_{timestamp}.csv";
                                var filePath = Path.Combine(outputFolder, fileName);

                                await ExportToCsvAsync(dataTable, filePath);
                            }

                            resultSetIndex++;
                        } 
                        while (!reader.IsClosed);
                    }
                }

                // Execute parameters command if specified
                if (!string.IsNullOrWhiteSpace(script.ExecutionParameters))
                {
                    var paramsCommand = $"EXEC {Path.GetFileNameWithoutExtension(script.ScriptPath)} " +
                                      script.ExecutionParameters;

                    using (var command = new SqlCommand(paramsCommand, connection))
                    {
                        command.CommandTimeout = script.TimeoutSeconds > 0 
                            ? script.TimeoutSeconds 
                            : 300;

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            var resultSetIndex = 0;
                            
                            do
                            {
                                var dataTable = new DataTable();
                                dataTable.Load(reader);

                                if (script.ExportToCsv && dataTable.Rows.Count > 0)
                                {
                                    var fileName = $"{serverName}_{script.Name}_params_" +
                                                 $"{resultSetIndex}_{timestamp}.csv";
                                    var filePath = Path.Combine(outputFolder, fileName);

                                    await ExportToCsvAsync(dataTable, filePath);
                                }

                                resultSetIndex++;
                            } 
                            while (!reader.IsClosed);
                        }
                    }
                }
            }
        }

        private async Task ExportToCsvAsync(DataTable dataTable, string filePath)
        {
            var csv = new StringBuilder();

            // Header
            csv.AppendLine(string.Join(",", 
                dataTable.Columns.Cast<DataColumn>()
                .Select(column => EscapeCsv(column.ColumnName))));

            // Rows
            foreach (DataRow row in dataTable.Rows)
            {
                csv.AppendLine(string.Join(",", 
                    row.ItemArray.Select(field => EscapeCsv(field?.ToString() ?? ""))));
            }

            await File.WriteAllTextAsync(filePath, csv.ToString());
        }

        private string EscapeCsv(string value)
        {
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }

        private async Task<string> GetServerNameAsync()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new SqlCommand("SELECT @@SERVERNAME", connection))
                {
                    var result = await command.ExecuteScalarAsync();
                    return result?.ToString() ?? "UNKNOWN";
                }
            }
        }
    }
}
```

## 📁 Folder Structure

```
SqlHealthCheck/
├── scripts/                    (Embedded diagnostic scripts)
│   ├── sp_Blitz.sql
│   ├── sp_triage.sql
│   └── (other diagnostic scripts)
├── output/                     (CSV exports)
│   ├── SERVER01_sp_Blitz_0_20260123-143022.csv
│   ├── SERVER01_sp_triage_0_20260123-143045.csv
│   └── ...
├── script-configurations.json  (Script settings)
└── sql-checks.json            (Check configurations)
```

## 🎯 Usage Workflow

### 1. Set Up Scripts
1. Create `scripts` folder in app directory
2. Copy sp_Blitz.sql and sp_triage.sql there
3. Click "Script Manager" button
4. Click "Scan Scripts Folder"
5. Configure execution parameters
6. Save configuration

### 2. Run Complete Health Check
1. Enter SQL Server connection string
2. Click "🏥 Run Complete Health Check"
3. Confirm execution
4. Wait for progress
5. Review CSV files in output folder

### 3. Bulk Edit Checks
1. Open Check Manager
2. Filter by source (e.g., sp_Blitz)
3. Select multiple checks (Ctrl+Click)
4. Click "⚡ Bulk Edit"
5. Choose what to change
6. Apply and save

## 📊 Summary of New Features

✅ Test query execution with live results  
✅ View execution code for each check  
✅ Execution Type field (Binary/RowCount)  
✅ Pass/Fail indicators in test results  
🔧 Bulk edit operations (code provided)  
🔧 Script Manager for embedded scripts (code provided)  
🔧 Complete Health Check runner (code provided)  
🔧 CSV export with server name and timestamp (code provided)  

All the code is provided above - just add the files and integrate!
