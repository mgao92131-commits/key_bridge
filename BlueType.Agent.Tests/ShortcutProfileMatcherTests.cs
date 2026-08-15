using System.Reflection;
using System.Runtime.CompilerServices;
using BlueType.Agent.Application.Commands;
using BlueType.Protocol;

namespace BlueType.Agent.Tests;

public sealed class ShortcutProfileMatcherTests
{
    [Theory]
    [InlineData("WindowsTerminal", "WindowsTerminal")]
    [InlineData("WindowsTerminal", "WindowsTerminal.exe")]
    [InlineData("WINDOWSTERMINAL.EXE", "windowsterminal")]
    public void Match_IsCaseInsensitiveAndIgnoresExeSuffix(string configuredName, string processName)
    {
        var profiles = new[]
        {
            CreateProfile("terminal", configuredName),
            CreateProfile("default"),
        };

        var matched = Match(profiles, processName);

        Assert.Equal("terminal", matched?.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("unknown-process")]
    public void Match_FallsBackToDefault_WhenProcessIsUnknown(string? processName)
    {
        var profiles = new[]
        {
            CreateProfile("terminal", "WindowsTerminal"),
            CreateProfile("default"),
        };

        var matched = Match(profiles, processName);

        Assert.Equal("default", matched?.Id);
    }

    [Fact]
    public void Match_ReturnsNull_WhenNoProfileMatchesAndDefaultIsMissing()
    {
        var matched = Match([CreateProfile("terminal", "WindowsTerminal")], "unknown-process");

        Assert.Null(matched);
    }

    private static ShortcutProfileDefinition? Match(
        IReadOnlyList<ShortcutProfileDefinition> profiles,
        string? processName)
    {
        var dispatcher = (ShortcutProfileDispatcher)RuntimeHelpers.GetUninitializedObject(
            typeof(ShortcutProfileDispatcher));
        var profilesField = typeof(ShortcutProfileDispatcher).GetField(
            "_profiles",
            BindingFlags.Instance | BindingFlags.NonPublic);
        profilesField!.SetValue(dispatcher, profiles);

        var matchMethod = typeof(ShortcutProfileDispatcher).GetMethod(
            "Match",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (ShortcutProfileDefinition?)matchMethod!.Invoke(dispatcher, [processName]);
    }

    private static ShortcutProfileDefinition CreateProfile(
        string id,
        params string[] windowsProcesses)
    {
        return new ShortcutProfileDefinition(
            id,
            id,
            new ShortcutProfileMatch(windowsProcesses, []),
            JsonProtocol.ToElement(new { id }).Clone());
    }
}
