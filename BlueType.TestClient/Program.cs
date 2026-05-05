using BlueType.Protocol;
using BlueType.TestClient.Protocol;
using BlueType.TestClient.Transports;
using System.Net.Sockets;
using System.Text.Json;
using InTheHand.Net;

namespace BlueType.TestClient;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Any(static arg => arg is "--help" or "-h"))
        {
            PrintUsage();
            return 0;
        }

        try
        {
            var options = CliOptions.Parse(args);
            await using var transport = await ConnectAsync(options);
            await RunSessionAsync(transport, options);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<IAsyncDisposableStream> ConnectAsync(CliOptions options)
    {
        return options.Transport switch
        {
            "tcp" => await TcpTransport.ConnectAsync(options.Host!, options.Port),
            "bluetooth" => await BluetoothTransport.ConnectAsync(options.Address!),
            _ => throw new InvalidOperationException($"Unsupported transport: {options.Transport}"),
        };
    }

    private static async Task RunSessionAsync(IAsyncDisposableStream transport, CliOptions options)
    {
        var hello = EnvelopeFactory.Hello(
            deviceId: options.DeviceId,
            deviceName: options.DeviceName,
            appVersion: "test-client",
            token: options.Token);

        await FrameCodec.WriteAsync(transport.Stream, hello);
        Console.WriteLine($"-> {hello.Type} {hello.Id}");

        var authorized = false;
        string? issuedToken = null;

        while (true)
        {
            var response = await FrameCodec.ReadAsync(transport.Stream);
            if (response is null)
            {
                throw new EndOfStreamException("Connection closed before handshake completed.");
            }

            PrintEnvelope("<-", response);

            if (response.Type == Responses.AuthPending)
            {
                continue;
            }

            if (response.Type == Responses.AuthResult)
            {
                authorized = true;
                issuedToken = TryGetString(response.Payload, "token");
                break;
            }

            if (response.Type == Responses.Ack && options.Token is not null)
            {
                authorized = true;
                break;
            }

            if (response.Type == Responses.Error)
            {
                return;
            }
        }

        if (!authorized || options.Command == SessionCommand.HelloOnly)
        {
            if (!string.IsNullOrWhiteSpace(issuedToken))
            {
                Console.WriteLine($"token: {issuedToken}");
            }

            return;
        }

        switch (options.Command)
        {
            case SessionCommand.AltTab:
                await RunAltTabAsync(transport.Stream, options.CommandArgument);
                break;
            case SessionCommand.WinMenu:
                await RunWinMenuAsync(transport.Stream);
                break;
            case SessionCommand.ShiftSelect:
                await RunShiftSelectAsync(transport.Stream, options.CommandArgument);
                break;
            default:
            {
                var commandEnvelope = options.Command switch
                {
                    SessionCommand.Ping => EnvelopeFactory.Ping(),
                    SessionCommand.Text => EnvelopeFactory.TextInsert(options.CommandArgument!),
                    SessionCommand.Key => EnvelopeFactory.KeyTap(options.CommandArgument!),
                    SessionCommand.KeyDown => EnvelopeFactory.KeyDown(options.CommandArgument!),
                    SessionCommand.KeyUp => EnvelopeFactory.KeyUp(options.CommandArgument!),
                    SessionCommand.Combo => EnvelopeFactory.Combo(ParseCombo(options.CommandArgument!)),
                    SessionCommand.MouseMove => CreateMouseMoveEnvelope(options.CommandArgument!),
                    SessionCommand.MouseClick => CreateMouseClickEnvelope(options.CommandArgument!),
                    SessionCommand.MouseScroll => CreateMouseScrollEnvelope(options.CommandArgument!),
                    SessionCommand.ClipboardGet => EnvelopeFactory.ClipboardGet(),
                    SessionCommand.ClipboardSet => EnvelopeFactory.ClipboardSet(options.CommandArgument!),
                    _ => throw new InvalidOperationException($"Unsupported command: {options.Command}"),
                };

                await SendCommandAsync(transport.Stream, commandEnvelope);
                break;
            }
        }

        if (!string.IsNullOrWhiteSpace(issuedToken))
        {
            Console.WriteLine($"token: {issuedToken}");
        }
    }

    private static IReadOnlyList<string> ParseCombo(string raw)
    {
        return raw.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task SendCommandAsync(Stream stream, Envelope commandEnvelope)
    {
        await FrameCodec.WriteAsync(stream, commandEnvelope);
        Console.WriteLine($"-> {commandEnvelope.Type} {commandEnvelope.Id}");

        var commandResponse = await FrameCodec.ReadAsync(stream);
        if (commandResponse is null)
        {
            throw new EndOfStreamException("Connection closed before command response.");
        }

        PrintEnvelope("<-", commandResponse);
    }

    private static async Task RunAltTabAsync(Stream stream, string? rawCount)
    {
        var count = string.IsNullOrWhiteSpace(rawCount) ? 1 : int.Parse(rawCount);
        if (count <= 0)
        {
            throw new InvalidOperationException("alt-tab requires a positive count.");
        }

        await SendCommandAsync(stream, EnvelopeFactory.KeyDown("ALT"));
        try
        {
            for (var index = 0; index < count; index++)
            {
                await SendCommandAsync(stream, EnvelopeFactory.KeyTap("TAB"));
                await Task.Delay(120);
            }
        }
        finally
        {
            await SendCommandAsync(stream, EnvelopeFactory.KeyUp("ALT"));
        }
    }

    private static async Task RunWinMenuAsync(Stream stream)
    {
        await SendCommandAsync(stream, EnvelopeFactory.KeyDown("WIN"));
        try
        {
            await Task.Delay(120);
        }
        finally
        {
            await SendCommandAsync(stream, EnvelopeFactory.KeyUp("WIN"));
        }
    }

    private static async Task RunShiftSelectAsync(Stream stream, string? rawArgument)
    {
        var (count, direction) = ParseShiftSelect(rawArgument);

        await SendCommandAsync(stream, EnvelopeFactory.KeyDown("SHIFT"));
        try
        {
            for (var index = 0; index < count; index++)
            {
                await SendCommandAsync(stream, EnvelopeFactory.KeyTap(direction));
                await Task.Delay(80);
            }
        }
        finally
        {
            await SendCommandAsync(stream, EnvelopeFactory.KeyUp("SHIFT"));
        }
    }

    private static (int Count, string Direction) ParseShiftSelect(string? rawArgument)
    {
        if (string.IsNullOrWhiteSpace(rawArgument))
        {
            return (3, "RIGHT");
        }

        var parts = rawArgument.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2)
        {
            throw new InvalidOperationException("shift-select accepts [count] or [count,direction].");
        }

        var count = int.Parse(parts[0]);
        if (count <= 0)
        {
            throw new InvalidOperationException("shift-select count must be positive.");
        }

        var direction = parts.Length == 2 ? parts[1].ToUpperInvariant() : "RIGHT";
        if (direction is not ("LEFT" or "RIGHT" or "UP" or "DOWN"))
        {
            throw new InvalidOperationException("shift-select direction must be LEFT, RIGHT, UP, or DOWN.");
        }

        return (count, direction);
    }

    private static Envelope CreateMouseMoveEnvelope(string raw)
    {
        var parts = ParseCsvInts(raw, 2, "mouse-move requires dx,dy");
        return EnvelopeFactory.MouseMove(parts[0], parts[1]);
    }

    private static Envelope CreateMouseClickEnvelope(string raw)
    {
        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length > 2)
        {
            throw new InvalidOperationException("mouse-click requires button[,repeat]");
        }

        var button = parts[0].ToUpperInvariant();
        var repeat = parts.Length == 2 ? int.Parse(parts[1]) : 1;
        return EnvelopeFactory.MouseClick(button, repeat);
    }

    private static Envelope CreateMouseScrollEnvelope(string raw)
    {
        var parts = ParseCsvInts(raw, 2, "mouse-scroll requires deltaX,deltaY");
        return EnvelopeFactory.MouseScroll(parts[0], parts[1]);
    }

    private static int[] ParseCsvInts(string raw, int expectedCount, string error)
    {
        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != expectedCount)
        {
            throw new InvalidOperationException(error);
        }

        return parts.Select(int.Parse).ToArray();
    }

    private static void PrintEnvelope(string prefix, Envelope envelope)
    {
        Console.WriteLine($"{prefix} {envelope.Type} {envelope.Id}");
        if (envelope.Payload.ValueKind != JsonValueKind.Undefined && envelope.Payload.ValueKind != JsonValueKind.Null)
        {
            Console.WriteLine(JsonSerializer.Serialize(envelope.Payload, JsonProtocol.SerializerOptions));
        }
    }

    private static string? TryGetString(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
Usage:
  BlueType.TestClient tcp --host 127.0.0.1 [--port 24862] --device-id android-001 --device-name "Test Phone" hello
  BlueType.TestClient tcp --host 127.0.0.1 --device-id android-001 --device-name "Test Phone" text "Hello"
  BlueType.TestClient tcp --host 127.0.0.1 --device-id android-001 --device-name "Test Phone" --token <token> key ENTER
  BlueType.TestClient tcp --host 127.0.0.1 --device-id android-001 --device-name "Test Phone" --token <token> key-down ALT
  BlueType.TestClient tcp --host 127.0.0.1 --device-id android-001 --device-name "Test Phone" --token <token> alt-tab 2
  BlueType.TestClient tcp --host 127.0.0.1 --device-id android-001 --device-name "Test Phone" --token <token> win-menu
  BlueType.TestClient tcp --host 127.0.0.1 --device-id android-001 --device-name "Test Phone" --token <token> shift-select 4,RIGHT
  BlueType.TestClient tcp --host 127.0.0.1 --device-id android-001 --device-name "Test Phone" combo CTRL+C
  BlueType.TestClient tcp --host 127.0.0.1 --device-id android-001 --device-name "Test Phone" mouse-move 40,15
  BlueType.TestClient tcp --host 127.0.0.1 --device-id android-001 --device-name "Test Phone" mouse-click LEFT,2
  BlueType.TestClient tcp --host 127.0.0.1 --device-id android-001 --device-name "Test Phone" mouse-scroll 0,-1
  BlueType.TestClient bluetooth --address 001122334455 --device-id android-001 --device-name "Test Phone" hello

Commands:
  hello
  ping
  text <value>
  key <value>
  key-down <value>
  key-up <value>
  alt-tab [count]
  win-menu
  shift-select [count[,direction]]
  combo <CTRL+C>
  mouse-move <dx,dy>
  mouse-click <button[,repeat]>
  mouse-scroll <deltaX,deltaY>
  clipboard-get
  clipboard-set <value>
""");
    }
}

internal sealed class CliOptions
{
    public required string Transport { get; init; }
    public string? Host { get; init; }
    public int Port { get; init; } = 24862;
    public string? Address { get; init; }
    public required string DeviceId { get; init; }
    public required string DeviceName { get; init; }
    public string? Token { get; init; }
    public required SessionCommand Command { get; init; }
    public string? CommandArgument { get; init; }

    public static CliOptions Parse(string[] args)
    {
        if (args.Length < 2)
        {
            throw new InvalidOperationException("Missing transport or command.");
        }

        var transport = args[0].Trim().ToLowerInvariant();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length)
                {
                    throw new InvalidOperationException($"Missing value for {arg}.");
                }

                options[arg[2..]] = args[++i];
                continue;
            }

            positionals.Add(arg);
        }

        if (positionals.Count == 0)
        {
            throw new InvalidOperationException("Missing command.");
        }

        var command = ParseCommand(positionals[0]);
        var commandArgument = positionals.Count > 1 ? positionals[1] : null;

        if (!options.TryGetValue("device-id", out var deviceId))
        {
            throw new InvalidOperationException("Missing --device-id.");
        }

        if (!options.TryGetValue("device-name", out var deviceName))
        {
            throw new InvalidOperationException("Missing --device-name.");
        }

        return transport switch
        {
            "tcp" => new CliOptions
            {
                Transport = transport,
                Host = GetRequired(options, "host"),
                Port = options.TryGetValue("port", out var portValue) ? int.Parse(portValue) : 24862,
                DeviceId = deviceId,
                DeviceName = deviceName,
                Token = options.GetValueOrDefault("token"),
                Command = command,
                CommandArgument = commandArgument,
            },
            "bluetooth" => new CliOptions
            {
                Transport = transport,
                Address = GetRequired(options, "address"),
                DeviceId = deviceId,
                DeviceName = deviceName,
                Token = options.GetValueOrDefault("token"),
                Command = command,
                CommandArgument = commandArgument,
            },
            _ => throw new InvalidOperationException("Transport must be tcp or bluetooth."),
        };
    }

    private static string GetRequired(IReadOnlyDictionary<string, string> options, string key)
    {
        if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing --{key}.");
        }

        return value;
    }

    private static SessionCommand ParseCommand(string raw)
    {
        return raw.ToLowerInvariant() switch
        {
            "hello" => SessionCommand.HelloOnly,
            "ping" => SessionCommand.Ping,
            "text" => SessionCommand.Text,
            "key" => SessionCommand.Key,
            "key-down" => SessionCommand.KeyDown,
            "key-up" => SessionCommand.KeyUp,
            "alt-tab" => SessionCommand.AltTab,
            "win-menu" => SessionCommand.WinMenu,
            "shift-select" => SessionCommand.ShiftSelect,
            "combo" => SessionCommand.Combo,
            "mouse-move" => SessionCommand.MouseMove,
            "mouse-click" => SessionCommand.MouseClick,
            "mouse-scroll" => SessionCommand.MouseScroll,
            "clipboard-get" => SessionCommand.ClipboardGet,
            "clipboard-set" => SessionCommand.ClipboardSet,
            _ => throw new InvalidOperationException($"Unsupported command: {raw}"),
        };
    }
}

internal enum SessionCommand
{
    HelloOnly,
    Ping,
    Text,
    Key,
    KeyDown,
    KeyUp,
    AltTab,
    WinMenu,
    ShiftSelect,
    Combo,
    MouseMove,
    MouseClick,
    MouseScroll,
    ClipboardGet,
    ClipboardSet,
}
