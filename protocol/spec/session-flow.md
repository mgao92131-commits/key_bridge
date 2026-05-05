# BlueType Session Flow

## Transport

BlueType supports two transports:

- Bluetooth RFCOMM
- Wi-Fi TCP on port `24862`

Both transports carry the same framed JSON protocol.

## First-Time Authorization

1. Client opens TCP or Bluetooth transport.
2. Client sends `hello` without a token.
3. Desktop validates the hello payload.
4. Desktop sends `auth_pending`.
5. Desktop shows an authorization prompt.
6. If the user approves once, desktop sends `auth_result` without a persistent token.
7. If the user approves and trusts the device, desktop sends `auth_result` with `token`, `persistToken: true`, and `trusted: true`.
8. Client enters connected state.
9. Client may send input and clipboard commands.

## Known Device Fast Path

1. Client opens TCP or Bluetooth transport.
2. Client sends `hello` with the saved token in the envelope `token` field.
3. Desktop validates `deviceId` and token.
4. Desktop sends `auth_result`.
5. Client enters connected state.

## Authorization Denied

1. Client sends `hello`.
2. Desktop sends `auth_pending`.
3. User denies the prompt.
4. Desktop sends `error` with `NOT_AUTHORIZED`.
5. Desktop closes or stops processing the attempted session.
6. Client clears any stale token for that desktop.

## Authorization Timeout

1. Client sends `hello`.
2. Desktop sends `auth_pending`.
3. User does not approve before timeout.
4. Desktop sends `error` with `AUTH_TIMEOUT`.
5. Session closes.

## Busy Or Takeover Denied

1. A client completes authorization while another session is active.
2. Desktop may prompt to switch active device.
3. If switch is denied, desktop sends `error` with `BUSY`.
4. Incoming session closes.

## Session Replaced

1. A new authorized session replaces the current active session.
2. The old session may receive `error` with `SESSION_REPLACED` on the next command.
3. The old client must stop sending commands and close its connection.

## Command Handling

1. Client sends an authorized command with a request `id`.
2. Desktop routes the command.
3. On success, desktop replies with:
   - `ack` for input and `clipboard_set`
   - `clipboard_value` for `clipboard_get`
4. On failure, desktop replies with `error` using the same request `id`.

## Heartbeat

1. Client sends periodic `ping`.
2. Desktop replies with `pong` using the same request `id`.
3. Desktop may also send periodic `ping`.
4. Client replies with `pong`.
5. If either side stops receiving inbound frames for the timeout window, it closes the session.

## Duplicate Hello

1. Client sends `hello`.
2. Authorization completes.
3. Client sends another `hello` on the same connection.
4. Desktop replies with `error` code `INVALID_PAYLOAD`.

## Unauthorized Command

1. Client opens transport.
2. Client sends an input or clipboard command before successful `hello`.
3. Desktop replies with `error` code `NOT_AUTHORIZED`.

## Shortcut Profile Event

1. Client is connected.
2. Desktop observes foreground app context.
3. Desktop sends `shortcut_profile` when the matched profile changes.
4. Client updates remote shortcut UI.
5. Desktop may send `profile: null` to reset client shortcuts to local defaults.

