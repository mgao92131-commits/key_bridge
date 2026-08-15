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
    private static readonly TimeSpan RuntimeShutdownTimeout = TimeSpan.FromSeconds(10);

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
        AgentRuntime? runtime = null;
        try
        {
            var synchronizationContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            var authorizationPrompt = new AuthorizationPromptPresenter(synchronizationContext);
            runtime = AgentCompositionRoot.Create(authorizationPrompt.ShowAsync);
            System.Windows.Forms.Application.Run(
                new TrayAppContext(runtime, authorizationPrompt, synchronizationContext));
        }
        finally
        {
            if (runtime is not null)
            {
                try
                {
                    var stopTask = runtime.StopAsync();
                    if (!stopTask.Wait(RuntimeShutdownTimeout))
                    {
                        AppLogger.Warn(
                            $"Agent runtime shutdown did not complete within {RuntimeShutdownTimeout.TotalSeconds:0} seconds.");
                    }
                    else
                    {
                        stopTask.GetAwaiter().GetResult();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Agent runtime shutdown failed during program cleanup.", ex);
                }
            }

            AppLogger.Info("BlueType Agent stopped.");
        }
    }
}
