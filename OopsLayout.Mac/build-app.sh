#!/bin/bash
# Builds OopsLayout.app for local use — a native (this-Mac arch) release build.
# No Xcode required, only the Swift toolchain + Command Line Tools.
#
#   ./build-app.sh            # release build into ./OopsLayout.app
#
# For a distributable universal (arm64 + x86_64) DMG, use ./build-dmg.sh instead.
set -euo pipefail
cd "$(dirname "$0")"
APP_NAME="OopsLayout"

echo "==> swift build -c release"
swift build -c release
BIN_PATH="$(swift build -c release --show-bin-path)/${APP_NAME}"

# Code signature. A *stable* identity is what lets the Accessibility permission
# survive rebuilds — ad-hoc signing changes the binary's identity every build, so
# macOS forgets the grant and re-asks each time. Prefer a local self-signed
# identity (created by tools/make-signing-cert.sh) if one exists.
SIGN_ID="${OOPS_SIGN_ID:-}"
if [ -z "${SIGN_ID}" ]; then
    # Note: no -v. A self-signed cert is untrusted (CSSMERR_TP_NOT_TRUSTED) so it
    # won't show under -v, but codesign signs with it fine — and a stable
    # signature is all TCC needs to remember the Accessibility grant.
    if security find-identity -p codesigning 2>/dev/null | grep -q "OopsLayout Local"; then
        SIGN_ID="OopsLayout Local"
    else
        SIGN_ID="-"
    fi
fi

echo "==> assembling ${APP_NAME}.app"
./tools/assemble-app.sh "${BIN_PATH}" "${SIGN_ID}"

echo "==> done: ${APP_NAME}.app"
echo "Run it with:  open ${APP_NAME}.app"
if [ "${SIGN_ID}" = "-" ]; then
    echo
    echo "NOTE: signed ad-hoc — macOS will re-ask for Accessibility after every rebuild."
    echo "      Run ./tools/make-signing-cert.sh once for a stable identity that sticks."
fi
