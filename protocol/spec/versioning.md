# BlueType Protocol Versioning Policy

The current wire contract is protocol version `1`, represented by the
Envelope `v` field and by `protocol-v1.json`.

## Changes allowed within v1

The following changes remain compatible with v1:

- adding optional payload fields;
- adding optional response fields;
- adding a new message type only after all supported clients can safely ignore
  or handle unknown types and the shared manifest and fixtures are updated.

Receivers should ignore unknown payload fields when possible. Existing
required fields, field names, field types, command names, response names, and
error codes must not change silently.

## Breaking changes

A change is breaking when it removes or renames a required field, changes a
field type or meaning, changes framing, changes an existing message type or
error code, or changes session/authentication semantics. Breaking changes
require a new protocol version and a separate compatibility plan.

This repository does not define protocol version `2` yet. The sixth phase
only fixes and verifies the existing v1 contract.

## Unsupported versions

A v1 implementation must not process an Envelope whose `v` value is not `1`.
It should stop processing that message and close or reject the affected
session according to the existing transport/session error path. Introducing a
new version-specific error code is outside the v1 contract.
