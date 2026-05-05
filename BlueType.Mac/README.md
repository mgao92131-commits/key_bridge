# BlueType.Mac

Swift macOS 14+ menu bar agent for the existing BlueType Android client.

## Run

```sh
cd BlueType.Mac
swift run BlueTypeMac
```

The agent listens on TCP port `24862` and publishes the BlueType RFCOMM service UUID
`5F8C2C1D-9A25-4A20-9F0B-30D8D0F7E913`.

Use the Android app's Wi-Fi manual connection dialog with one of the IP addresses shown in the
BlueType menu bar item, or pair the Android phone with the Mac and use the Bluetooth target.

## Permissions

macOS must grant Accessibility/Input Monitoring permission before remote key and mouse injection
can work. The menu includes an entry that opens the Accessibility settings pane.

Trusted Android devices are stored at:

```text
~/Library/Application Support/BlueType/devices.json
```

## Verify

```sh
cd BlueType.Mac
swift test
```
