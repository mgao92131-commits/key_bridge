# BlueType Protocol Errors

Errors are sent as `error` envelopes:

```json
{
  "v": 1,
  "id": "request-id",
  "type": "error",
  "payload": {
    "code": "INVALID_PAYLOAD",
    "message": "Human-readable explanation."
  }
}
```

## Standard Error Codes

### `BUSY`

Another device is already controlling the desktop, or the incoming takeover was denied.

Client expectation:

- Show a clear message.
- Do not retry automatically until the user explicitly reconnects.

### `NOT_AUTHORIZED`

The client has not completed `hello` authorization or supplied an invalid/stale token.

Client expectation:

- If received during `hello`, clear saved token and require approval again.
- If received after connection, disconnect and require a new handshake.

### `AUTH_TIMEOUT`

The desktop approval prompt timed out before user approval.

Client expectation:

- Show a retryable error.
- Do not keep the stale pending connection.

### `AUTH_UI_UNAVAILABLE`

The desktop could not show the authorization prompt.

Client expectation:

- Surface that approval is unavailable on the desktop.
- Allow user retry.

### `INVALID_PAYLOAD`

The payload shape, value type, value range, or command type is invalid.

Client expectation:

- Treat as a client bug or protocol mismatch.
- Do not retry the same command unchanged.

### `SERVER_ERROR`

The desktop hit an unexpected failure while routing or handling a command.

Client expectation:

- Keep the connection unless another session error follows.
- Report the command failure.

### `SESSION_REPLACED`

The connection is no longer the active control session because another session replaced it.

Client expectation:

- Stop sending commands on this connection.
- Close the transport and update UI state.

### `INPUT_BLOCKED`

The desktop received the command but could not inject input because platform permissions block it. This is currently used by the macOS agent when Accessibility/Input Monitoring permission is missing.

Client expectation:

- Show a desktop-side permission error.
- Keep the session available for retry after the desktop permission is fixed.

### `CLIPBOARD_FAILED`

Clipboard synchronization failed on the desktop.

Client expectation:

- Report clipboard failure for the request.
- Keep the connection unless another session error follows.

## Compatibility Notes

- Clients must handle unknown error codes by showing the code or message.
- Servers should preserve the triggering request `id` in the error envelope.
- `message` is intended for UI/logs and should not be parsed for behavior.

