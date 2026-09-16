using System.IO;
using System.Windows;
using SimVROptimizer.Core;

namespace SimVROptimizer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "VR Auto-Optimizer", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        if (e.Args.Any(argument => argument.Equals("--uninstall-cleanup", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                new RecoveryShortcutService().RemoveAll();
                var toolbarSource = Path.Combine(AppContext.BaseDirectory, "MSFS", MsfsToolbarPanelInstaller.PackageName);
                new MsfsToolbarPanelInstaller(toolbarSource).RemoveAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Uninstallation must continue even if an optional MSFS package or shortcut is unavailable.
            }
            Shutdown();
            return;
        }
        var continueSession = e.Args.Any(argument => argument.Equals("--continue-session", StringComparison.OrdinalIgnoreCase));
        var restoreLastSession = e.Args.Any(argument => argument.Equals("--restore-last-session", StringComparison.OrdinalIgnoreCase));
        MainWindow = new MainWindow(continueSession, restoreLastSession);
        MainWindow.Show();
    }
}
