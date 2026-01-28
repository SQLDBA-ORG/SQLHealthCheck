using System.Windows;

namespace SqlMonitorUI
{
    public partial class ImportChecksDialog : Window
    {
        public bool ImportSpBlitz { get; private set; }
        public bool ImportSpTriage { get; private set; }
        public bool ImportBpCheck { get; private set; }
        public string? SpBlitzPath { get; private set; }
        public string? SpTriagePath { get; private set; }
        public string? BpCheckPath { get; private set; }
        public bool DetectDuplicates { get; private set; }

        public ImportChecksDialog()
        {
            InitializeComponent();
            UpdateImportButtonState();
        }

        private void BrowseSpBlitzButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select sp_Blitz.sql file",
                Filter = "SQL Files (*.sql)|*.sql|All Files (*.*)|*.*",
                DefaultExt = ".sql"
            };

            if (dialog.ShowDialog() == true)
            {
                SpBlitzPathTextBox.Text = dialog.FileName;
                SpBlitzPath = dialog.FileName;
                SpBlitzCheckBox.IsChecked = true;
                UpdateImportButtonState();
            }
        }

        private void BrowseSpTriageButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select sp_triage.sql file",
                Filter = "SQL Files (*.sql)|*.sql|All Files (*.*)|*.*",
                DefaultExt = ".sql"
            };

            if (dialog.ShowDialog() == true)
            {
                SpTriagePathTextBox.Text = dialog.FileName;
                SpTriagePath = dialog.FileName;
                SpTriageCheckBox.IsChecked = true;
                UpdateImportButtonState();
            }
        }

        private void BrowseBpCheckButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Check_BP_Servers.sql file",
                Filter = "SQL Files (*.sql)|*.sql|All Files (*.*)|*.*",
                DefaultExt = ".sql"
            };

            if (dialog.ShowDialog() == true)
            {
                BpCheckPathTextBox.Text = dialog.FileName;
                BpCheckPath = dialog.FileName;
                BpCheckCheckBox.IsChecked = true;
                UpdateImportButtonState();
            }
        }

        private void SpBlitzCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (BrowseSpBlitzButton == null) return;
            BrowseSpBlitzButton.IsEnabled = SpBlitzCheckBox.IsChecked == true;
            UpdateImportButtonState();
        }

        private void SpTriageCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (BrowseSpTriageButton == null) return;
            BrowseSpTriageButton.IsEnabled = SpTriageCheckBox.IsChecked == true;
            UpdateImportButtonState();
        }

        private void BpCheckCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (BrowseBpCheckButton == null) return;
            BrowseBpCheckButton.IsEnabled = BpCheckCheckBox.IsChecked == true;
            UpdateImportButtonState();
        }

        private void UpdateImportButtonState()
        {
            if (SpBlitzCheckBox == null || SpTriageCheckBox == null || BpCheckCheckBox == null || ImportButton == null) return;
            
            bool canImport = false;

            if (SpBlitzCheckBox.IsChecked == true && !string.IsNullOrEmpty(SpBlitzPath))
                canImport = true;

            if (SpTriageCheckBox.IsChecked == true && !string.IsNullOrEmpty(SpTriagePath))
                canImport = true;

            if (BpCheckCheckBox.IsChecked == true && !string.IsNullOrEmpty(BpCheckPath))
                canImport = true;

            ImportButton.IsEnabled = canImport;
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            ImportSpBlitz = SpBlitzCheckBox.IsChecked == true && !string.IsNullOrEmpty(SpBlitzPath);
            ImportSpTriage = SpTriageCheckBox.IsChecked == true && !string.IsNullOrEmpty(SpTriagePath);
            ImportBpCheck = BpCheckCheckBox.IsChecked == true && !string.IsNullOrEmpty(BpCheckPath);
            DetectDuplicates = DetectDuplicatesCheckBox.IsChecked == true;

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
