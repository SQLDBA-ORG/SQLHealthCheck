using System.Windows;
using SqlCheckLibrary.Services;

namespace SqlMonitorUI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Initialize resource optimization for enterprise memory management
            ResourceManager.InitializeResourceOptimization();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Cleanup resources before exit
            ResourceManager.AggressiveCleanup();

            base.OnExit(e);
        }
    }
}
