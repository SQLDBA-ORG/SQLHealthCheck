using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using SqlCheckLibrary.Services;

namespace SqlMonitorUI
{
    public partial class ConnectionDialog : Window
    {
        /// <summary>
        /// List of connection strings (one per server)
        /// </summary>
        public List<string> ConnectionStrings { get; private set; } = new List<string>();
        
        /// <summary>
        /// List of server names entered
        /// </summary>
        public List<string> ServerNames { get; private set; } = new List<string>();
        
        /// <summary>
        /// Whether to run checks in parallel
        /// </summary>
        public bool RunInParallel { get; private set; } = true;
        
        public bool UseIntegratedSecurity { get; private set; } = true;

        // Legacy property for backwards compatibility (returns first connection string)
        public string ConnectionString => ConnectionStrings.FirstOrDefault() ?? string.Empty;

        public ConnectionDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialize with existing server list (newline separated)
        /// </summary>
        public ConnectionDialog(string existingServers) : this()
        {
            if (!string.IsNullOrWhiteSpace(existingServers))
            {
                // Check if it's a connection string or server list
                if (existingServers.Contains("="))
                {
                    // It's a connection string, parse it
                    try
                    {
                        var info = ConnectionStringBuilder.ParseConnectionString(existingServers);
                        if (info.IsValid)
                        {
                            ServerTextBox.Text = info.Server;
                            DatabaseTextBox.Text = info.Database ?? "master";
                            WindowsAuthRadio.IsChecked = info.UseIntegratedSecurity;
                            SqlAuthRadio.IsChecked = !info.UseIntegratedSecurity;
                            UsernameTextBox.Text = info.Username ?? "";
                            EncryptCheckBox.IsChecked = info.Encrypt;
                            TrustCertCheckBox.IsChecked = info.TrustServerCertificate;
                            UpdateAuthPanelVisibility();
                        }
                    }
                    catch { }
                }
                else
                {
                    // It's a server list
                    ServerTextBox.Text = existingServers;
                }
            }
        }

        private void AuthType_Changed(object sender, RoutedEventArgs e)
        {
            UpdateAuthPanelVisibility();
        }

        private void UpdateAuthPanelVisibility()
        {
            if (SqlAuthPanel != null)
            {
                SqlAuthPanel.Visibility = SqlAuthRadio.IsChecked == true
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private List<string> GetServerList()
        {
            return ServerTextBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();
        }

        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput())
                return;

            var servers = GetServerList();
            var results = new StringBuilder();
            int successCount = 0;
            int failCount = 0;

            foreach (var server in servers)
            {
                try
                {
                    var connectionString = BuildConnectionString(server);
                    using var runner = new CheckRunner(connectionString);
                    var testResult = await runner.TestConnectionAsync();

                    if (testResult)
                    {
                        var serverName = await runner.GetServerNameAsync();
                        results.AppendLine($"✓ {server} → {serverName}");
                        successCount++;
                    }
                    else
                    {
                        results.AppendLine($"✗ {server} → Connection failed");
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    results.AppendLine($"✗ {server} → {ex.Message}");
                    failCount++;
                }
            }

            var summary = $"Test Results: {successCount} succeeded, {failCount} failed\n\n{results}";
            var icon = failCount == 0 ? MessageBoxImage.Information : 
                       (successCount == 0 ? MessageBoxImage.Error : MessageBoxImage.Warning);
            
            MessageBox.Show(summary, "Connection Test", MessageBoxButton.OK, icon);
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput())
                return;

            try
            {
                ServerNames = GetServerList();
                ConnectionStrings = ServerNames.Select(s => BuildConnectionString(s)).ToList();
                UseIntegratedSecurity = WindowsAuthRadio.IsChecked == true;
                RunInParallel = ParallelExecutionCheckBox.IsChecked == true;

                // Validate security on first connection string
                if (ConnectionStrings.Any())
                {
                    var securityValidation = ConnectionStringBuilder.ValidateSecurity(ConnectionStrings.First());
                    if (!securityValidation.IsSecure && securityValidation.Warnings.Count > 0)
                    {
                        var warningMessage = string.Join("\n• ", securityValidation.Warnings);
                        var result = MessageBox.Show(
                            $"Security Warnings:\n• {warningMessage}\n\nContinue anyway?",
                            "Security Warning",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (result != MessageBoxResult.Yes)
                            return;
                    }
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error building connection string: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool ValidateInput()
        {
            var servers = GetServerList();
            
            if (servers.Count == 0)
            {
                MessageBox.Show("Please enter at least one server name.",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                ServerTextBox.Focus();
                return false;
            }

            if (SqlAuthRadio.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
                {
                    MessageBox.Show("Please enter a username for SQL Server Authentication.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    UsernameTextBox.Focus();
                    return false;
                }

                if (string.IsNullOrWhiteSpace(PasswordBox.Password))
                {
                    MessageBox.Show("Please enter a password for SQL Server Authentication.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    PasswordBox.Focus();
                    return false;
                }
            }

            return true;
        }

        private string BuildConnectionString(string server)
        {
            var database = string.IsNullOrWhiteSpace(DatabaseTextBox.Text) ? null : DatabaseTextBox.Text.Trim();
            var encrypt = EncryptCheckBox.IsChecked == true;
            var trustCert = TrustCertCheckBox.IsChecked == true;

            if (WindowsAuthRadio.IsChecked == true)
            {
                return ConnectionStringBuilder.BuildWithIntegratedSecurity(
                    server, database, encrypt, trustCert);
            }
            else
            {
                return ConnectionStringBuilder.BuildWithSqlAuth(
                    server,
                    UsernameTextBox.Text.Trim(),
                    PasswordBox.Password,
                    database,
                    encrypt,
                    trustCert);
            }
        }
    }
}
