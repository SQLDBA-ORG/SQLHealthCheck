using System;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.SqlClient;
using SqlCheckLibrary.Models;
using System.Threading.Tasks;

namespace SqlMonitorUI
{
    public partial class SqlQueryEditorWindow : Window
    {
        private SqlCheck _check;
        private string _connectionString;

        public SqlQueryEditorWindow(SqlCheck check, string connectionString)
        {
            InitializeComponent();
            _check = check;
            _connectionString = connectionString;

            // Load check details
            CheckIdText.Text = check.Id;
            CheckNameText.Text = check.Name;
            SqlQueryTextBox.Text = check.SqlQuery;
            RecommendedActionTextBox.Text = check.RecommendedAction;

            // Set execution type
            ExecutionTypeCombo.SelectedIndex = check.ExecutionType == "RowCount" ? 1 : 0;
        }

        private void ViewExecutionCodeButton_Click(object sender, RoutedEventArgs e)
        {
            var executionCode = GenerateExecutionCode();
            var viewer = new CodeViewerWindow("Execution Code", executionCode);
            viewer.ShowDialog();
        }

        private string GenerateExecutionCode()
        {
            var executionType = ((ComboBoxItem)ExecutionTypeCombo.SelectedItem)?.Tag?.ToString() ?? "Binary";
            var query = SqlQueryTextBox.Text.Trim();

            return $@"-- Check: {_check.Name}
-- ID: {_check.Id}
-- Execution Type: {executionType}
-- Expected Value: {_check.ExpectedValue}

-- This is the exact code that will be executed:

{query}

-- Interpretation:
-- {(executionType == "Binary" ? "Binary mode: 0 = Pass, 1 = Fail" : "RowCount mode: 0 rows = Pass, >0 rows = Fail")}
-- Expected: {_check.ExpectedValue} (typically 0 for pass)
";
        }

        private async void TestQueryButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                MessageBox.Show("No connection string available. Please set connection in main window first.",
                    "Connection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var query = SqlQueryTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                MessageBox.Show("Please enter a query to test.", "Query Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TestQueryButton.IsEnabled = false;
            TestStatusText.Text = "Executing query...";
            TestStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0, 120, 212));

            try
            {
                var dataTable = await ExecuteQueryAsync(query);
                
                TestResultsGrid.ItemsSource = dataTable.DefaultView;
                
                var executionType = ((ComboBoxItem)ExecutionTypeCombo.SelectedItem)?.Tag?.ToString() ?? "Binary";
                
                if (executionType == "Binary")
                {
                    // Check if result is 0 or 1
                    if (dataTable.Rows.Count > 0 && dataTable.Columns.Count > 0)
                    {
                        var result = dataTable.Rows[0][0].ToString();
                        var passed = result == "0";
                        TestStatusText.Text = $"Result: {result} ({(passed ? "PASS ✓" : "FAIL ✗")}) | {dataTable.Rows.Count} row(s), {dataTable.Columns.Count} column(s)";
                        TestStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                            passed ? System.Windows.Media.Color.FromRgb(16, 124, 16) : 
                                   System.Windows.Media.Color.FromRgb(216, 59, 1));
                    }
                }
                else // RowCount
                {
                    var passed = dataTable.Rows.Count == 0;
                    TestStatusText.Text = $"Row Count: {dataTable.Rows.Count} ({(passed ? "PASS ✓" : "FAIL ✗")}) | {dataTable.Columns.Count} column(s)";
                    TestStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        passed ? System.Windows.Media.Color.FromRgb(16, 124, 16) : 
                               System.Windows.Media.Color.FromRgb(216, 59, 1));
                }
            }
            catch (Exception ex)
            {
                TestStatusText.Text = $"Error: {ex.Message}";
                TestStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(216, 59, 1));
                
                MessageBox.Show($"Query execution failed:\n\n{ex.Message}",
                    "Execution Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                TestQueryButton.IsEnabled = true;
            }
        }

        private async Task<DataTable> ExecuteQueryAsync(string query)
        {
            var dataTable = new DataTable();

            await Task.Run(() =>
            {
                // Build connection string with explicit pooling settings
                var builder = new SqlConnectionStringBuilder(_connectionString)
                {
                    ConnectTimeout = 30,
                    Pooling = true,
                    MinPoolSize = 0,
                    MaxPoolSize = 5
                };

                using (var connection = new SqlConnection(builder.ConnectionString))
                {
                    try
                    {
                        connection.Open();
                        using (var command = new SqlCommand(query, connection))
                        {
                            command.CommandTimeout = 30;
                            using (var adapter = new SqlDataAdapter(command))
                            {
                                adapter.Fill(dataTable);
                            }
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Pool exhausted - clear it and retry
                        SqlConnection.ClearPool(connection);
                        connection.Open();
                        using (var command = new SqlCommand(query, connection))
                        {
                            command.CommandTimeout = 30;
                            using (var adapter = new SqlDataAdapter(command))
                            {
                                adapter.Fill(dataTable);
                            }
                        }
                    }
                }
            });

            return dataTable;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _check.SqlQuery = SqlQueryTextBox.Text.Trim();
            _check.RecommendedAction = RecommendedActionTextBox.Text.Trim();
            _check.ExecutionType = ((ComboBoxItem)ExecutionTypeCombo.SelectedItem)?.Tag?.ToString() ?? "Binary";

            MessageBox.Show("Query updated.\n\nRemember to click 'Save Changes' in the Check Manager to persist to file.",
                "Saved", MessageBoxButton.OK, MessageBoxImage.Information);

            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
