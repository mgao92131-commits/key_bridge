using BlueType.Protocol;

namespace BlueType.TestClient.Protocol;

internal static class EnvelopeFactory
{
    public static Envelope Hello(string deviceId, string deviceName, string appVersion, string? token)
    {
        return Create(Commands.Hello, new { deviceId, deviceName, appVersion }, token);
    }

    public static Envelope Ping()
    {
        return Create(Commands.Ping, new { });
    }

    public static Envelope TextInsert(string text)
    {
        return Create(Commands.TextInsert, new { text });
    }

    public static Envelope KeyTap(string key)
    {
        return Create(Commands.KeyTap, new { key });
    }

    public static Envelope KeyDown(string key)
    {
        return Create(Commands.KeyDown, new { key });
    }

    public static Envelope KeyUp(string key)
    {
        return Create(Commands.KeyUp, new { key });
    }

    public static Envelope Combo(IReadOnlyList<string> keys)
    {
        return Create(Commands.Combo, new { keys });
    }

    public static Envelope MouseMove(int dx, int dy)
    {
        return Create(Commands.MouseMove, new { dx, dy });
    }

    public static Envelope MouseClick(string button, int repeat)
    {
        return Create(Commands.MouseClick, new { button, repeat });
    }

    public static Envelope MouseScroll(int deltaX, int deltaY)
    {
        return Create(Commands.MouseScroll, new { deltaX, deltaY });
    }

    public static Envelope ClipboardGet()
    {
        return Create(Commands.ClipboardGet, new { });
    }

    public static Envelope ClipboardSet(string text)
    {
        return Create(Commands.ClipboardSet, new { text });
    }

    private static Envelope Create(string type, object payload, string? token = null)
    {
        return JsonProtocol.CreateEnvelope(
            id: Guid.NewGuid().ToString("D"),
            type: type,
            payload: payload,
            token: token);
    }
}
