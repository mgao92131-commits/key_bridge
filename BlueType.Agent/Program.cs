using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Infrastructure.Persistence;
using BlueType.Agent.Presentation.Tray;
using System.Threading;

namespace BlueType.Agent;

internal static class Program
{
    private const string InstanceMutexName = @"Local\BlueType.Agent";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        AppSettingsStore.EnsureInitialized();

        using var instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "BlueType Agent is already running.",
                "BlueType Agent",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        System.Windows.Forms.Application.ThreadException += (_, args) => AppLogger.Error("Unhandled UI exception.", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLogger.Error("Unhandled app domain exception.", args.ExceptionObject as Exception);

        AppLogger.Info("BlueType Agent starting.");
        try
        {
        System.Windows.Forms.Application.Run(new TrayAppContext());
        }
        finally
        {
            AppLogger.Info("BlueType Agent stopped.");
        }
    }
}
