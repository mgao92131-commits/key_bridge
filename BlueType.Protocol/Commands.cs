namespace BlueType.Protocol;

public static class Commands
{
    public const string Hello = "hello";
    public const string TextInsert = "text_insert";
    public const string KeyTap = "key_tap";
    public const string KeyDown = "key_down";
    public const string KeyUp = "key_up";
    public const string Combo = "combo";
    public const string MouseMove = "mouse_move";
    public const string MouseButton = "mouse_button";
    public const string MouseClick = "mouse_click";
    public const string MouseScroll = "mouse_scroll";
    public const string ClipboardSet = "clipboard_set";
    public const string ClipboardGet = "clipboard_get";
    public const string Ping = "ping";
    public const string Pong = "pong";
}
