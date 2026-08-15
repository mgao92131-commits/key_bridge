# BlueType Protocol v1 Inventory

This document is the explicit inventory of the BlueType v1 wire contract.
The machine-readable counterpart is `protocol-v1.json`.

## Envelope

Every message is a JSON envelope with these fields:

| Field | Required | Shape | Notes |
| --- | --- | --- | --- |
| `v` | yes | integer | Current protocol version is `1`. |
| `id` | yes | non-empty string | Request, response, or event identifier. |
| `type` | yes | non-empty string | One of the types listed below. |
| `token` | no | string | Authorization token after a trusted handshake. |
| `payload` | yes | object | Message-specific fields. |

## Client commands

| Type | Payload |
| --- | --- |
| `hello` | `deviceId`, `deviceName`, optional `appVersion` |
| `text_insert` | `text` |
| `key_tap` | `key` |
| `key_down` | `key` |
| `key_up` | `key` |
| `combo` | `keys` |
| `mouse_move` | optional/relative `dx`, `dy` |
| `mouse_button` | `button`, `action` (`down` or `up`) |
| `mouse_click` | `button`, optional `repeat` |
| `mouse_scroll` | optional `deltaX`, `deltaY` |
| `clipboard_set` | `text` |
| `clipboard_get` | empty object |
| `ping` | empty object |

`ping` is listed as a command because either peer may send it as a heartbeat
message. The peer replies with `pong` using the same `id`.

## Agent responses and events

| Type | Payload |
| --- | --- |
| `pong` | empty object |
| `ack` | `ok` |
| `error` | `code`, `message` |
| `auth_pending` | `timeoutSec`, `message` |
| `auth_result` | `ok`, optional `token`, `persistToken`, `trusted` |
| `clipboard_value` | `text` |
| `shortcut_profile` | profile name and optional profile object |

## Standard error codes

| Code | Meaning |
| --- | --- |
| `BUSY` | Another device owns the active session or takeover was denied. |
| `NOT_AUTHORIZED` | Hello authorization has not completed or the token is invalid. |
| `AUTH_TIMEOUT` | The authorization prompt timed out. |
| `AUTH_UI_UNAVAILABLE` | The desktop could not show the authorization prompt. |
| `INVALID_PAYLOAD` | The message type or payload is invalid. |
| `SERVER_ERROR` | The desktop encountered an unexpected command failure. |
| `SESSION_REPLACED` | Another connection replaced the active session. |
| `INPUT_BLOCKED` | Desktop input permissions prevent input injection. |
| `CLIPBOARD_FAILED` | Desktop clipboard synchronization failed. |

## Fixture coverage requirement

The shared specification must contain at least one valid fixture for every
command type, every response/event type, and every standard error code. A
fixture with an `error` type counts toward error-code coverage only when its
payload contains that code.

Fixtures are examples of the wire contract; they do not replace the message
and session-flow rules in `messages.md`, `errors.md`, and `session-flow.md`.
