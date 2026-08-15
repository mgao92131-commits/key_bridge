using System.Reflection;
using System.Text.Json;
using BlueType.Agent.Application.Commands;

namespace BlueType.Agent.Tests;

public sealed class ShortcutProfileStoreTests
{
    [Fact]
    public void SeedDefaultFile_CreatesDefaultAndWindowsTerminalProfiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BlueTypeTests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "shortcut-profiles.json");

        try
        {
            var seedMethod = typeof(ShortcutProfileStore).GetMethod(
                "SeedDefaultFile",
                BindingFlags.Static | BindingFlags.NonPublic);
            seedMethod!.Invoke(null, [path]);

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var profiles = document.RootElement.GetProperty("profiles");

            Assert.Contains(
                profiles.EnumerateArray(),
                profile => profile.GetProperty("id").GetString() == "default");
            Assert.Contains(
                profiles.EnumerateArray(),
                profile => profile.GetProperty("id").GetString() == "windows-terminal");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
