using System.Windows;

namespace KeyShield
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Ensure only one instance
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            var existing = System.Diagnostics.Process
                .GetProcessesByName(proc.ProcessName);
            if (existing.Length > 1)
            {
                System.Windows.MessageBox.Show("KeyShield is already running.",
                    "KeyShield", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
            }
        }
    }
}
