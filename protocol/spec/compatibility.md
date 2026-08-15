# BlueType Cross-Platform Compatibility Contract

`protocol/spec` is the single source of truth for the Windows Agent, Android
client, and Mac Agent. Platform code may use native models and serializers,
but it must validate against the same manifest, schemas, and fixtures.

## Contract matrix

| Contract | Shared source | Windows | Android | Mac |
| --- | --- | --- | --- | --- |
| Envelope v1 | `schema/envelope.schema.json` | `ProtocolExamplesTests` | `ProtocolExamplesTest` | `ProtocolExamplesTests` |
| Command and response types | `protocol-v1.json` | C# constant contract test | `MsgType` manifest test | `MessageType.all` manifest test |
| Error codes | `protocol-v1.json` and `errors.md` | valid fixture test | valid fixture test | valid fixture test |
| Valid message fixtures | `examples/*.json` | frame round-trip test | frame round-trip test | frame round-trip test |
| Invalid message fixtures | `invalid/*.json` | v1 contract rejection test | v1 contract rejection test | v1 contract rejection test |
| Framing | `framing.md`, `frames/*.json` | framing fixture tests | framing fixture tests | framing fixture tests |

The matrix describes required enforcement points; it is not a manually
maintained pass/fail status report. CI test results are the authoritative
status.

## Drift rule

Adding or changing a command, response, error code, payload field, or framing
rule requires updating the shared specification and the affected fixtures in
the same change. Each platform's contract test must then fail until its native
constants or parser behavior is brought back into agreement.

## CI expectation

The repository's CI should run all three contract suites:

- the .NET protocol/Agent tests;
- Android JVM unit tests;
- Mac SwiftPM tests.

The sixth phase does not add code generation. Native implementations remain
independent while their wire behavior is checked against this shared contract.
