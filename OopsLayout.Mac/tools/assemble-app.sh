#!/bin/bash
# Assembles OopsLayout.app around an already-built binary: lays out the bundle,
# writes Info.plist, copies the icon, and code-signs. Shared by build-app.sh
# (dev) and build-dmg.sh (release).
#
#   assemble-app.sh <binary-path> <sign-identity>
#
# <sign-identity> is a codesign identity name, or "-" for ad-hoc.
set -euo pipefail
cd "$(dirname "$0")/.."

BIN_PATH="$1"
SIGN_ID="$2"

APP_NAME="OopsLayout"
BUNDLE_ID="com.oopslayout.app"
APP_DIR="${APP_NAME}.app"
# Single source of truth for the version (build-dmg.sh bumps it each release).
VERSION="$(cat VERSION 2>/dev/null || echo 0.0.0)"

rm -rf "${APP_DIR}"
mkdir -p "${APP_DIR}/Contents/MacOS" "${APP_DIR}/Contents/Resources"

cp "${BIN_PATH}" "${APP_DIR}/Contents/MacOS/${APP_NAME}"

if [ -f "Resources/AppIcon.icns" ]; then
    cp "Resources/AppIcon.icns" "${APP_DIR}/Contents/Resources/AppIcon.icns"
fi

cat > "${APP_DIR}/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>${APP_NAME}</string>
    <key>CFBundleDisplayName</key>
    <string>${APP_NAME}</string>
    <key>CFBundleExecutable</key>
    <string>${APP_NAME}</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundleIdentifier</key>
    <string>${BUNDLE_ID}</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>LSUIElement</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
    <key>NSHumanReadableCopyright</key>
    <string>MIT License</string>
</dict>
</plist>
PLIST

echo "==> codesign (${SIGN_ID})"
codesign --force --deep --sign "${SIGN_ID}" "${APP_DIR}"
