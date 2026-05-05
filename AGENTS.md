# BlueType Agent Notes

## Project

BlueType is a cross-device input bridge MVP.

- `BlueType.Agent`: Windows tray agent for Bluetooth RFCOMM, Wi-Fi TCP, device authorization, and input injection.
- `BlueType.Mac`: macOS menu bar agent for Bluetooth RFCOMM, Wi-Fi TCP, device authorization, and input injection.
- `BlueType.Android`: Android client for discovery, connection, authentication, remote input, and clipboard commands.
- `BlueType.TestClient`: local protocol and transport test client.
- `BlueType.Protocol`: shared protocol helpers for .NET components.

## Entry Points

- Solution: `BlueType.sln`
- Windows agent startup: `BlueType.Agent/Program.cs`
- Mac agent package: `BlueType.Mac/Package.swift`
- Mac agent startup: `BlueType.Mac/Sources/BlueTypeMac/main.swift`
- Android app module: `BlueType.Android/app`

## Protocol

- Transports: Bluetooth RFCOMM and Wi-Fi TCP on port `24862`.
- Session flow: client connects, sends `hello`, desktop agent authorizes the device, then accepts input and clipboard commands.
- Only one active client session is allowed at a time.
- Android sends periodic `ping`; desktop agents also send heartbeat `ping` and drop silent sessions after timeout.
- Keep protocol changes aligned across desktop agents and Android.

## Common Commands

- Build full .NET solution: `dotnet build BlueType.sln`
- Build Windows agent: `dotnet build BlueType.Agent/BlueType.Agent.csproj`
- View Windows logs: `Get-Content "$env:APPDATA\BlueType\logs\$(Get-Date -Format yyyy-MM-dd).log" -Tail 200`
- Build Android debug APK: `cd BlueType.Android && ./gradlew :app:assembleDebug`
- Install Android debug APK: `adb install -r BlueType.Android/app/build/outputs/apk/debug/app-debug.apk`
- Watch Android logs: `adb logcat | grep BlueType`
- Build, sign, and restart Mac agent: `./tools/restart-mac-agent.sh`
- Rebuild Mac agent and reset macOS Accessibility approval: `./tools/restart-mac-agent.sh --reset-accessibility`
- View Mac logs: `tail -n 200 "$HOME/Library/Application Support/BlueType/logs/mac-agent.log"`
- Verify Mac TCP listener: `lsof -nP -iTCP:24862 -sTCP:LISTEN`

## Mac Agent Permission Recovery

- Prefer `./tools/restart-mac-agent.sh --reset-accessibility` after changing Mac agent code. Use `./tools/restart-mac-agent.sh` only for restarts where the executable did not change.
- Do not run `swift run BlueTypeMac` for the menu bar app; use the script so `BlueTypeMac.app` is refreshed and re-signed as `com.bluetype.macagent`.
- If Android connects but input commands fail with `INPUT_BLOCKED`, run `./tools/restart-mac-agent.sh --reset-accessibility`, then re-enable `BlueType Mac Agent` in System Settings > Privacy & Security > Accessibility.
- If the Accessibility entry is already enabled but input still fails, toggle it off and on once, then disconnect and reconnect Android.

## Working Guidance

- Prefer fixing transport and session-state issues on both desktop and Android when a bug spans both sides.
- When Bluetooth connection fails before desktop logs anything, inspect Android socket creation and fallback logic first.
- For Mac input problems, first check `mac-agent.log`: if commands are received but fail with `INPUT_BLOCKED`, recover permissions; if commands are not received, inspect Android gesture dispatch or transport state.
- Android build requires Java 11 or newer; set `JAVA_HOME` if Gradle picks an older Java.
