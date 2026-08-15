using System.Text.Json;
using BlueType.Agent.Infrastructure.Shortcuts;
using BlueType.Protocol;

namespace BlueType.Agent.Tests;

public sealed class ShortcutProfileStoreTests
{
    [Fact]
    public void Load_CreatesDefaultAndWindowsTerminalProfiles_WhenFileIsMissing()
    {
        using var sandbox = new ProfileFileSandbox();
        var profiles = new JsonShortcutProfileRepository(sandbox.Path).Load();

        Assert.Contains(profiles, profile => profile.Id == "default");
        Assert.Contains(profiles, profile => profile.Id == "windows-terminal");
    }

    [Fact]
    public void Load_AppendsDefaultProfile_WhenConfiguredFileDoesNotContainOne()
    {
        using var sandbox = new ProfileFileSandbox();
        Directory.CreateDirectory(Path.GetDirectoryName(sandbox.Path)!);
        var document = new
        {
            profiles = new[]
            {
                new
                {
                    id = "terminal",
                    name = "Terminal",
                    match = new
                    {
                        windowsProcesses = new[] { "WindowsTerminal" },
                        macBundleIds = Array.Empty<string>(),
                    },
                    profile = new { kind = "test" },
                },
            },
        };
        File.WriteAllText(sandbox.Path, JsonSerializer.Serialize(document, JsonProtocol.SerializerOptions));

        var profiles = new JsonShortcutProfileRepository(sandbox.Path).Load();

        Assert.Contains(profiles, profile => profile.Id == "terminal");
        Assert.Contains(profiles, profile => profile.Id == "default");
    }

    [Fact]
    public void Load_ReturnsEmptyList_WhenJsonIsInvalid()
    {
        using var sandbox = new ProfileFileSandbox();
        Directory.CreateDirectory(Path.GetDirectoryName(sandbox.Path)!);
        File.WriteAllText(sandbox.Path, "{ invalid json");

        var profiles = new JsonShortcutProfileRepository(sandbox.Path).Load();

        Assert.Empty(profiles);
    }

    private sealed class ProfileFileSandbox : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"BlueTypeTests-{Guid.NewGuid():N}");

        public string Path => System.IO.Path.Combine(_directory, "shortcut-profiles.json");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
