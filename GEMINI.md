# GEMINI.md - BlueType (Key Bridge) Project Context

## Project Overview
BlueType (Key Bridge) is a cross-platform input bridge that allows an Android device to act as a remote keyboard, mouse, and clipboard controller for a Windows PC or a Mac.

### Architecture
- **Windows Agent (`BlueType.Agent`)**: A .NET 10 WinForms application that lives in the system tray. It hosts a TCP server (port 24862) and a Bluetooth RFCOMM server to receive input commands.
- **Mac Agent (`BlueType.Mac`)**: A Swift macOS 14+ application that lives in the menu bar. It also hosts a TCP server (port 24862) and a Bluetooth RFCOMM server.
- **Android Client (`BlueType.Android`)**: A Jetpack Compose application that connects to the Agents. It features a trackpad, keyboard controls, and clipboard sync.
- **Protocol**: Custom JSON-based protocol over stateful sockets. Supports `hello`, `auth`, `ping`, `input_stroke`, `mouse_move`, `mouse_click`, `mouse_scroll`, and `clipboard_sync`.

### Visual Style: Modern Technical Minimalism
The project adheres to a "Modern Technical Minimalism" design philosophy:
- **Core Aesthetic**: Atmospheric, high-end editorial layout using tonal layering instead of hard borders.
- **Colors**: Deep Space Gray (`#1B1B1F`), Surface (`#1F1F23`), and Lavender Purple (`#C7BDF0`) as the primary accent.
- **Dark Mode**: Forced system-wide dark mode for both Android and Windows to maintain a premium, focused tool feel.

---

## Building and Running

### Windows Agent
- **Target Framework**: .NET 10 (windows10.0.19041)
- **Build**: `dotnet build BlueType.Agent/BlueType.Agent.csproj`
- **Run**: `.\BlueType.Agent\bin\Debug\net10.0-windows10.0.19041\BlueType.Agent.exe`
- **Logs**: Located in `%APPDATA%\BlueType\logs\`

### Android Client
- **Prerequisites**: JDK 17+ (JDK 21/23 recommended for this machine).
- **Environment Setup** (PowerShell):
  ```powershell
  $env:JAVA_HOME = 'C:\Users\Administrator.DESKTOP-F9T4GKP\.jdks\openjdk-23.0.1'
  $env:Path = "$env:JAVA_HOME\bin;$env:Path"
  ```
- **Build APK**: `cd BlueType.Android; .\gradlew.bat :app:assembleDebug`
- **Install**: `adb install -r .\app\build\outputs\apk\debug\app-debug.apk`

### Mac Agent
- **Target Platform**: macOS 14+ (Swift 6.0)
- **Build & Run**: `./tools/restart-mac-agent.sh` (preferred for signing and permissions)
- **Logs**: `tail -f "$HOME/Library/Application Support/BlueType/logs/mac-agent.log"`

---

## Development Conventions

### Coding Style
- **Windows**: Modern C# 13+ conventions. Use `internal sealed` for implementation classes. Follow WinForms tray app patterns.
- **Mac**: Modern Swift 6+ with structured concurrency. Use `MacAgent` and `AppState` for core logic.
- **Android**: Modern Kotlin with Jetpack Compose. Use `StateFlow` and `ViewModel` for state management. Adhere to Material 3 design tokens.
- **Shared Standards**:
    - **No-Line Rule**: Avoid 1px borders; use color shifts for sectioning.
    - **Dark Mode First**: Do not implement light mode logic. Hardcode dark theme assets.

### Protocols & Communication
- Protocol definitions are mirrored between `BlueType.Agent/Protocol/` and `BlueType.Android/.../bluetooth/Commands.kt`.
- All commands are wrapped in an `Envelope` structure.
- Bluetooth Service UUID: `5F8C2C1D-9A25-4A20-9F0B-30D8D0F7E913`

### Verification Workflow
1.  **Transport Check**: Verify TCP/Bluetooth connection before testing UI.
2.  **Auth Flow**: Ensure the Windows Agent displays the authorization prompt for new devices.
3.  **Input Injection**: Use the `tools/InputProbe.ps1` or the `Local input test` section in the Agent Settings to verify Win32 `SendInput` behavior.

---

## Important Files
- `DESIGN.md`: Deep dive into the visual design system.
- `AGENTS.md`: Practical notes on build environments and local paths.
- `BlueType.Agent/Core/ThemeHelper.cs`: Global styling logic for Windows forms.
- `BlueType.Android/app/src/main/java/com/bluetype/android/ui/theme/BlueTypeTheme.kt`: Main theme entry for Android.
