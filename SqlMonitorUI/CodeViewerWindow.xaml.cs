using System.Windows;
using System;

namespace SqlMonitorUI
{
    public partial class CodeViewerWindow : Window
    {
        public CodeViewerWindow(string title, string code)
        {
            InitializeComponent();
            TitleText.Text = title;
            CodeTextBox.Text = code;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(CodeTextBox.Text);
                MessageBox.Show("Code copied to clipboard!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
