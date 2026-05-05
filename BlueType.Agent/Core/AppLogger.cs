namespace BlueType.Agent.Core;

internal static class AppLogger
{
    private static readonly object SyncRoot = new();

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception is null ? message : $"{message} {exception}");
    }

    private static void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";
            var logFilePath = AppSettingsStore.GetLogFilePath(DateOnly.FromDateTime(DateTime.Now));
            lock (SyncRoot)
            {
                File.AppendAllText(logFilePath, line);
            }
        }
        catch
        {
            // Logging must not crash the app.
        }
    }
}
