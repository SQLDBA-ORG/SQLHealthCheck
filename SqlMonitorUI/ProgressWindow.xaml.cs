using System.Windows;

namespace SqlMonitorUI
{
    public partial class ProgressWindow : Window
    {
        public ProgressWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Update progress display - call from UI thread or use Dispatcher
        /// </summary>
        public void UpdateProgress(string status, int percentage)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateProgress(status, percentage));
                return;
            }

            StatusText.Text = status;
            ProgressBar.Value = percentage;
            PercentageText.Text = $"{percentage}%";
        }

        /// <summary>
        /// Set status text only
        /// </summary>
        public void SetStatus(string status)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetStatus(status));
                return;
            }

            StatusText.Text = status;
        }

        /// <summary>
        /// Set progress value only
        /// </summary>
        public void SetProgress(int percentage)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetProgress(percentage));
                return;
            }

            ProgressBar.Value = percentage;
            PercentageText.Text = $"{percentage}%";
        }
    }
}
