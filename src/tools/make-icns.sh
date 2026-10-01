#!/bin/bash
# Build a .icns from the sextant mark. iconutil wants an iconset of exact sizes.
set -euo pipefail

png="$1"
out="$2"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
iconset="$tmp/sextant.iconset"
mkdir -p "$iconset" "$(dirname "$out")"

write() {
  local size="$1"
  local name="$2"
  sips -z "$size" "$size" "$png" --out "$iconset/$name" >/dev/null
}

write 16 icon_16x16.png
write 32 icon_16x16@2x.png
write 32 icon_32x32.png
write 64 icon_32x32@2x.png
write 128 icon_128x128.png
write 256 icon_128x128@2x.png
write 256 icon_256x256.png
write 512 icon_256x256@2x.png
write 512 icon_512x512.png
write 1024 icon_512x512@2x.png

iconutil -c icns "$iconset" -o "$out"
