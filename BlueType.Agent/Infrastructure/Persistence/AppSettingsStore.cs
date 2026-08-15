namespace BlueType.Agent.Infrastructure.Persistence;

internal static class AppSettingsStore
{
    public static string BaseDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlueType");

    public static string SettingsFilePath => Path.Combine(BaseDirectory, "settings.json");

    public static string LogsDirectory => Path.Combine(BaseDirectory, "logs");

    public static string GetLogFilePath(DateOnly date)
    {
        return Path.Combine(LogsDirectory, $"{date:yyyy-MM-dd}.log");
    }

    public static void EnsureInitialized()
    {
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
