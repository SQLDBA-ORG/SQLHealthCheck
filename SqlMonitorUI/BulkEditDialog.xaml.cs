using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using SqlCheckLibrary.Models;

namespace SqlMonitorUI
{
    public partial class BulkEditDialog : Window
    {
        private readonly List<SqlCheck> _selectedChecks;

        // Properties to indicate which fields should be changed
        public bool ChangeCategory { get; private set; }
        public bool ChangeSeverity { get; private set; }
        public bool ChangeEnabled { get; private set; }
        public bool ChangeExecutionType { get; private set; }
        public bool ChangePriority { get; private set; }

        // New values for fields
        public string Category { get; private set; } = string.Empty;
        public string Severity { get; private set; } = string.Empty;
        public bool Enabled { get; private set; }
        public string ExecutionType { get; private set; } = string.Empty;
        public int Priority { get; private set; }

        public BulkEditDialog(List<SqlCheck> selectedChecks)
        {
            InitializeComponent();
            _selectedChecks = selectedChecks;
            SelectedCountText.Text = $"{selectedChecks.Count} check{(selectedChecks.Count == 1 ? "" : "s")} selected";

            // Set default selections
            CategoryCombo.SelectedIndex = 0;
            SeverityCombo.SelectedIndex = 1; // Warning
            EnabledCombo.SelectedIndex = 0; // Enabled
            ExecutionTypeCombo.SelectedIndex = 0; // Binary
            PriorityCombo.SelectedIndex = 2; // Medium
        }

        private void ChangeCheck_Changed(object sender, RoutedEventArgs e)
        {
            // Enable/disable corresponding combo boxes
            CategoryCombo.IsEnabled = ChangeCategoryCheck.IsChecked == true;
            SeverityCombo.IsEnabled = ChangeSeverityCheck.IsChecked == true;
            EnabledCombo.IsEnabled = ChangeEnabledCheck.IsChecked == true;
            ExecutionTypeCombo.IsEnabled = ChangeExecutionTypeCheck.IsChecked == true;
            PriorityCombo.IsEnabled = ChangePriorityCheck.IsChecked == true;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate at least one change is selected
            if (ChangeCategoryCheck.IsChecked != true &&
                ChangeSeverityCheck.IsChecked != true &&
                ChangeEnabledCheck.IsChecked != true &&
                ChangeExecutionTypeCheck.IsChecked != true &&
                ChangePriorityCheck.IsChecked != true)
            {
                MessageBox.Show("Please select at least one property to change.",
                    "No Changes Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Set which fields to change
            ChangeCategory = ChangeCategoryCheck.IsChecked == true;
            ChangeSeverity = ChangeSeverityCheck.IsChecked == true;
            ChangeEnabled = ChangeEnabledCheck.IsChecked == true;
            ChangeExecutionType = ChangeExecutionTypeCheck.IsChecked == true;
            ChangePriority = ChangePriorityCheck.IsChecked == true;

            // Get new values
            if (ChangeCategory && CategoryCombo.SelectedItem is ComboBoxItem categoryItem)
            {
                Category = categoryItem.Content?.ToString() ?? "Custom";
            }

            if (ChangeSeverity && SeverityCombo.SelectedItem is ComboBoxItem severityItem)
            {
                Severity = severityItem.Content?.ToString() ?? "Warning";
            }

            if (ChangeEnabled && EnabledCombo.SelectedItem is ComboBoxItem enabledItem)
            {
                Enabled = enabledItem.Tag?.ToString() == "True";
            }

            if (ChangeExecutionType && ExecutionTypeCombo.SelectedItem is ComboBoxItem execTypeItem)
            {
                ExecutionType = execTypeItem.Tag?.ToString() ?? "Binary";
            }

            if (ChangePriority && PriorityCombo.SelectedItem is ComboBoxItem priorityItem)
            {
                if (int.TryParse(priorityItem.Tag?.ToString(), out int priority))
                {
                    Priority = priority;
                }
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
