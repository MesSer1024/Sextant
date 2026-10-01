#!/bin/bash
# Attach Info.plist and the Dock icon to a build or publish directory.
#
# The directory is one of:
#   .../Sextant.app/Contents/MacOS   already the bundle executable folder
#   .../Sextant.app                  publish output placed in the bundle root
#   any other folder                 loose publish output; files move into Sextant.app
set -euo pipefail

dir="${1%/}"
icns="$2"
plist="$3"

if [[ ! -d "$dir" ]]; then
  echo "stamp-mac-app: directory not found: $dir" >&2
  exit 1
fi
if [[ ! -f "$icns" || ! -f "$plist" ]]; then
  echo "stamp-mac-app: missing icon or Info.plist" >&2
  exit 1
fi

stamp_contents() {
  local contents="$1"
  mkdir -p "$contents/Resources" "$contents/MacOS"
  cp "$plist" "$contents/Info.plist"
  cp "$icns" "$contents/Resources/sextant.icns"
  if [[ -f "$contents/MacOS/Sextant" ]]; then
    chmod +x "$contents/MacOS/Sextant"
  fi
}

base="$(basename "$dir")"
parent="$(basename "$(dirname "$dir")")"
if [[ "$base" == "MacOS" && "$parent" == "Contents" ]]; then
  stamp_contents "$(dirname "$dir")"
  exit 0
fi

if [[ "$base" == *.app ]]; then
  app="$dir"
else
  app="$dir/Sextant.app"
fi

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
shopt -s nullglob dotglob
for item in "$dir"/*; do
  name="$(basename "$item")"
  if [[ "$item" == "$app" || "$name" == "." || "$name" == ".." ]]; then
    continue
  fi
  if [[ "$app" == "$dir" && "$name" == "Contents" ]]; then
    continue
  fi
  mv "$item" "$app/Contents/MacOS/"
done

stamp_contents "$app/Contents"
