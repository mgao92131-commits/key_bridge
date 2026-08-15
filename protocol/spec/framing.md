# BlueType Protocol v1 Framing

Each wire message is transmitted as one frame:

1. A 4-byte big-endian signed length prefix.
2. A UTF-8 encoded JSON envelope with exactly that many bytes.

The length is the JSON payload length and does not include the 4-byte prefix.
Valid payload lengths are `1` through `65536` bytes inclusive. A zero,
negative, oversized, or truncated payload must be rejected. A clean end of
stream before the next length prefix is reported as no next frame.

The shared `frames/*.json` fixtures contain a canonical JSON string and its
complete frame as lowercase hexadecimal. They are used to verify decode
behavior and the length prefix on Windows, Android, and Mac. Encoders may
emit JSON object properties in a different order; compatibility tests compare
the decoded JSON envelope semantically rather than requiring object key order
to match.

The JSON envelope and its payload rules are defined separately under
`schema/`; framing does not change the v1 message contract.
