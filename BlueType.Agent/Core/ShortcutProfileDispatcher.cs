using System.Diagnostics;
using System.Text.Json;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Native;
using BlueType.Agent.Transport;
using BlueType.Protocol;

namespace BlueType.Agent.Core;

internal interface IShortcutProfileDispatcher
{
    void RegisterSession(Guid sessionId, ClientSession session);
    void UnregisterSession(Guid sessionId);
}

internal sealed class ShortcutProfileDispatcher : IShortcutProfileDispatcher, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan StableDuration = TimeSpan.FromMilliseconds(500);

    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly IReadOnlyList<ShortcutProfileDefinition> _profiles;
    private readonly Task _pollTask;

    private ActiveShortcutSession? _activeSession;
    private string? _observedProcess;
    private long _observedSince;
    private string? _lastSentProfileKey;

    public ShortcutProfileDispatcher()
    {
        _profiles = ShortcutProfileStore.Load();
        _pollTask = Task.Run(PollAsync);
    }

    public void RegisterSession(Guid sessionId, ClientSession session)
    {
        lock (_gate)
        {
            _activeSession = new ActiveShortcutSession(sessionId, session);
            _lastSentProfileKey = null;
        }

        _ = SendCurrentAsync(CancellationToken.None);
    }

    public void UnregisterSession(Guid sessionId)
    {
        lock (_gate)
        {
            if (_activeSession?.SessionId == sessionId)
            {
                _activeSession = null;
                _lastSentProfileKey = null;
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _pollTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
        _stop.Dispose();
    }

    private async Task PollAsync()
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                var processName = ForegroundProcessReader.CurrentProcessName();
                var now = Environment.TickCount64;
                var stable = false;

                lock (_gate)
                {
                    if (!string.Equals(_observedProcess, processName, StringComparison.OrdinalIgnoreCase))
                    {
                        _observedProcess = processName;
                        _observedSince = now;
                    }
                    else if (now - _observedSince >= StableDuration.TotalMilliseconds)
                    {
                        stable = true;
                    }
                }

                if (stable)
                {
                    await SendCurrentAsync(_stop.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.Error("Shortcut profile foreground monitor stopped.", ex);
        }
    }

    private async Task SendCurrentAsync(CancellationToken cancellationToken)
    {
        ActiveShortcutSession? session;
        string? processName;
        lock (_gate)
        {
            session = _activeSession;
            processName = _observedProcess ?? ForegroundProcessReader.CurrentProcessName();
        }

        if (session is null)
        {
            return;
        }

        var profile = Match(processName);
        var profileKey = profile?.Id ?? string.Empty;

        lock (_gate)
        {
            if (_activeSession?.SessionId != session.SessionId)
            {
                return;
            }

            if (string.Equals(_lastSentProfileKey, profileKey, StringComparison.Ordinal))
            {
                return;
            }

            _lastSentProfileKey = profileKey;
        }

        var envelope = JsonProtocol.CreateEnvelope(
            Guid.NewGuid().ToString(),
            Responses.ShortcutProfile,
            new ShortcutProfilePayload(profile?.Name, profile?.Profile));

        try
        {
            await session.Session.WriteAsync(envelope, cancellationToken);
            AppLogger.Info(profile is null
                ? $"Sent shortcut profile reset for foreground process {processName ?? "unknown"}."
                : $"Sent shortcut profile '{profile.Id}' for foreground process {processName ?? "unknown"}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Error("Failed to send shortcut profile.", ex);
        }
    }

    private ShortcutProfileDefinition? Match(string? processName)
    {
        if (!string.IsNullOrWhiteSpace(processName))
        {
            var appProfile = _profiles.FirstOrDefault(profile =>
                profile.Match.WindowsProcesses.Any(candidate => ProcessNamesEqual(candidate, processName)));
            if (appProfile is not null)
            {
                return appProfile;
            }
        }

        return _profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, "default", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ProcessNamesEqual(string configured, string actual)
    {
        static string Normalize(string value) =>
            value.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? value.Trim()[..^4]
                : value.Trim();

        return string.Equals(Normalize(configured), Normalize(actual), StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ActiveShortcutSession(Guid SessionId, ClientSession Session);
    private sealed record ShortcutProfilePayload(string? Name, JsonElement? Profile);
}

internal sealed record ShortcutProfileDefinition(
    string Id,
    string Name,
    ShortcutProfileMatch Match,
    JsonElement Profile);

internal sealed record ShortcutProfileMatch(
    IReadOnlyList<string> WindowsProcesses,
    IReadOnlyList<string> MacBundleIds);

internal static class ShortcutProfileStore
{
    public static IReadOnlyList<ShortcutProfileDefinition> Load()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlueType",
            "shortcut-profiles.json");

        if (!File.Exists(path))
        {
            try
            {
                SeedDefaultFile(path);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to create default shortcut profile file at {path}.", ex);
                return Array.Empty<ShortcutProfileDefinition>();
            }
        }

        try
        {
            var json = File.ReadAllText(path);
            var document = JsonSerializer.Deserialize<ShortcutProfileFile>(json, JsonProtocol.SerializerOptions);
            if (document?.Profiles is null)
            {
                return Array.Empty<ShortcutProfileDefinition>();
            }

            var profiles = document.Profiles
                .Where(profile => profile.Profile.ValueKind is JsonValueKind.Object)
                .Select(profile => new ShortcutProfileDefinition(
                    profile.Id ?? string.Empty,
                    profile.Name ?? profile.Id ?? string.Empty,
                    new ShortcutProfileMatch(
                        profile.Match?.WindowsProcesses ?? [],
                        profile.Match?.MacBundleIds ?? []),
                    profile.Profile.Clone()))
                .Where(profile => !string.IsNullOrWhiteSpace(profile.Id))
                .ToArray();
            if (!profiles.Any(profile => string.Equals(profile.Id, "default", StringComparison.OrdinalIgnoreCase)))
            {
                profiles = profiles.Append(CreateDefaultDefinition()).ToArray();
            }

            return profiles;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to load shortcut profiles from {path}. Android will use local defaults.", ex);
            return Array.Empty<ShortcutProfileDefinition>();
        }
    }

    private sealed class ShortcutProfileFile
    {
        public List<ShortcutProfileDto> Profiles { get; set; } = [];
    }

    private sealed class ShortcutProfileDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public ShortcutProfileMatchDto? Match { get; set; }
        public JsonElement Profile { get; set; }
    }

    private sealed class ShortcutProfileMatchDto
    {
        public List<string> WindowsProcesses { get; set; } = [];
        public List<string> MacBundleIds { get; set; } = [];
    }

    private static void SeedDefaultFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var root = new
        {
            profiles = new object[]
            {
                new
                {
                    id = "default",
                    name = "Default",
                    match = new
                    {
                        windowsProcesses = Array.Empty<string>(),
                        macBundleIds = Array.Empty<string>(),
                    },
                    profile = DefaultProfile(),
                },
                new
                {
                    id = "windows-terminal",
                    name = "Terminal",
                    match = new
                    {
                        windowsProcesses = new[] { "WindowsTerminal", "wt", "cmd", "powershell", "pwsh" },
                        macBundleIds = Array.Empty<string>(),
                    },
                    profile = WindowsTerminalProfile(),
                },
            },
        };

        var json = JsonSerializer.Serialize(root, new JsonSerializerOptions(JsonProtocol.SerializerOptions)
        {
            WriteIndented = true,
        });
        File.WriteAllText(path, json);
        AppLogger.Info($"Created default shortcut profile file at {path}.");
    }

    private static object DefaultProfile() => new
    {
        leftRail = Rail(Combo("SHIFT", "TAB"), KeyTap("TAB"), "ALT"),
        rightRail = Rail(Combo("SHIFT", "TAB"), KeyTap("TAB"), "CTRL"),
        bottomRail = Rail(KeyTap("LEFT"), KeyTap("RIGHT"), "WIN", "CTRL"),
        customButtons = new[]
        {
            Button("copy", "COPY", Combo("CTRL", "C")),
            Button("paste", "PASTE", Combo("CTRL", "V")),
            Button("cut", "CUT", Combo("CTRL", "X")),
            Button("undo", "UNDO", Combo("CTRL", "Z")),
            Button("redo", "REDO", Combo("CTRL", "Y")),
            Button("all", "ALL", Combo("CTRL", "A")),
            Button("save", "SAVE", Combo("CTRL", "S")),
            Button("find", "FIND", Combo("CTRL", "F")),
        },
    };

    private static object WindowsTerminalProfile() => new
    {
        leftRail = Rail(Combo("SHIFT", "TAB"), KeyTap("TAB"), "ALT"),
        rightRail = Rail(Combo("SHIFT", "TAB"), KeyTap("TAB"), "CTRL"),
        bottomRail = Rail(KeyTap("LEFT"), KeyTap("RIGHT"), "WIN", "CTRL"),
        customButtons = new[]
        {
            Button("copy", "COPY", Combo("CTRL", "SHIFT", "C")),
            Button("paste", "PASTE", Combo("CTRL", "SHIFT", "V")),
            Button("new_tab", "NEW TAB", Combo("CTRL", "SHIFT", "T")),
            Button("prev_tab", "PREV TAB", Combo("CTRL", "SHIFT", "TAB")),
            Button("next_tab", "NEXT TAB", Combo("CTRL", "TAB")),
            Button("interrupt", "INT", Combo("CTRL", "C")),
            Button("clear", "CLEAR", Combo("CTRL", "L")),
            Button("find", "FIND", Combo("CTRL", "SHIFT", "F")),
        },
    };

    private static object Rail(object primaryAction, object secondaryAction, params string[] stickyModifiers) => new
    {
        primaryAction,
        secondaryAction,
        stickyModifiers,
        stickyDurationMs = 600,
    };

    private static object Button(string id, string label, object action) => new
    {
        id,
        label,
        action,
    };

    private static object KeyTap(string key) => new
    {
        kind = "key_tap",
        key,
    };

    private static object Combo(params string[] keys) => new
    {
        kind = "combo",
        keys,
    };

    private static ShortcutProfileDefinition CreateDefaultDefinition() => new(
        "default",
        "Default",
        new ShortcutProfileMatch([], []),
        JsonProtocol.ToElement(DefaultProfile()).Clone());
}

internal static class ForegroundProcessReader
{
    public static string? CurrentProcessName()
    {
        var window = Win32.GetForegroundWindow();
        if (window == 0)
        {
            return null;
        }

        _ = Win32.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class NullShortcutProfileDispatcher : IShortcutProfileDispatcher
{
    public static readonly NullShortcutProfileDispatcher Instance = new();

    private NullShortcutProfileDispatcher()
    {
    }

    public void RegisterSession(Guid sessionId, ClientSession session)
    {
    }

    public void UnregisterSession(Guid sessionId)
    {
    }
}
