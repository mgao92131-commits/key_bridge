# BlueType Protocol Messages

## Wire Format

Every protocol message is sent as one frame:

1. 4-byte big-endian signed integer payload length.
2. UTF-8 JSON envelope.

The maximum JSON payload size is 64 KiB.

## Envelope

All protocol messages use the same envelope:

```json
{
  "v": 1,
  "id": "string-request-id",
  "type": "message_type",
  "token": "optional-session-token",
  "payload": {}
}
```

Fields:

- `v`: protocol version. Current value is `1`.
- `id`: request or event id. Responses reuse the triggering request id when applicable.
- `type`: message type.
- `token`: optional authorization token. Clients include it after a trusted auth result.
- `payload`: message-specific JSON object.

Unknown payload fields should be ignored by receivers when possible.

---

## Client Commands

### `hello`

Starts authorization for a new transport connection. This must be the first non-heartbeat command before input and clipboard commands are accepted.

Payload:

```json
{
  "deviceId": "android-001",
  "deviceName": "Pixel 9",
  "appVersion": "1.0.0"
}
```

Notes:

- `deviceId` and `deviceName` are required strings.
- `appVersion` is optional.
- If the client has a saved trusted token, it sends the token in the envelope `token` field.

### `ping`

Heartbeat message sent by either side.

Payload:

```json
{}
```

### `pong`

Heartbeat reply sent by either side.

Payload:

```json
{}
```

### `text_insert`

Injects text on the desktop.

Payload:

```json
{
  "text": "Hello"
}
```

Limits:

- `text` is required.
- Desktop agents reject text payloads above 8 KiB UTF-8.

### `key_tap`

Presses and releases one key.

Payload:

```json
{
  "key": "ENTER"
}
```

### `key_down`

Presses one key and leaves it down until `key_up` or session cleanup.

Payload:

```json
{
  "key": "CTRL"
}
```

### `key_up`

Releases one key.

Payload:

```json
{
  "key": "CTRL"
}
```

### `combo`

Sends a key combination.

Payload:

```json
{
  "keys": ["CTRL", "C"]
}
```

### `mouse_move`

Moves the mouse by a relative delta.

Payload:

```json
{
  "dx": 40,
  "dy": 15
}
```

### `mouse_button`

Presses or releases a mouse button.

Payload:

```json
{
  "button": "LEFT",
  "action": "down"
}
```

Valid `action` values:

- `down`
- `up`

### `mouse_click`

Clicks a mouse button.

Payload:

```json
{
  "button": "LEFT",
  "repeat": 2
}
```

Notes:

- `repeat` is optional and defaults to `1`.

### `mouse_scroll`

Scrolls by a relative delta.

Payload:

```json
{
  "deltaX": 0,
  "deltaY": -1
}
```

Notes:

- `deltaX` is optional and defaults to `0`.
- `deltaY` is optional and defaults to `0`.

### `clipboard_set`

Sets desktop clipboard text.

Payload:

```json
{
  "text": "Copied text"
}
```

### `clipboard_get`

Requests desktop clipboard text.

Payload:

```json
{}
```

---

## Server Responses And Events

### `ack`

Acknowledges successful command handling.

Payload:

```json
{
  "ok": true
}
```

Typical responses:

- `text_insert`
- `key_tap`
- `key_down`
- `key_up`
- `combo`
- `mouse_move`
- `mouse_button`
- `mouse_click`
- `mouse_scroll`
- `clipboard_set`

### `error`

Reports command, auth, session, input, or server failure.

Payload:

```json
{
  "code": "BUSY",
  "message": "Another device is already connected."
}
```

See `errors.md` for standard codes.

### `auth_pending`

Sent after `hello` when the desktop requires user approval.

Payload:

```json
{
  "timeoutSec": 60,
  "message": "Please confirm on Windows"
}
```

### `auth_result`

Sent after successful authorization.

Payload:

```json
{
  "ok": true,
  "token": "optional-token",
  "persistToken": true,
  "trusted": true
}
```

Notes:

- `token` can be `null` or omitted for allow-once authorization.
- `persistToken` means the client should persist `token`.
- `trusted` is currently equivalent to `persistToken` for client compatibility.

### `clipboard_value`

Response to `clipboard_get`.

Payload:

```json
{
  "text": "Clipboard contents"
}
```

### `shortcut_profile`

Desktop-to-client event that provides context-specific remote shortcut layout hints.

Payload:

```json
{
  "name": "Terminal",
  "profile": {
    "leftRail": {
      "primaryAction": { "kind": "combo", "keys": ["SHIFT", "TAB"] },
      "secondaryAction": { "kind": "key_tap", "key": "TAB" },
      "stickyModifiers": ["ALT"],
      "stickyDurationMs": 600
    },
    "rightRail": {
      "primaryAction": { "kind": "combo", "keys": ["SHIFT", "TAB"] },
      "secondaryAction": { "kind": "key_tap", "key": "TAB" },
      "stickyModifiers": ["CTRL"],
      "stickyDurationMs": 600
    },
    "bottomRail": {
      "primaryAction": { "kind": "key_tap", "key": "LEFT" },
      "secondaryAction": { "kind": "key_tap", "key": "RIGHT" },
      "stickyModifiers": ["WIN", "CTRL"],
      "stickyDurationMs": 600
    },
    "customButtons": [
      {
        "id": "copy",
        "label": "COPY",
        "action": { "kind": "combo", "keys": ["CTRL", "C"] }
      }
    ]
  }
}
```

Notes:

- `name` may be `null`.
- `profile` may be `null`; this resets the Android client to local defaults.
- Supported shortcut action kinds are currently `key_tap`, `combo`, `text_insert`, `delay`, and `macro`.

