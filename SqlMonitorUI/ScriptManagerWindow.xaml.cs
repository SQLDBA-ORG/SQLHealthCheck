using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SqlCheckLibrary.Models;
using SqlCheckLibrary.Services;

namespace SqlMonitorUI
{
    public partial class ScriptManagerWindow : Window
    {
        private readonly CompleteHealthCheckRunner _runner;
        private ObservableCollection<ScriptConfiguration> _scripts;
        private ObservableCollection<LiveQueryViewModel> _liveQueries;
        private const string SCRIPTS_FOLDER = "scripts";
        private string _connectionString;

        public ScriptManagerWindow(string connectionString)
        {
            InitializeComponent();
            _runner = new CompleteHealthCheckRunner(connectionString);
            _scripts = new ObservableCollection<ScriptConfiguration>();
            _liveQueries = new ObservableCollection<LiveQueryViewModel>();
            ScriptsDataGrid.ItemsSource = _scripts;
            LiveQueriesDataGrid.ItemsSource = _liveQueries;
            _connectionString = connectionString;
            Loaded += ScriptManagerWindow_Loaded;
        }

        private async void ScriptManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Load script configurations
                var scripts = await _runner.LoadScriptConfigurationsAsync();
                _scripts.Clear();
                foreach (var script in scripts)
                {
                    _scripts.Add(script);
                }

                // Load live monitoring config
                LoadLiveMonitoringConfig();

                UpdateStatus($"Loaded {_scripts.Count} script(s) and {_liveQueries.Count} monitoring queries");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading configurations: {ex.Message}",
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #region Live Monitoring Config

        private void LoadLiveMonitoringConfig()
        {
            try
            {
                var config = LiveMonitoringConfig.Instance;
                
                // Set global settings
                RefreshIntervalTextBox.Text = config.RefreshIntervalMs.ToString();
                QueryTimeoutTextBox.Text = config.QueryTimeoutSeconds.ToString();

                // Load queries into grid
                _liveQueries.Clear();
                
                _liveQueries.Add(new LiveQueryViewModel("Metrics", config.Queries.Metrics));
                _liveQueries.Add(new LiveQueryViewModel("Sessions", config.Queries.Sessions));
                _liveQueries.Add(new LiveQueryViewModel("Blocking", config.Queries.Blocking));
                _liveQueries.Add(new LiveQueryViewModel("TopQueries", config.Queries.TopQueries));
                _liveQueries.Add(new LiveQueryViewModel("DriveLatency", config.Queries.DriveLatency));
                _liveQueries.Add(new LiveQueryViewModel("ServerDetails", config.Queries.ServerDetails));
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading live config: {ex.Message}");
            }
        }

        private void SaveLiveMonitoringConfig()
        {
            try
            {
                var config = LiveMonitoringConfig.Instance;

                // Save global settings
                if (int.TryParse(RefreshIntervalTextBox.Text, out int refreshInterval))
                    config.RefreshIntervalMs = refreshInterval;
                if (int.TryParse(QueryTimeoutTextBox.Text, out int timeout))
                    config.QueryTimeoutSeconds = timeout;

                // Save query settings from grid
                foreach (var query in _liveQueries)
                {
                    switch (query.Name)
                    {
                        case "Metrics":
                            UpdateQueryConfig(config.Queries.Metrics, query);
                            break;
                        case "Sessions":
                            UpdateQueryConfig(config.Queries.Sessions, query);
                            break;
                        case "Blocking":
                            UpdateQueryConfig(config.Queries.Blocking, query);
                            break;
                        case "TopQueries":
                            UpdateQueryConfig(config.Queries.TopQueries, query);
                            break;
                        case "DriveLatency":
                            UpdateQueryConfig(config.Queries.DriveLatency, query);
                            break;
                        case "ServerDetails":
                            UpdateQueryConfig(config.Queries.ServerDetails, query);
                            break;
                    }
                }

                config.Save();
            }
            catch (Exception ex)
            {
                throw new Exception($"Error saving live monitoring config: {ex.Message}");
            }
        }

        private void UpdateQueryConfig(QueryConfig config, LiveQueryViewModel vm)
        {
            config.Enabled = vm.Enabled;
            config.Description = vm.Description;
            config.TimeoutSeconds = vm.TimeoutSeconds;
            config.RefreshEveryNTicks = vm.RefreshEveryNTicks;
            config.Sql = vm.Sql;
        }

        private void ReloadLiveConfigButton_Click(object sender, RoutedEventArgs e)
        {
            LiveMonitoringConfig.Instance.Reload();
            LoadLiveMonitoringConfig();
            UpdateStatus("Live monitoring config reloaded from file");
        }

        private void OpenLiveConfigFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LiveMonitoring.config.json");
                if (File.Exists(configPath))
                {
                    System.Diagnostics.Process.Start("notepad.exe", configPath);
                }
                else
                {
                    // Create default config
                    LiveMonitoringConfig.Instance.Save();
                    System.Diagnostics.Process.Start("notepad.exe", configPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening config file: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditLiveQueryButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var query = button?.Tag as LiveQueryViewModel;

            if (query != null)
            {
                var editor = new LiveQueryEditorWindow(query);
                if (editor.ShowDialog() == true)
                {
                    // Query was updated - refresh the grid
                    LiveQueriesDataGrid.Items.Refresh();
                    UpdateStatus($"Updated SQL for '{query.Name}'");
                }
            }
        }

        #endregion

        #region Script Management (existing code)

        private async void ScanScriptsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(SCRIPTS_FOLDER))
                {
                    Directory.CreateDirectory(SCRIPTS_FOLDER);
                    MessageBox.Show($"Created '{SCRIPTS_FOLDER}' folder.\n\nPlease copy your diagnostic scripts into this folder and scan again.",
                        "Scripts Folder Created", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var scannedScripts = await _runner.ScanScriptsFolderAsync(SCRIPTS_FOLDER);

                if (scannedScripts.Count == 0)
                {
                    MessageBox.Show($"No SQL files found in '{SCRIPTS_FOLDER}' folder.",
                        "No Scripts Found", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

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
                    MessageBox.Show($"Found {newCount} new script(s).",
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

                var destPath = Path.Combine(SCRIPTS_FOLDER, fileName);
                if (!File.Exists(destPath))
                {
                    Directory.CreateDirectory(SCRIPTS_FOLDER);
                    File.Copy(dialog.FileName, destPath);
                }

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
                var result = MessageBox.Show($"Remove '{selected.Name}' from configuration?",
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

        #endregion

        #region Save/Close

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Save script configurations
                await _runner.SaveScriptConfigurationsAsync(_scripts.ToList());

                // Save live monitoring config
                SaveLiveMonitoringConfig();

                UpdateStatus("All configurations saved");
                MessageBox.Show("All configurations saved successfully!",
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

        #endregion
    }

    #region View Models for Live Monitoring Queries

    /// <summary>
    /// ViewModel for displaying and editing live monitoring queries in the grid
    /// </summary>
    public class LiveQueryViewModel : INotifyPropertyChanged
    {
        private bool _enabled;
        private string _description = "";
        private int _timeoutSeconds;
        private int _refreshEveryNTicks;
        private string _sql = "";

        public string Name { get; }

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(nameof(Enabled)); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(nameof(Description)); }
        }

        public int TimeoutSeconds
        {
            get => _timeoutSeconds;
            set { _timeoutSeconds = value; OnPropertyChanged(nameof(TimeoutSeconds)); }
        }

        public int RefreshEveryNTicks
        {
            get => _refreshEveryNTicks;
            set { _refreshEveryNTicks = value; OnPropertyChanged(nameof(RefreshEveryNTicks)); }
        }

        public string Sql
        {
            get => _sql;
            set 
            { 
                _sql = value; 
                OnPropertyChanged(nameof(Sql)); 
                OnPropertyChanged(nameof(SqlPreview));
            }
        }

        public string SqlPreview => Sql?.Length > 100 
            ? Sql.Substring(0, 100).Replace("\n", " ").Replace("\r", "") + "..." 
            : Sql?.Replace("\n", " ").Replace("\r", "") ?? "";

        public LiveQueryViewModel(string name, QueryConfig config)
        {
            Name = name;
            _enabled = config.Enabled;
            _description = config.Description;
            _timeoutSeconds = config.TimeoutSeconds;
            _refreshEveryNTicks = config.RefreshEveryNTicks;
            _sql = config.Sql;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
