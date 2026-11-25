#!/bin/bash
# Regenerates Resources/AppIcon.icns from the shared Windows icon
# (../OopsLayout.Windows/Resources/icon.ico). Run after the source icon changes.
set -euo pipefail
cd "$(dirname "$0")/.."

SRC="../OopsLayout.Windows/Resources/icon.ico"
WORK="$(mktemp -d)"
ICONSET="${WORK}/AppIcon.iconset"
mkdir -p "${ICONSET}"

# Largest frame out of the .ico (256x256).
sips -s format png "${SRC}" --out "${WORK}/base.png" >/dev/null

gen() { sips -z "$2" "$2" "${WORK}/base.png" --out "${ICONSET}/$1" >/dev/null; }
gen icon_16x16.png 16
gen icon_16x16@2x.png 32
gen icon_32x32.png 32
gen icon_32x32@2x.png 64
gen icon_128x128.png 128
gen icon_128x128@2x.png 256
gen icon_256x256.png 256
gen icon_256x256@2x.png 512   # upscaled (source maxes at 256)
gen icon_512x512.png 512      # upscaled
gen icon_512x512@2x.png 1024  # upscaled

mkdir -p Resources
iconutil -c icns "${ICONSET}" -o Resources/AppIcon.icns
rm -rf "${WORK}"
echo "wrote Resources/AppIcon.icns"
