namespace BlueType.Agent.Tests.TestHelpers;

internal sealed class DeviceRegistrySandbox : IDisposable
{
    public DeviceRegistrySandbox()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "BlueType.Agent.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public string SettingsFilePath => Path.Combine(DirectoryPath, "settings.json");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}
