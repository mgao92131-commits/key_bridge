using BlueType.Agent.Bootstrap;
using BlueType.Agent.Host;
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
            var synchronizationContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            var authorizationPrompt = new AuthorizationPromptPresenter(synchronizationContext);
            var runtime = AgentCompositionRoot.Create(authorizationPrompt.ShowAsync);
            System.Windows.Forms.Application.Run(
                new TrayAppContext(runtime, authorizationPrompt, synchronizationContext));
        }
        finally
        {
            AppLogger.Info("BlueType Agent stopped.");
        }
    }
}
