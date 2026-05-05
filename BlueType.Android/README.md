# BlueType.Android

Android MVP scaffold for the Key Bridge client.

## Current status

This directory contains the initial project skeleton:

- Compose app shell
- Protocol models
- Connection state model
- Foreground service scaffold
- TCP/Bluetooth session scaffolds

Transport wiring, paired-device loading, authorization flow, and clipboard integration are not finished yet.

## Local prerequisites

- Android Studio with Android SDK installed
- JDK 17 for Gradle sync/build
- Android device or emulator for UI verification

## Notes for this machine

At the time this scaffold was created:

- The shell default `java` resolved to Java 8
- Android Studio was installed at `D:\Program Files\Android\Android Studio`
- Android Studio bundled JBR was available at `D:\Program Files\Android\Android Studio\jbr\bin\java.exe`
- The bundled JBR version was OpenJDK 21
- `gradle` and `adb` were not exposed on the shell `PATH`

So the project files were created manually, and command-line build verification still depends on wiring the shell to the Android Studio toolchain or opening the project in Android Studio.
