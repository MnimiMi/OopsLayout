#!/bin/bash
# Builds a distributable OopsLayout-<version>.dmg containing a UNIVERSAL
# (arm64 + x86_64) build — runs on every Apple Silicon and Intel Mac.
#
#   ./build-dmg.sh
#
# Universal builds need both slices. With full Xcode active, SwiftPM builds both
# in one go (--arch). With Command Line Tools only, we build each slice
# separately and join them with `lipo` (Apple's standard universal-binary tool —
# the exact same join Xcode does internally). The script auto-detects which path
# to take, so it works either way.
#
# The release .app is signed ad-hoc by default (no personal cert shipped). Set
# OOPS_SIGN_ID to a Developer ID to sign properly (and notarize separately).
set -euo pipefail
cd "$(dirname "$0")"

APP_NAME="OopsLayout"

# Auto-bump the patch version on every DMG build so partners never confuse two
# builds with the same number. VERSION is the single source of truth (read by
# assemble-app.sh too); we increment and write it back here.
CUR="$(cat VERSION 2>/dev/null || echo 0.0.0)"
IFS=. read -r MA MI PA <<< "${CUR}"
VERSION="${MA}.${MI}.$(( PA + 1 ))"
echo "${VERSION}" > VERSION
echo "==> version ${CUR} -> ${VERSION}"

DMG="${APP_NAME}-${VERSION}.dmg"

# Code signature. Accessibility grants only survive across versions if the app's
# signing identity is STABLE — ad-hoc changes the identity every build, so the
# partner's machine sees each new DMG as a different app, re-asks (or fails to
# re-ask), and the old grant leaves the switcher dead. Prefer the local
# self-signed identity from tools/make-signing-cert.sh; fall back to ad-hoc only
# if it's missing. Override with OOPS_SIGN_ID (e.g. a Developer ID for release).
SIGN_ID="${OOPS_SIGN_ID:-}"
if [ -z "${SIGN_ID}" ]; then
    if security find-identity -p codesigning 2>/dev/null | grep -q "OopsLayout Local"; then
        SIGN_ID="OopsLayout Local"
    else
        SIGN_ID="-"
    fi
fi

DEVELOPER_DIR="$(xcode-select -p 2>/dev/null || true)"

if [[ "${DEVELOPER_DIR}" == *"Xcode.app"* ]]; then
    echo "==> universal build via Xcode build system (--arch arm64 --arch x86_64)"
    swift build -c release --arch arm64 --arch x86_64
    UNIVERSAL_BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/${APP_NAME}"
else
    echo "==> Command Line Tools only — building each slice and joining with lipo"

    echo "    [1/3] arm64 slice"
    swift build -c release --scratch-path .build-arm64 \
        -Xswiftc -target -Xswiftc arm64-apple-macosx12.0 \
        -Xcc -target -Xcc arm64-apple-macosx12.0 >/dev/null

    echo "    [2/3] x86_64 slice"
    swift build -c release --scratch-path .build-x86_64 \
        -Xswiftc -target -Xswiftc x86_64-apple-macosx12.0 \
        -Xcc -target -Xcc x86_64-apple-macosx12.0 >/dev/null

    echo "    [3/3] lipo -> universal"
    UNIVERSAL_BIN="$(mktemp -d)/${APP_NAME}"
    lipo -create \
        ".build-arm64/release/${APP_NAME}" \
        ".build-x86_64/release/${APP_NAME}" \
        -output "${UNIVERSAL_BIN}"
fi

echo "==> binary architectures: $(lipo -archs "${UNIVERSAL_BIN}")"

echo "==> assembling ${APP_NAME}.app"
./tools/assemble-app.sh "${UNIVERSAL_BIN}" "${SIGN_ID}"

echo "==> building ${DMG}"
STAGE="$(mktemp -d)"
cp -R "${APP_NAME}.app" "${STAGE}/"
ln -s /Applications "${STAGE}/Applications"   # drag-to-install target

# A short "how to open" note next to the app, since the app isn't notarized and
# macOS blocks the first launch behind Gatekeeper.
cat > "${STAGE}/HOW TO OPEN — КАК ОТКРЫТЬ.txt" <<'NOTE'
OopsLayout — first launch
=========================

1. Drag OopsLayout into the Applications folder.
2. Double-click it. macOS will say it "can't be opened" — click OK.
3. Open  System Settings → Privacy & Security , scroll down, and click
   "Open Anyway" next to the OopsLayout message. Confirm with Touch ID/password.
4. When asked, enable OopsLayout under Privacy & Security → Accessibility.

This one-time step is normal: the app is free and open-source, so it is signed
but not notarized by Apple. Nothing is wrong with the download.

Power-user shortcut (Terminal):
    xattr -dr com.apple.quarantine /Applications/OopsLayout.app


OopsLayout — первый запуск
==========================

1. Перетащите OopsLayout в папку Applications (Программы).
2. Двойной клик. macOS скажет «не удаётся открыть» — нажмите OK.
3. Откройте  Системные настройки → Конфиденциальность и безопасность ,
   прокрутите вниз и нажмите «Открыть всё равно» рядом с сообщением про
   OopsLayout. Подтвердите Touch ID / паролем.
4. Когда попросит — включите OopsLayout в разделе Универсальный доступ.

Это разовый шаг и это нормально: программа бесплатная и с открытым исходным
кодом, поэтому подписана, но не нотаризована Apple. С загрузкой всё в порядке.

Через Терминал (для продвинутых):
    xattr -dr com.apple.quarantine /Applications/OopsLayout.app
NOTE

rm -f "${DMG}"
hdiutil create -volname "${APP_NAME}" -srcfolder "${STAGE}" -ov -format UDZO "${DMG}" >/dev/null
rm -rf "${STAGE}"

echo
echo "==> done: ${DMG}  ($(du -h "${DMG}" | cut -f1))"
echo "    Universal app, signed: ${SIGN_ID}"
echo "    Upload this to a GitHub Release."
