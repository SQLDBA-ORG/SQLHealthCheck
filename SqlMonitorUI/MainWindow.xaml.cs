using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using SqlCheckLibrary.Models;
using SqlCheckLibrary.Services;
using System.Threading.Tasks;

namespace SqlMonitorUI
{
    public partial class MainWindow : Window
    {
        private CheckRepository _repository;
        private ObservableCollection<CheckResultViewModel> _allResults;
        private ObservableCollection<CategoryViewModel> _categories;
        private ObservableCollection<ServerViewModel> _servers;
        private ICollectionView _resultsView;
        private DispatcherTimer? _memoryMonitorTimer;
        
        // Multi-server support
        private List<string> _serverNames = new List<string>();
        private List<string> _connectionStrings = new List<string>();
        private bool _runInParallel = true;
        
        // Configuration
        private AppConfig _config;
        
        // Status filter (null = all, "Passed", "Critical", "Warning", "Info")
        private string? _statusFilter = null;

        public MainWindow()
        {
            InitializeComponent();
            _repository = new CheckRepository();
            _allResults = new ObservableCollection<CheckResultViewModel>();
            _categories = new ObservableCollection<CategoryViewModel>();
            _servers = new ObservableCollection<ServerViewModel>();
            
            // Initialize config
            _config = AppConfig.Instance;
            _config.ConfigChanged += Config_ConfigChanged;
            
            CategoryListBox.ItemsSource = _categories;
            ServerListBox.ItemsSource = _servers;
            ResultsDataGrid.ItemsSource = _allResults;
            
            _resultsView = CollectionViewSource.GetDefaultView(_allResults);
            _resultsView.Filter = FilterResults;

            Loaded += MainWindow_Loaded;

            // Start memory monitoring
            StartMemoryMonitoring();
        }

        private void Config_ConfigChanged(object? sender, ConfigChangedEventArgs e)
        {
            // Handle config changes from file (external edits)
            if (e.Source == ConfigChangeSource.File)
            {
                // Update on UI thread
                Dispatcher.Invoke(() =>
                {
                    LoadServersFromConfig();
                });
            }
        }

        private void LoadServersFromConfig()
        {
            _serverNames = _config.Servers.ToList();
            _runInParallel = _config.RunInParallel;
            
            // Build connection strings
            _connectionStrings = _serverNames.Select(server =>
            {
                if (_config.UseWindowsAuth)
                {
                    return ConnectionStringBuilder.BuildWithIntegratedSecurity(
                        server, 
                        _config.DefaultDatabase,
                        true,  // encrypt
                        true); // trust cert
                }
                else
                {
                    // For SQL auth, we can't store password, so just use server name
                    return ConnectionStringBuilder.BuildWithIntegratedSecurity(
                        server,
                        _config.DefaultDatabase,
                        true,
                        true);
                }
            }).ToList();
            
            // Update display
            UpdateServerDisplayText();
        }

        private void UpdateServerDisplayText()
        {
            if (_serverNames.Count == 0)
            {
                ConnectionStringTextBox.Text = "";
            }
            else if (_serverNames.Count == 1)
            {
                ConnectionStringTextBox.Text = _serverNames[0];
            }
            else
            {
                ConnectionStringTextBox.Text = $"{_serverNames.Count} servers: {string.Join(", ", _serverNames.Take(3))}{(_serverNames.Count > 3 ? "..." : "")}";
            }
        }

        private void StartMemoryMonitoring()
        {
            _memoryMonitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };

            _memoryMonitorTimer.Tick += (s, e) =>
            {
                var pressure = ResourceManager.CheckMemoryPressure();

                if (pressure == MemoryPressure.High)
                {
                    ResourceManager.SuggestCleanup();
                }
            };

            _memoryMonitorTimer.Start();
        }

      

        protected override void OnClosed(EventArgs e)
        {
            _memoryMonitorTimer?.Stop();

            // Unsubscribe from config changes
            if (_config != null)
            {
                _config.ConfigChanged -= Config_ConfigChanged;
            }

            // Clear all connection pools to release resources
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            // Clean up resources
            _allResults.Clear();
            _categories.Clear();
            _servers.Clear();
            _serverNames.Clear();
            _connectionStrings.Clear();

            // Force garbage collection
           // SqlConnection.ClearAllPools();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();

            base.OnClosed(e);
        }


        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await _repository.LoadChecksAsync();
                
                // Load servers from config
                LoadServersFromConfig();
                
                EmptyStatePanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading checks: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            // Pass existing server list (newline separated) or connection string
            var existingServers = _serverNames.Count > 0 
                ? string.Join(Environment.NewLine, _serverNames) 
                : ConnectionStringTextBox.Text;
                
            var connectionDialog = new ConnectionDialog(existingServers);
            if (connectionDialog.ShowDialog() == true)
            {
                _serverNames = connectionDialog.ServerNames;
                _connectionStrings = connectionDialog.ConnectionStrings;
                _runInParallel = connectionDialog.RunInParallel;
                
                // Save to config
                _config.Servers = _serverNames;
                _config.RunInParallel = _runInParallel;
                
                // Update display text
                UpdateServerDisplayText();
            }
        }

        private async void RunChecksButton_Click(object sender, RoutedEventArgs e)
        {
            // If no servers configured via dialog, try to parse the text box
            if (_connectionStrings.Count == 0)
            {
                var text = ConnectionStringTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    MessageBox.Show("Please configure server connection(s) using the Connect button.", 
                        "Connection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Single server from text box (legacy mode)
                _serverNames = new List<string> { text.Contains("=") ? "Server" : text };
                _connectionStrings = new List<string> 
                { 
                    text.Contains("=") ? text : ConnectionStringBuilder.BuildWithIntegratedSecurity(text) 
                };
            }

            // Show loading state
            LoadingPanel.Visibility = Visibility.Visible;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            ResultsDataGrid.Visibility = Visibility.Collapsed;
            RunChecksButton.IsEnabled = false;
            RefreshButton.IsEnabled = false;

            try
            {
                var checks = _repository.GetEnabledChecks();
                var allResults = new List<CheckResultViewModel>();

                if (_runInParallel && _connectionStrings.Count > 1)
                {
                    // Parallel execution
                    allResults = await RunChecksParallelAsync(checks);
                }
                else
                {
                    // Sequential execution
                    allResults = await RunChecksSequentialAsync(checks);
                }

                // Update UI
                _allResults.Clear();
                foreach (var result in allResults)
                {
                    _allResults.Add(result);
                }

                UpdateStatistics();
                UpdateCategories();
                UpdateServers();
                
                // Clear any existing status filter
                _statusFilter = null;
                ClearCardHighlights();
                
                var serverCount = _serverNames.Count;
                LastUpdateText.Text = $"Last updated: {DateTime.Now:g} ({serverCount} server{(serverCount > 1 ? "s" : "")})";
                
                ResultsDataGrid.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error running checks: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                EmptyStatePanel.Visibility = Visibility.Visible;
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                RunChecksButton.IsEnabled = true;
                RefreshButton.IsEnabled = true;
            }
        }

        private async Task<List<CheckResultViewModel>> RunChecksParallelAsync(List<SqlCheck> checks)
        {
            var allResults = new List<CheckResultViewModel>();
            var lockObj = new object();

            var tasks = _connectionStrings.Select(async (connStr, index) =>
            {
                var serverName = _serverNames[index];
                try
                {
                    using var runner = new CheckRunner(connStr);
                    
                    if (!await runner.TestConnectionAsync())
                    {
                        lock (lockObj)
                        {
                            allResults.Add(new CheckResultViewModel(
                                new CheckResult
                                {
                                    CheckId = "CONNECTION",
                                    CheckName = "Connection Test",
                                    Category = "Connection",
                                    Severity = "Critical",
                                    Passed = false,
                                    Message = "Could not connect to server",
                                    ExecutedAt = DateTime.Now
                                },
                                "System",
                                serverName));
                        }
                        return;
                    }

                    var results = await runner.RunChecksAsync(checks);

                    lock (lockObj)
                    {
                        foreach (var result in results)
                        {
                            var check = checks.FirstOrDefault(c => c.Id == result.CheckId);
                            var source = check?.Source ?? "Custom";
                            allResults.Add(new CheckResultViewModel(result, source, serverName));
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        allResults.Add(new CheckResultViewModel(
                            new CheckResult
                            {
                                CheckId = "ERROR",
                                CheckName = "Execution Error",
                                Category = "Error",
                                Severity = "Critical",
                                Passed = false,
                                Message = ex.Message,
                                ExecutedAt = DateTime.Now
                            },
                            "System",
                            serverName));
                    }
                }
            });

            await Task.WhenAll(tasks);
            return allResults.OrderBy(r => r.ServerName).ThenBy(r => r.Category).ThenBy(r => r.CheckName).ToList();
        }

        private async Task<List<CheckResultViewModel>> RunChecksSequentialAsync(List<SqlCheck> checks)
        {
            var allResults = new List<CheckResultViewModel>();

            for (int i = 0; i < _connectionStrings.Count; i++)
            {
                var connStr = _connectionStrings[i];
                var serverName = _serverNames[i];

                try
                {
                    using var runner = new CheckRunner(connStr);

                    if (!await runner.TestConnectionAsync())
                    {
                        allResults.Add(new CheckResultViewModel(
                            new CheckResult
                            {
                                CheckId = "CONNECTION",
                                CheckName = "Connection Test",
                                Category = "Connection",
                                Severity = "Critical",
                                Passed = false,
                                Message = "Could not connect to server",
                                ExecutedAt = DateTime.Now
                            },
                            "System",
                            serverName));
                        continue;
                    }

                    var results = await runner.RunChecksAsync(checks);

                    foreach (var result in results)
                    {
                        var check = checks.FirstOrDefault(c => c.Id == result.CheckId);
                        var source = check?.Source ?? "Custom";
                        allResults.Add(new CheckResultViewModel(result, source, serverName));
                    }
                }
                catch (Exception ex)
                {
                    allResults.Add(new CheckResultViewModel(
                        new CheckResult
                        {
                            CheckId = "ERROR",
                            CheckName = "Execution Error",
                            Category = "Error",
                            Severity = "Critical",
                            Passed = false,
                            Message = ex.Message,
                            ExecutedAt = DateTime.Now
                        },
                        "System",
                        serverName));
                }
            }

            return allResults.OrderBy(r => r.ServerName).ThenBy(r => r.Category).ThenBy(r => r.CheckName).ToList();
        }

        private void ManageChecksButton_Click(object sender, RoutedEventArgs e)
        {
            // Get a valid connection string - prefer from configured list
            var connectionString = _connectionStrings.FirstOrDefault();
            
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                // Try to build one from the text box if it looks like a server name
                var text = ConnectionStringTextBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(text) && !text.Contains("servers:") && !text.Contains("="))
                {
                    // Looks like a simple server name
                    connectionString = ConnectionStringBuilder.BuildWithIntegratedSecurity(text);
                }
                else if (text.Contains("="))
                {
                    // Looks like a connection string
                    connectionString = text;
                }
            }
            
            var manager = new CheckManagerWindow(_repository, connectionString ?? string.Empty);
            manager.ShowDialog();
            
            // Reload repository after manager closes
            _ = _repository.LoadChecksAsync();
        }

        private void LiveMonitorButton_Click(object sender, RoutedEventArgs e)
        {
            var connectionString = _connectionStrings.FirstOrDefault();
            
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                MessageBox.Show("Please configure a server connection first using the Connect button.", 
                    "Connection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var monitor = new LiveMonitoringWindow(connectionString);
            monitor.Show();
        }

        private void ScriptManagerButton_Click(object sender, RoutedEventArgs e)
        {
            var connectionString = _connectionStrings.FirstOrDefault();
            
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                MessageBox.Show("Please configure a server connection first using the Connect button.", 
                    "Connection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var scriptManager = new ScriptManagerWindow(connectionString);
            scriptManager.ShowDialog();
        }

        private async void RunCompleteHealthCheckButton_Click(object sender, RoutedEventArgs e)
        {
            if (_connectionStrings.Count == 0 || _serverNames.Count == 0)
            {
                MessageBox.Show("Please configure a server connection first using the Connect button.",
                    "Connection Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Load script configurations using first connection
            using var tempRunner = new CompleteHealthCheckRunner(_connectionStrings.First());
            var scripts = await tempRunner.LoadScriptConfigurationsAsync();

            if (scripts.Count == 0)
            {
                MessageBox.Show("No scripts configured. Please configure scripts in Script Manager first.\n\n" +
                               "1. Click 'Scripts' button\n" +
                               "2. Click 'Scan Scripts Folder'\n" +
                               "3. Save configuration",
                    "No Scripts", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var enabledScripts = scripts.Where(s => s.Enabled).OrderBy(s => s.ExecutionOrder).ToList();
            if (enabledScripts.Count == 0)
            {
                MessageBox.Show("No enabled scripts found. Please enable at least one script in Script Manager.",
                    "No Enabled Scripts", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var serverCount = _serverNames.Count;
            var executionMode = _runInParallel ? "parallel" : "sequential";
            
            var result = MessageBox.Show(
                $"This will execute {enabledScripts.Count} diagnostic script(s) on {serverCount} server(s) in {executionMode} mode:\n\n" +
                $"Servers:\n{string.Join("\n", _serverNames.Select(s => $"• {s}"))}\n\n" +
                $"Scripts:\n{string.Join("\n", enabledScripts.Select(s => $"• {s.Name}"))}\n\n" +
                "Results will be exported to CSV in the output folder.\n\nContinue?",
                "Confirm Complete Health Check",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            RunCompleteHealthCheckButton.IsEnabled = false;

            try
            {
                var progress = new ProgressWindow();
                progress.Owner = this;
                progress.Show();

                var outputFolder = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "output");

                Directory.CreateDirectory(outputFolder);

                var totalOperations = serverCount * enabledScripts.Count;
                var completedOperations = 0;

                if (_runInParallel)
                {
                    // Parallel execution: run all servers simultaneously, scripts in order per server
                    var tasks = _connectionStrings.Select(async (connStr, index) =>
                    {
                        var serverName = _serverNames[index];
                        using var runner = new CompleteHealthCheckRunner(connStr);
                        
                        foreach (var script in enabledScripts)
                        {
                            try
                            {
                                await runner.RunSingleScriptAsync(script, outputFolder, serverName);
                            }
                            catch (Exception ex)
                            {
                                // Log error but continue with other scripts
                                System.Diagnostics.Debug.WriteLine($"Error running {script.Name} on {serverName}: {ex.Message}");
                            }
                            
                            completedOperations++;
                            var pct = (int)((double)completedOperations / totalOperations * 100);
                            Dispatcher.Invoke(() => progress.UpdateProgress($"Running scripts... ({completedOperations}/{totalOperations})", pct));
                        }
                    });

                    await Task.WhenAll(tasks);
                }
                else
                {
                    // Sequential execution: one server at a time, scripts in order
                    for (int i = 0; i < _connectionStrings.Count; i++)
                    {
                        var connStr = _connectionStrings[i];
                        var serverName = _serverNames[i];
                        
                        using var runner = new CompleteHealthCheckRunner(connStr);
                        
                        foreach (var script in enabledScripts)
                        {
                            try
                            {
                                progress.UpdateProgress($"Running {script.Name} on {serverName}...", 
                                    (int)((double)completedOperations / totalOperations * 100));
                                
                                await runner.RunSingleScriptAsync(script, outputFolder, serverName);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error running {script.Name} on {serverName}: {ex.Message}");
                            }
                            
                            completedOperations++;
                        }
                    }
                }

                progress.Close();

                MessageBox.Show(
                    $"Complete health check finished!\n\n" +
                    $"Checked {serverCount} server(s) with {enabledScripts.Count} script(s).\n\n" +
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

        private async void ImportBlitzButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open file dialog to select sp_Blitz.sql or sp_triage.sql
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select sp_Blitz.sql or sp_triage.sql file",
                    Filter = "SQL Files (*.sql)|*.sql|All Files (*.*)|*.*",
                    DefaultExt = ".sql"
                };

                if (dialog.ShowDialog() != true)
                    return;

                ImportBlitzButton.IsEnabled = false;
                ImportBlitzButton.Content = "⏳ Importing...";

                var parser = new SpBlitzParser();
                var fileName = System.IO.Path.GetFileName(dialog.FileName).ToLower();
                
                List<SpBlitzParser.BlitzCheck> checksToImport;
                string source;

                // Determine which script this is
                if (fileName.Contains("sp_blitz"))
                {
                    source = "sp_Blitz";
                    checksToImport = await parser.ParseSpBlitzFile(dialog.FileName);
                }
                else if (fileName.Contains("sp_triage") || fileName.Contains("sqldba"))
                {
                    source = "sp_triage";
                    checksToImport = await parser.ParseSpTriageFile(dialog.FileName);
                }
                else
                {
                    // Ask user which one it is
                    var askResult = MessageBox.Show(
                        "Could not determine script type from filename.\n\n" +
                        "Click YES if this is sp_Blitz.sql\n" +
                        "Click NO if this is sp_triage.sql",
                        "Which Script?",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (askResult == MessageBoxResult.Cancel)
                    {
                        return;
                    }

                    source = askResult == MessageBoxResult.Yes ? "sp_Blitz" : "sp_triage";
                    checksToImport = askResult == MessageBoxResult.Yes ?
                        await parser.ParseSpBlitzFile(dialog.FileName) :
                        await parser.ParseSpTriageFile(dialog.FileName);
                }
                
                if (checksToImport.Count == 0)
                {
                    MessageBox.Show($"No checks found in the {source} file. Please verify the file format.", 
                        "Import Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Convert to SqlCheck format
                var newChecks = parser.ConvertToSqlChecks(checksToImport, source);
                
                // Merge with existing checks
                var existingChecks = _repository.GetAllChecks();
                var mergedChecks = parser.MergeChecks(existingChecks, newChecks);

                // Count implemented vs placeholder
                var implemented = newChecks.Count(c => !c.SqlQuery.Contains("not yet implemented"));
                var placeholders = newChecks.Count - implemented;

                // Show summary and ask for confirmation
                var message = $"Found {checksToImport.Count} checks in {source}\n\n" +
                             $"Source: {source}\n" +
                             $"New checks to add: {newChecks.Count(c => !existingChecks.Any(ec => ec.Id == c.Id))}\n" +
                             $"Existing checks to update: {newChecks.Count(c => existingChecks.Any(ec => ec.Id == c.Id))}\n\n" +
                             $"Working queries: {implemented}\n" +
                             $"Placeholders: {placeholders}\n\n" +
                             $"Total checks after import: {mergedChecks.Count}\n\n" +
                             $"Continue with import?";

                var confirmResult = MessageBox.Show(message, "Confirm Import", 
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirmResult != MessageBoxResult.Yes)
                    return;

                // Save merged checks
                await parser.SaveChecksToFile(mergedChecks, "sql-checks.json");
                
                // Reload repository
                await _repository.LoadChecksAsync();

                MessageBox.Show($"Successfully imported {checksToImport.Count} checks from {source}!\n\n" +
                               $"Source: {source}\n" +
                               $"Total checks: {mergedChecks.Count}\n" +
                               $"Enabled by default: {mergedChecks.Count(c => c.Enabled)}\n" +
                               $"Working queries: {implemented}\n" +
                               $"Placeholders: {placeholders}\n\n" +
                               $"The 'Source' column in results will show which script each check came from.",
                    "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing: {ex.Message}\n\n{ex.StackTrace}", 
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ImportBlitzButton.IsEnabled = true;
                ImportBlitzButton.Content = "📥 Import";
            }
        }

        private void UpdateStatistics()
        {
            // Count unique servers
            var uniqueServers = _allResults
                .Select(r => r.ServerName)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .Count();
            InstanceCountText.Text = (uniqueServers > 0 ? uniqueServers : _serverNames.Count).ToString();
            
            TotalChecksText.Text = _allResults.Count.ToString();
            PassedChecksText.Text = _allResults.Count(r => r.Passed).ToString();
            CriticalIssuesText.Text = _allResults.Count(r => !r.Passed && r.Severity == "Critical").ToString();
            WarningIssuesText.Text = _allResults.Count(r => !r.Passed && r.Severity == "Warning").ToString();
            InfoIssuesText.Text = _allResults.Count(r => !r.Passed && r.Severity == "Info").ToString();
        }

        private void UpdateCategories()
        {
            _categories.Clear();
            
            // Add "All" category
            _categories.Add(new CategoryViewModel 
            { 
                Name = "All", 
                Count = $"({_allResults.Count})",
                Color = new SolidColorBrush(Color.FromRgb(0, 120, 212))
            });

            // Add categories from results
            var categoryGroups = _allResults
                .GroupBy(r => r.Category)
                .OrderBy(g => g.Key);

            foreach (var group in categoryGroups)
            {
                var color = GetCategoryColor(group.Key);
                _categories.Add(new CategoryViewModel 
                { 
                    Name = group.Key, 
                    Count = $"({group.Count()})",
                    Color = new SolidColorBrush(color)
                });
            }

            // Select "All" by default
            CategoryListBox.SelectedIndex = 0;
        }

        private void UpdateServers()
        {
            _servers.Clear();
            
            // Get unique servers from results
            var serverGroups = _allResults
                .Where(r => !string.IsNullOrEmpty(r.ServerName))
                .GroupBy(r => r.ServerName)
                .OrderBy(g => g.Key);

            var serverCount = serverGroups.Count();
            
            // Add "All" option if there are multiple servers
            if (serverCount > 1)
            {
                _servers.Add(new ServerViewModel 
                { 
                    Name = "All", 
                    Count = $"({_allResults.Count})",
                    Color = new SolidColorBrush(Color.FromRgb(107, 105, 214)) // Purple
                });
            }

            // Add each server
            foreach (var group in serverGroups)
            {
                var failedCount = group.Count(r => !r.Passed);
                var serverColor = failedCount > 0 
                    ? Color.FromRgb(216, 59, 1)    // Red if has failures
                    : Color.FromRgb(16, 124, 16);  // Green if all passed
                    
                _servers.Add(new ServerViewModel 
                { 
                    Name = group.Key, 
                    Count = $"({group.Count()})",
                    Color = new SolidColorBrush(serverColor)
                });
            }

            // Select "All" by default if multiple servers
            if (serverCount > 1)
            {
                ServerListBox.SelectedIndex = 0;
            }
            else if (serverCount == 1)
            {
                ServerListBox.SelectedIndex = 0;
            }
        }

        private Color GetCategoryColor(string category)
        {
            return category switch
            {
                "Backup" => Color.FromRgb(16, 124, 16),      // Green
                "Security" => Color.FromRgb(216, 59, 1),      // Red
                "Performance" => Color.FromRgb(255, 165, 0),  // Orange
                "Configuration" => Color.FromRgb(0, 120, 212), // Blue
                "Integrity" => Color.FromRgb(139, 0, 139),     // Purple
                _ => Color.FromRgb(128, 128, 128)              // Gray
            };
        }

        private void CategoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _resultsView.Refresh();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _resultsView.Refresh();
        }

        private void ServerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _resultsView.Refresh();
        }

        #region Statistics Card Click Handlers

        private void InstancesCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Clear server filter
            ServerListBox.SelectedItem = null;
            _statusFilter = null;
            ClearCardHighlights();
            _resultsView.Refresh();
        }

        private void TotalCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Show all - clear status filter
            _statusFilter = null;
            ClearCardHighlights();
            TotalCard.Background = new SolidColorBrush(Color.FromRgb(227, 242, 253)); // Light blue highlight
            _resultsView.Refresh();
        }

        private void PassedCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _statusFilter = "Passed";
            ClearCardHighlights();
            PassedCard.Background = new SolidColorBrush(Color.FromRgb(200, 230, 201)); // Light green
            _resultsView.Refresh();
        }

        private void CriticalCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _statusFilter = "Critical";
            ClearCardHighlights();
            CriticalCard.Background = new SolidColorBrush(Color.FromRgb(255, 205, 210)); // Light red
            _resultsView.Refresh();
        }

        private void WarningCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _statusFilter = "Warning";
            ClearCardHighlights();
            WarningCard.Background = new SolidColorBrush(Color.FromRgb(255, 224, 178)); // Light orange
            _resultsView.Refresh();
        }

        private void InfoCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _statusFilter = "Info";
            ClearCardHighlights();
            InfoCard.Background = new SolidColorBrush(Color.FromRgb(187, 222, 251)); // Light blue
            _resultsView.Refresh();
        }

        private void ClearCardHighlights()
        {
            var defaultBackground = new SolidColorBrush(Colors.White);
            InstancesCard.Background = defaultBackground;
            TotalCard.Background = defaultBackground;
            PassedCard.Background = defaultBackground;
            CriticalCard.Background = defaultBackground;
            WarningCard.Background = defaultBackground;
            InfoCard.Background = defaultBackground;
        }

        #endregion



        private bool FilterResults(object obj)
        {
            if (obj is not CheckResultViewModel result)
                return false;

            // Filter by status (from card clicks)
            if (!string.IsNullOrEmpty(_statusFilter))
            {
                if (_statusFilter == "Passed")
                {
                    if (!result.Passed)
                        return false;
                }
                else
                {
                    // Filter by severity for failed checks
                    if (result.Passed || result.Severity != _statusFilter)
                        return false;
                }
            }

            // Filter by server
            var selectedServer = ServerListBox.SelectedItem as ServerViewModel;
            if (selectedServer != null && selectedServer.Name != "All")
            {
                if (result.ServerName != selectedServer.Name)
                    return false;
            }

            // Filter by category
            var selectedCategory = CategoryListBox.SelectedItem as CategoryViewModel;
            if (selectedCategory != null && selectedCategory.Name != "All")
            {
                if (result.Category != selectedCategory.Name)
                    return false;
            }

            // Filter by search text
            var searchText = SearchTextBox.Text?.Trim().ToLower();
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                return result.CheckName.ToLower().Contains(searchText) ||
                       result.Category.ToLower().Contains(searchText) ||
                       result.Message.ToLower().Contains(searchText) ||
                       (result.ServerName?.ToLower().Contains(searchText) ?? false);
            }

            return true;
        }
    }

    // ViewModel for server filter
    public class ServerViewModel
    {
        public string Name { get; set; } = string.Empty;
        public string Count { get; set; } = string.Empty;
        public SolidColorBrush Color { get; set; } = new SolidColorBrush(Colors.Gray);
    }

    // ViewModel for check results
    public class CheckResultViewModel
    {
        // Cached brushes (frozen for performance)
        private static readonly SolidColorBrush GreenBrush;
        private static readonly SolidColorBrush RedBrush;
        private static readonly SolidColorBrush OrangeBrush;
        private static readonly SolidColorBrush BlueBrush;
        private static readonly SolidColorBrush GrayBrush;
        
        static CheckResultViewModel()
        {
            GreenBrush = new SolidColorBrush(Color.FromRgb(16, 124, 16)); GreenBrush.Freeze();
            RedBrush = new SolidColorBrush(Color.FromRgb(216, 59, 1)); RedBrush.Freeze();
            OrangeBrush = new SolidColorBrush(Color.FromRgb(255, 165, 0)); OrangeBrush.Freeze();
            BlueBrush = new SolidColorBrush(Color.FromRgb(0, 120, 212)); BlueBrush.Freeze();
            GrayBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)); GrayBrush.Freeze();
        }

        public string CheckId { get; set; }
        public string CheckName { get; set; }
        public string Category { get; set; }
        public string Severity { get; set; }
        public bool Passed { get; set; }
        public string Message { get; set; }
        public DateTime ExecutedAt { get; set; }
        public string Source { get; set; }
        public string ServerName { get; set; }
        public string Status => Passed ? "Passed" : "Failed";
        public string ExecutedAtFormatted => ExecutedAt.ToString("g");

        public SolidColorBrush StatusColor => Passed ? GreenBrush : Severity switch
        {
            "Critical" => RedBrush,
            "Warning" => OrangeBrush,
            _ => BlueBrush
        };

        public SolidColorBrush SeverityBackground => Severity switch
        {
            "Critical" => RedBrush,
            "Warning" => OrangeBrush,
            "Info" => BlueBrush,
            _ => GrayBrush
        };

        public CheckResultViewModel(CheckResult result, string source = "Custom", string serverName = "")
        {
            CheckId = result.CheckId;
            CheckName = result.CheckName;
            Category = result.Category;
            Severity = result.Severity;
            Passed = result.Passed;
            Message = result.Passed ? "Check passed successfully" : result.Message;
            ExecutedAt = result.ExecutedAt;
            Source = source;
            ServerName = serverName;
        }
    }

    // ViewModel for categories
    public class CategoryViewModel
    {
        public string Name { get; set; } = string.Empty;
        public string Count { get; set; } = string.Empty;
        public SolidColorBrush Color { get; set; } = new SolidColorBrush(Colors.Gray);
    }
}
