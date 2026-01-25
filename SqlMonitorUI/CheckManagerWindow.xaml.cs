using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using SqlCheckLibrary.Models;
using SqlCheckLibrary.Services;

namespace SqlMonitorUI
{
    public partial class CheckManagerWindow : Window
    {
        private ObservableCollection<SqlCheck> _allChecks;
        private ObservableCollection<SqlCheck> _filteredChecks;
        private CheckRepository _repository;
        private string _connectionString;

        public CheckManagerWindow(CheckRepository repository, string connectionString)
        {
            InitializeComponent();
            _repository = repository;
            _connectionString = connectionString;
            _allChecks = new ObservableCollection<SqlCheck>();
            _filteredChecks = new ObservableCollection<SqlCheck>();
            
            ChecksDataGrid.ItemsSource = _filteredChecks;
            
            LoadChecks();
        }

        private void LoadChecks()
        {
            _allChecks.Clear();
            var checks = _repository.GetAllChecks();
            
            foreach (var check in checks)
            {
                _allChecks.Add(check);
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            // Guard against calls before initialization completes
            if (_filteredChecks == null || _allChecks == null)
                return;
                
            _filteredChecks.Clear();

            string? sourceFilter = null;
            if (SpBlitzRadio?.IsChecked == true)
                sourceFilter = "sp_Blitz";
            else if (SpTriageRadio?.IsChecked == true)
                sourceFilter = "sp_triage";
            else if (CustomRadio?.IsChecked == true)
                sourceFilter = "Custom";

            foreach (var check in _allChecks)
            {
                if (sourceFilter == null || check.Source == sourceFilter)
                {
                    _filteredChecks.Add(check);
                }
            }

            if (CountText != null)
                CountText.Text = $"{_filteredChecks.Count} checks";
        }

        private void SourceFilter_Changed(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var importDialog = new ImportChecksDialog();
            if (importDialog.ShowDialog() == true)
            {
                try
                {
                    var parser = new SpBlitzParser();
                    var checksToAdd = new List<SqlCheck>();

                    // Import selected scripts
                    if (importDialog.ImportSpBlitz && !string.IsNullOrEmpty(importDialog.SpBlitzPath))
                    {
                        var blitzChecks = await parser.ParseSpBlitzFile(importDialog.SpBlitzPath);
                        var sqlChecks = parser.ConvertToSqlChecks(blitzChecks, "sp_Blitz");
                        checksToAdd.AddRange(sqlChecks);
                    }

                    if (importDialog.ImportSpTriage && !string.IsNullOrEmpty(importDialog.SpTriagePath))
                    {
                        var triageChecks = await parser.ParseSpTriageFile(importDialog.SpTriagePath);
                        var sqlChecks = parser.ConvertToSqlChecks(triageChecks, "sp_triage");
                        checksToAdd.AddRange(sqlChecks);
                    }

                    if (checksToAdd.Count == 0)
                    {
                        MessageBox.Show("No checks were selected for import.", "Import", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    // Merge with existing checks
                    var merged = parser.MergeChecks(_allChecks.ToList(), checksToAdd);
                    
                    // Update repository
                    await parser.SaveChecksToFile(merged, "sql-checks.json");
                    await _repository.LoadChecksAsync();

                    MessageBox.Show($"Successfully imported {checksToAdd.Count} checks!\n\n" +
                                  $"Total checks: {merged.Count}\n" +
                                  $"Don't forget to click 'Save Changes' to persist.",
                        "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);

                    LoadChecks();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error importing checks: {ex.Message}", 
                        "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export Checks to JSON",
                    Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                    DefaultExt = ".json",
                    FileName = $"sql-checks-export-{DateTime.Now:yyyyMMdd-HHmmss}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    // Get current filtered checks or all checks
                    var checksToExport = AllSourcesRadio.IsChecked == true 
                        ? _allChecks.ToList() 
                        : _filteredChecks.ToList();

                    var json = JsonSerializer.Serialize(checksToExport, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });

                    File.WriteAllText(dialog.FileName, json);

                    MessageBox.Show($"Exported {checksToExport.Count} checks to:\n{dialog.FileName}", 
                        "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting checks: {ex.Message}", 
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var newCheck = new SqlCheck
            {
                Id = $"CUSTOM_{DateTime.Now:yyyyMMddHHmmss}",
                Name = "New Check",
                Description = "Enter description",
                Category = "Custom",
                Severity = "Info",
                SqlQuery = "SELECT 0 AS Result -- Enter your query here",
                ExpectedValue = 0,
                Enabled = false,
                Source = "Custom",
                RecommendedAction = "Enter recommended action",
                ExecutionType = "Binary",
                Priority = 3,
                SeverityScore = 1
            };

            _allChecks.Add(newCheck);
            ApplyFilter();

            MessageBox.Show("New check added. Edit the SQL query and other fields, then save changes.",
                "Check Added", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = ChecksDataGrid.SelectedItems.Cast<SqlCheck>().ToList();
            
            if (selected.Count == 0)
            {
                MessageBox.Show("Please select checks to delete.", "Delete", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete {selected.Count} check(s)?\n\nThis cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var check in selected)
                {
                    _allChecks.Remove(check);
                }

                ApplyFilter();
                
                MessageBox.Show($"Deleted {selected.Count} check(s).\n\nClick 'Save Changes' to persist.",
                    "Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BulkEditButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = ChecksDataGrid.SelectedItems.Cast<SqlCheck>().ToList();
            
            if (selected.Count == 0)
            {
                MessageBox.Show("Please select checks to bulk edit.\n\nTip: Use Ctrl+Click or Shift+Click to select multiple checks.", 
                    "Bulk Edit", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    if (bulkEditor.ChangePriority)
                        check.Priority = bulkEditor.Priority;
                }

                ApplyFilter(); // Refresh grid
                MessageBox.Show($"Bulk edited {selected.Count} checks.\n\nClick 'Save Changes' to persist.",
                    "Bulk Edit Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void EditSqlButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var check = button?.Tag as SqlCheck;

            if (check != null)
            {
                var editor = new SqlQueryEditorWindow(check, _connectionString);
                editor.ShowDialog();
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var parser = new SpBlitzParser();
                await parser.SaveChecksToFile(_allChecks.ToList(), "sql-checks.json");
                await _repository.LoadChecksAsync();

                MessageBox.Show($"Saved {_allChecks.Count} checks to sql-checks.json",
                    "Save Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving checks: {ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Check for unsaved changes
            var result = MessageBox.Show(
                "Close without saving changes?\n\nAny unsaved modifications will be lost.",
                "Confirm Close",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                this.Close();
            }
            else if (result == MessageBoxResult.No)
            {
                SaveButton_Click(sender, e);
                this.Close();
            }
        }

        private void ChecksDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Auto-save indication could go here
        }
    }
}
