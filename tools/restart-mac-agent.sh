#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: tools/restart-mac-agent.sh [--release] [--reset-accessibility] [--sign-identity IDENTITY|--adhoc]

Builds BlueTypeMac, refreshes BlueType.Mac/BlueTypeMac.app, re-signs it with
the stable bundle identifier, restarts the app, and verifies TCP port 24862.

Options:
  --release               Build the release product instead of debug.
  --reset-accessibility   Reset macOS Accessibility and Bluetooth approval for
                          com.bluetype.macagent. Use this when logs show
                          INPUT_BLOCKED, Bluetooth permission drift, or stale
                          TCC state after changing/re-signing the app.
  --sign-identity VALUE   Code-sign with the provided identity name or hash.
                          Defaults to the first available Apple Development
                          identity, falling back to ad-hoc when none exists.
  --adhoc                 Force ad-hoc signing. This is less stable for TCC
                          permissions after rebuilds.
EOF
}

BUILD_CONFIGURATION="debug"
RESET_ACCESSIBILITY=0
SIGN_IDENTITY=""
FORCE_ADHOC=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --release)
            BUILD_CONFIGURATION="release"
            shift
            ;;
        --reset-accessibility)
            RESET_ACCESSIBILITY=1
            shift
            ;;
        --sign-identity)
            if [[ $# -lt 2 ]]; then
                echo "--sign-identity requires a value." >&2
                exit 2
            fi
            SIGN_IDENTITY="$2"
            shift 2
            ;;
        --adhoc)
            FORCE_ADHOC=1
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
MAC_DIR="$REPO_ROOT/BlueType.Mac"
APP_NAME="BlueTypeMac"
APP_BUNDLE="$MAC_DIR/$APP_NAME.app"
INSTALLED_APP_BUNDLE="/Applications/$APP_NAME.app"
CONTENTS_DIR="$APP_BUNDLE/Contents"
EXECUTABLE="$CONTENTS_DIR/MacOS/$APP_NAME"
PLIST_SOURCE="$MAC_DIR/Resources/Info.plist"
PLIST_TARGET="$CONTENTS_DIR/Info.plist"
ICON_SOURCE="$MAC_DIR/Resources/AppIcon.icns"
ICON_TARGET="$CONTENTS_DIR/Resources/AppIcon.icns"
ENTITLEMENTS_SOURCE="$MAC_DIR/Resources/BlueTypeMac.entitlements"
PORT="24862"
BUNDLE_ID="com.bluetype.macagent"

choose_sign_identity() {
    if [[ "$FORCE_ADHOC" -eq 1 ]]; then
        echo "-"
        return
    fi

    if [[ -n "$SIGN_IDENTITY" ]]; then
        echo "$SIGN_IDENTITY"
        return
    fi

    local identity
    identity="$(security find-identity -v -p codesigning 2>/dev/null \
        | awk -F'"' '/Apple Development:/{print $2; exit}')"
    if [[ -n "$identity" ]]; then
        echo "$identity"
    else
        echo "-"
    fi
}

cd "$MAC_DIR"

echo "Building $APP_NAME ($BUILD_CONFIGURATION)..."
if [[ "$BUILD_CONFIGURATION" == "release" ]]; then
    swift build -c release --product "$APP_NAME"
    BUILT_EXECUTABLE="$MAC_DIR/.build/release/$APP_NAME"
else
    swift build --product "$APP_NAME"
    BUILT_EXECUTABLE="$MAC_DIR/.build/debug/$APP_NAME"
fi

if [[ ! -x "$BUILT_EXECUTABLE" ]]; then
    echo "Build did not produce executable: $BUILT_EXECUTABLE" >&2
    exit 1
fi

echo "Refreshing app bundle..."
mkdir -p "$CONTENTS_DIR/MacOS" "$CONTENTS_DIR/Resources"
cp "$BUILT_EXECUTABLE" "$EXECUTABLE"
chmod +x "$EXECUTABLE"

if [[ -f "$PLIST_SOURCE" ]]; then
    cp "$PLIST_SOURCE" "$PLIST_TARGET"
fi

if [[ -f "$ICON_SOURCE" ]]; then
    cp "$ICON_SOURCE" "$ICON_TARGET"
else
    echo "Warning: app icon not found at $ICON_SOURCE; bundle will use the default app icon." >&2
fi

SIGN_CHOICE="$(choose_sign_identity)"
if [[ "$SIGN_CHOICE" == "-" ]]; then
    echo "Signing app bundle as $BUNDLE_ID with ad-hoc identity..."
    echo "Warning: ad-hoc signing can make Accessibility permission stale after rebuilds." >&2
    TIMESTAMP_OPTION="--timestamp=none"
else
    echo "Signing app bundle as $BUNDLE_ID with identity: $SIGN_CHOICE"
    TIMESTAMP_OPTION="--timestamp"
fi
if [[ -f "$ENTITLEMENTS_SOURCE" ]]; then
    codesign --force --deep --options runtime "$TIMESTAMP_OPTION" --entitlements "$ENTITLEMENTS_SOURCE" --sign "$SIGN_CHOICE" "$APP_BUNDLE"
else
    echo "Warning: entitlements file not found at $ENTITLEMENTS_SOURCE; signing without entitlements." >&2
    codesign --force --deep --options runtime "$TIMESTAMP_OPTION" --sign "$SIGN_CHOICE" "$APP_BUNDLE"
fi

SIGNED_ID="$(codesign -dv --verbose=4 "$APP_BUNDLE" 2>&1 | awk -F= '/^Identifier=/{value=$2} END{print value}')"
if [[ "$SIGNED_ID" != "$BUNDLE_ID" ]]; then
    echo "Unexpected codesign identifier: ${SIGNED_ID:-<missing>}" >&2
    exit 1
fi

echo "Installing signed app bundle to $INSTALLED_APP_BUNDLE..."
rm -rf "$INSTALLED_APP_BUNDLE"
ditto "$APP_BUNDLE" "$INSTALLED_APP_BUNDLE"

INSTALLED_SIGNED_ID="$(codesign -dv --verbose=4 "$INSTALLED_APP_BUNDLE" 2>&1 | awk -F= '/^Identifier=/{value=$2} END{print value}')"
if [[ "$INSTALLED_SIGNED_ID" != "$BUNDLE_ID" ]]; then
    echo "Unexpected installed codesign identifier: ${INSTALLED_SIGNED_ID:-<missing>}" >&2
    exit 1
fi

if [[ "$RESET_ACCESSIBILITY" -eq 1 ]]; then
    echo "Resetting permissions for $BUNDLE_ID..."
    tccutil reset Accessibility "$BUNDLE_ID" || true
    tccutil reset Bluetooth "$BUNDLE_ID" || true
    tccutil reset BluetoothAlways "$BUNDLE_ID" || true
fi

echo "Stopping existing $APP_NAME processes..."
pkill -x "$APP_NAME" 2>/dev/null || true
sleep 1

echo "Starting $INSTALLED_APP_BUNDLE..."
open "$INSTALLED_APP_BUNDLE"
sleep 2

echo "Verifying process..."
pgrep -fl "$APP_NAME" || {
    echo "$APP_NAME is not running." >&2
    exit 1
}

echo "Verifying TCP port $PORT..."
if lsof -nP -iTCP:"$PORT" -sTCP:LISTEN >/dev/null 2>&1; then
    lsof -nP -iTCP:"$PORT" -sTCP:LISTEN
else
    echo "$APP_NAME is running, but TCP port $PORT is not listening yet." >&2
    exit 1
fi

echo
if [[ "$RESET_ACCESSIBILITY" -eq 1 ]]; then
    echo "Permissions were reset. Please:"
    echo "1. Re-enable BlueType Mac Agent in: System Settings > Privacy & Security > Accessibility"
    echo "2. When the app starts, grant Bluetooth permission when prompted."
    echo
    echo "If an old Accessibility entry is still listed, remove it with the minus button,"
    echo "add this exact app bundle, then enable it:"
    echo "  $INSTALLED_APP_BUNDLE"
    open 'x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility'
else
    echo "If Android connects but input is blocked or Bluetooth permission looks stale, run:"
    echo "  tools/restart-mac-agent.sh --reset-accessibility"
fi

echo "Done."
