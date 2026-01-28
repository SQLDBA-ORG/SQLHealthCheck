using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using SqlCheckLibrary.Models;
using SqlCheckLibrary.Services;
using System.Windows.Controls;


namespace SqlMonitorUI
{
    public partial class ScriptManagerWindow : Window
    {
        private readonly CompleteHealthCheckRunner _runner;
        private ObservableCollection<ScriptConfiguration> _scripts;
        private const string SCRIPTS_FOLDER = "scripts";
        private string _connectionString;

        public ScriptManagerWindow(string connectionString)
        {
            InitializeComponent();
            _runner = new CompleteHealthCheckRunner(connectionString);
            _scripts = new ObservableCollection<ScriptConfiguration>();
            ScriptsDataGrid.ItemsSource = _scripts;
            _connectionString = connectionString;
            Loaded += ScriptManagerWindow_Loaded;
        }



        private async void ScriptManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var scripts = await _runner.LoadScriptConfigurationsAsync();
                _scripts.Clear();
                foreach (var script in scripts)
                {
                    _scripts.Add(script);
                }
                UpdateStatus($"Loaded {_scripts.Count} script configuration(s)");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading configurations: {ex.Message}",
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void ScanScriptsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create scripts folder if it doesn't exist
                if (!Directory.Exists(SCRIPTS_FOLDER))
                {
                    Directory.CreateDirectory(SCRIPTS_FOLDER);
                    MessageBox.Show($"Created '{SCRIPTS_FOLDER}' folder.\n\nPlease copy your diagnostic scripts (sp_Blitz.sql, sp_triage.sql, etc.) into this folder and scan again.",
                        "Scripts Folder Created", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var scannedScripts = await _runner.ScanScriptsFolderAsync(SCRIPTS_FOLDER);

                if (scannedScripts.Count == 0)
                {
                    MessageBox.Show($"No SQL files found in '{SCRIPTS_FOLDER}' folder.\n\nPlease copy your diagnostic scripts into this folder.",
                        "No Scripts Found", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Merge with existing (don't duplicate)
                int newCount = 0;
                foreach (var script in scannedScripts)
                {
                    var existing = _scripts.FirstOrDefault(s =>
                        s.ScriptPath.Equals(script.ScriptPath, StringComparison.OrdinalIgnoreCase));

                    if (existing == null)
                    {
                        _scripts.Add(script);
                        newCount++;
                    }
                }

                if (newCount > 0)
                {
                    UpdateStatus($"Added {newCount} new script(s)");
                    MessageBox.Show($"Found {newCount} new script(s) in '{SCRIPTS_FOLDER}' folder.",
                        "Scan Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("All scripts in the folder are already configured.",
                        "Scan Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning scripts: {ex.Message}",
                    "Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddScriptButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select SQL Script",
                Filter = "SQL Files (*.sql)|*.sql|All Files (*.*)|*.*",
                InitialDirectory = Path.GetFullPath(SCRIPTS_FOLDER)
            };

            if (dialog.ShowDialog() == true)
            {
                var fileName = Path.GetFileName(dialog.FileName);
                var name = Path.GetFileNameWithoutExtension(dialog.FileName);

                // Copy to scripts folder if not already there
                var destPath = Path.Combine(SCRIPTS_FOLDER, fileName);
                if (!File.Exists(destPath))
                {
                    Directory.CreateDirectory(SCRIPTS_FOLDER);
                    File.Copy(dialog.FileName, destPath);
                }

                // Check if already exists
                if (_scripts.Any(s => s.ScriptPath.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("This script is already in the configuration.",
                        "Duplicate Script", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var script = new ScriptConfiguration
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Description = $"Diagnostic script: {fileName}",
                    ScriptPath = fileName,
                    Enabled = true,
                    ExecutionOrder = _scripts.Count + 1,
                    TimeoutSeconds = 300,
                    ExportToCsv = true
                };

                _scripts.Add(script);
                UpdateStatus($"Added script: {name}");
            }
        }

        private void RemoveScriptButton_Click(object sender, RoutedEventArgs e)
        {
            if (ScriptsDataGrid.SelectedItem is ScriptConfiguration selected)
            {
                var result = MessageBox.Show($"Remove '{selected.Name}' from configuration?\n\n(The script file will not be deleted)",
                    "Confirm Remove", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _scripts.Remove(selected);
                    UpdateStatus($"Removed script: {selected.Name}");
                }
            }
            else
            {
                MessageBox.Show("Please select a script to remove.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenScriptsFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var fullPath = Path.GetFullPath(SCRIPTS_FOLDER);
                Directory.CreateDirectory(fullPath);
                System.Diagnostics.Process.Start("explorer.exe", fullPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening folder: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _runner.SaveScriptConfigurationsAsync(_scripts.ToList());
                UpdateStatus("Configuration saved");
                MessageBox.Show("Script configuration saved successfully!",
                    "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving configuration: {ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void UpdateStatus(string message)
        {
            StatusText.Text = $"{DateTime.Now:HH:mm:ss} - {message}";
        }

      // private void EditSqlScriptButton_Click(object sender, RoutedEventArgs e)
      // {
      //     var button = sender as Button;
      //     var script = button?.Tag as ScriptConfiguration;
      //
      //     if (script != null)
      //     {
      //         var editor = new SqlQueryEditorWindow(script, _connectionString);
      //         editor.ShowDialog();
      //     }
      // }
    }
}
