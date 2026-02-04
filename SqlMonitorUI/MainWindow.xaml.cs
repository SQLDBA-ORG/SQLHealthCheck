//* بسم الله الرحمن الرحيم  */
//* In the name of God, the Merciful, the Compassionate */
//
//Welcome to sp_triage®
//This script will grab a bunch of metrics from the SQL server, looping all instances on this machine and generate some outputs.

using Azure;

using Microsoft.Data.SqlClient;


//using Microsoft.WindowsAzure.Storage;
//using Microsoft.WindowsAzure.Storage.Auth;
//using Microsoft.WindowsAzure.Storage.Blob;
using SqlCheckLibrary.Models;
using SqlCheckLibrary.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

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
        private string currentOutputFolder;
        //private List<(string ConnectionString, string ServerName, bool IsOnline) FinalserverResults //= new List<(string ConnectionString, string ServerName, bool IsOnline)>();

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
                StartLiveMonitorButton.IsEnabled = false;
            }
            else if (_serverNames.Count == 1)
            {
                ConnectionStringTextBox.Text = _serverNames[0];
                StartLiveMonitorButton.IsEnabled = true;
            }
            else
            {
                ConnectionStringTextBox.Text = $"{_serverNames.Count} servers: {string.Join(", ", _serverNames.Take(3))}{(_serverNames.Count > 3 ? "..." : "")}";
                StartLiveMonitorButton.IsEnabled = true;
            }
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Optional: Handle tab changes if needed
            if (e.Source != MainTabControl) return;

            if (MainTabControl.SelectedItem == LiveMonitorTab)
            {
                // Update the Live Monitor tab state based on connection
                if (_connectionStrings == null || !_connectionStrings.Any())
                {
                    StartLiveMonitorButton.IsEnabled = false;
                }
                else
                {
                    StartLiveMonitorButton.IsEnabled = true;
                }
            }
        }

        private void StartLiveMonitorButton_Click(object sender, RoutedEventArgs e)
        {
            if (_connectionStrings == null || !_connectionStrings.Any())
            {
                MessageBox.Show("Please configure a server connection first using the Connect button.",
                    "Connection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Open the Live Monitor in a new window (since embedding is complex)
            var monitor = new LiveMonitoringWindow(_connectionStrings);
            monitor.Show();
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
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Default, true, true);
            GC.WaitForPendingFinalizers();
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
                LastUpdateText.Text = $" - Last updated: {DateTime.Now:g} ({serverCount} server{(serverCount > 1 ? "s" : "")})";

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
            if (_connectionStrings == null || !_connectionStrings.Any())
            {
                MessageBox.Show("Please configure a server connection first using the Connect button.",
                    "Connection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var monitor = new LiveMonitoringWindow(_connectionStrings);
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
            if (_connectionStrings == null || !_connectionStrings.Any())
            {
                MessageBox.Show("Please configure server connections first using the Connect button.",
                    "Connection Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Load script configurations (use first connection string just to load configs)
            var firstConnStr = _connectionStrings.First();
            using var scriptRunner = new CompleteHealthCheckRunner(firstConnStr);
            var scripts = await scriptRunner.LoadScriptConfigurationsAsync();

            if (scripts.Count == 0)
            {
                MessageBox.Show("No scripts configured. Please configure scripts in Script Manager first.\n\n" +
                               "1. Click 'Scripts' button\n" +
                               "2. Click 'Scan Scripts Folder'\n" +
                               "3. Save configuration",
                    "No Scripts", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var enabledScripts = scripts.Where(s => s.Enabled).ToList();
            if (enabledScripts.Count == 0)
            {
                MessageBox.Show("No enabled scripts found. Please enable at least one script in Script Manager.",
                    "No Enabled Scripts", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Test all server connections first
            var serverResults = new List<(string ConnectionString, string ServerName, bool IsOnline, bool IsSYSADMIN, string LoginName)>();
            foreach (var connStr in _connectionStrings)
            {
                string serverName;
                try { serverName = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr).DataSource; }
                catch { serverName = "Unknown"; }
                
                var isOnline = await TestServerConnectionAsync(connStr);
                //Check sysadmin before moving on




                //string q = "SET NOCOUNT ON;" +
                //"SELECT " +
                //"[Build]" +
                //", [OriginalLogin] " +
                //",[UserName]" +
                //", [AmIsysadmin];";

                if (isOnline) {
                    var admincheck = TestServerConnectionSYSADMINAsync(connStr);

                    var LoginName = "";
                    var UserName = "";
                    var IsUserSysadmin = 0;

                    foreach (DataRow row in admincheck.Rows)
                    {

                        LoginName = (string)row["OriginalLogin"];
                        UserName = (string)row["UserName"];
                        IsUserSysadmin = Convert.ToInt32(row["AmIsysadmin"]);
                    }
                        

                        var IsSYSADMIN = false;
                    if(IsUserSysadmin == 1)
                        {
                            IsSYSADMIN = true;
                        }
                        else
                        {
                            IsSYSADMIN = false;
                    }
                    TestServerConnectionSYSADMINAsync(connStr);
                    serverResults.Add((connStr, serverName, isOnline, IsSYSADMIN, UserName));
                }
                
            }

            var onlineServers = serverResults.Where(s => s.IsOnline).ToList();
            var offlineServers = serverResults.Where(s => !s.IsOnline).ToList();

            if (onlineServers.Count == 0)
            {
                MessageBox.Show("No servers are currently online. Please check your connections.",
                    "No Servers Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var serverStatusMessage = $"Server Status:\n";
            foreach (var srv in serverResults)
            {
                serverStatusMessage += $"  {(srv.IsOnline ? "✅" : "❌")} {srv.ServerName}, {srv.LoginName} sysadmin? {(srv.IsSYSADMIN ? "👍" : "❌")} \n";
            }

            var result = MessageBox.Show(
                $"This will execute {enabledScripts.Count} diagnostic script(s) on {onlineServers.Count} server(s):\n\n" +
                serverStatusMessage + "\n" +
                "Scripts to run:\n" +
                string.Join("\n", enabledScripts.Select(s => $"• {s.Name}")) +
                "\n\nResults will be exported to CSV in the output folder." +
                "\nThe user context ideally should be SYSADMIN, or things might fail."+
                "\nObjects will be created in the MASTER database on each server.\n\nContinue?",
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
                    "output",
                    $"Audit_{DateTime.Now:yyyyMMdd_HHmmss}");
                currentOutputFolder = outputFolder;
                Directory.CreateDirectory(outputFolder);

                int serverIndex = 0;
                foreach (var server in onlineServers)
                {
                    serverIndex++;
                    var serverOutputFolder = Path.Combine(outputFolder, SanitizeFileName(server.ServerName));
                    Directory.CreateDirectory(serverOutputFolder);

                    progress.UpdateProgress($"[{serverIndex}/{onlineServers.Count}] Running on {server.ServerName}...", 
                        (int)((double)serverIndex / onlineServers.Count * 100));

                    using var runner = new CompleteHealthCheckRunner(server.ConnectionString);
                    await runner.RunCompleteHealthCheckAsync(
                        scripts,
                        serverOutputFolder,
                        (msg, pct) => progress.UpdateProgress($"[{server.ServerName}] {msg}", pct));
                }

                progress.Close();

                var summaryMessage = $"Complete health check finished!\n\n" +
                    $"Servers processed: {onlineServers.Count}\n" +
                    (offlineServers.Any() ? $"Servers skipped (offline): {offlineServers.Count}\n" : "") +
                    $"\nResults exported to:\n{outputFolder}";

                MessageBox.Show(summaryMessage,
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
                UploadtoAzure.IsEnabled = true;
                UploadtoAzure.Foreground = Brushes.White;
       
            }
        }

        private async System.Threading.Tasks.Task<bool> TestServerConnectionAsync(string connectionString)
        {
            return await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString)
                    {
                        ConnectTimeout = 5
                    };
                    using var conn = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
                    conn.Open();
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }
        private  DataTable TestServerConnectionSYSADMINAsync(string connectionString)
        {

                    //public async Task<string> GetServerVersionAsync()
        //{
            using var connection = new SqlConnection(connectionString);
            //await connection.OpenAsync();
            using var command = new SqlCommand("SET NOCOUNT ON;" +
                "SELECT " +
                //"SERVERPROPERTY ('productversion') [Build]" +
                //", SYSTEM_USER [CurrentLogin], ORIGINAL_LOGIN() [OriginalLogin] " +
                //",SUSER_SNAME() [UserName]" +
                " CASE WHEN (SELECT COUNT(1) FROM sys.server_role_members rm ,sys.server_principals sp WHERE rm.role_principal_id = SUSER_ID('Sysadmin') AND rm.member_principal_id = sp.principal_id AND name = SUSER_SNAME()) > 0 THEN 1 ELSE 0 END [AmIsysadmin];", connection);


            string q = "SET NOCOUNT ON;" +
                "SELECT " +
                "SERVERPROPERTY ('productversion') [Build]" +
                ", SYSTEM_USER [CurrentLogin], ORIGINAL_LOGIN() [OriginalLogin] " +
                ",SUSER_SNAME() [UserName]" +
                " ,CASE WHEN (SELECT COUNT(1) FROM sys.server_role_members rm ,sys.server_principals sp WHERE rm.role_principal_id = SUSER_ID('Sysadmin') AND rm.member_principal_id = sp.principal_id AND name = SUSER_SNAME()) > 0 THEN 1 ELSE 0 END [AmIsysadmin];";
              using var cmd = new SqlCommand(q, connection) { CommandTimeout = 30 };
              var dt = new DataTable();
              try { new SqlDataAdapter(cmd).Fill(dt); } catch { /* Ignore empty sessions from filters */ }


             return dt;




            //try
            //{
            //    var result = await command.ExecuteScalarAsync();
            //    if (result.ToString() == "1")
            //{
            //    return true;
            //}
            //else
            //    return false;
            //}
            //catch
            //{
            //    return false;
            //}
        }


        private string SanitizeFileName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
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

    private void RunUploadtoAzureButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show($"This will upload data to SQLDBA's Azure Blob store. It's secure as, but you need to be aware that you are about to share data outside of your business.", "Data sharing",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            
            var result = MessageBox.Show(
            $"This will upload a CSV file from every server that was audited to Azure Blob store.\n\n" +
            "Are you sure you'd like to continue?\n" +
            "\n" +
            "\n\nMore information on how we handle data is available on our webiste.\n\nwww.sqldba.org/nda \n"+
            "Alternatively send the CSV output in password protected ZIP files to us at\nscriptoutput@sqldba.org \n\nContinue?",
            "Confirm Complete Health Check Upload to Azure",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;
            for (int i = 0; i < _connectionStrings.Count; i++)
            {
                
                var servertoupload = _serverNames[i];



                var psCommmand = ".\\azcopy.exe copy \"" + currentOutputFolder + "\\"+ servertoupload + "\\*.sp_triage_*.csv\" \"https://sqldbaorgstorage.blob.core.windows.net/raw/ready?sp=acw&st=2023-04-05T21:18:07Z&se=2033-06-04T05:18:07Z&spr=https&sv=2021-12-02&sr=d&sig=zCRXJULTwR6aTB5%2FBvt0T7dX98avVCafRtLOJzCT0y0%3D&sdd=1\" --recursive=true --check-length=false";
                var psCommandBytes = System.Text.Encoding.Unicode.GetBytes(psCommmand);
                var psCommandBase64 = Convert.ToBase64String(psCommandBytes);

                //string createText = psCommmand;
                //File.WriteAllText(currentOutputFolder + "\\azupload.ps1", createText);

                var startInfo = new ProcessStartInfo()
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy unrestricted -encodedCommand " + psCommandBase64 + "",
                    UseShellExecute = true
                };

                try
                {
                    Process.Start(startInfo);
                    // LogMessage(string.Format("Upload to Azure completed."));
                }
                catch (Exception ex)
                {
                    // LogMessage(string.Format("Error with azcopy: {0}", ex.Message));
                }
            }
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var about= new About();
            about.Show();
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
        public string CheckId { get; set; }
        public string CheckName { get; set; }
        public string Category { get; set; }
        public string Severity { get; set; }
        public bool Passed { get; set; }
        public string Message { get; set; }
        public DateTime ExecutedAt { get; set; }
        public string Source { get; set; }
        public string ServerName { get; set; }
        public string Status => Passed ? "Passed" : "Triggered";
        public string ExecutedAtFormatted => ExecutedAt.ToString("g");

        public SolidColorBrush StatusColor => Passed
            ? new SolidColorBrush(Color.FromRgb(16, 124, 16))    // Green
            : Severity switch
            {
                "Critical" => new SolidColorBrush(Color.FromRgb(216, 59, 1)),   // Red
                "Warning" => new SolidColorBrush(Color.FromRgb(255, 165, 0)),   // Orange
                _ => new SolidColorBrush(Color.FromRgb(0, 120, 212))            // Blue
            };

        public SolidColorBrush SeverityBackground => Severity switch
        {
            "Critical" => new SolidColorBrush(Color.FromRgb(216, 59, 1)),
            "Warning" => new SolidColorBrush(Color.FromRgb(255, 165, 0)),
            "Info" => new SolidColorBrush(Color.FromRgb(0, 120, 212)),
            _ => new SolidColorBrush(Color.FromRgb(128, 128, 128))
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