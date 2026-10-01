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

# The SDK ad-hoc signs the apphost as a loose executable. Once that binary is
# the main executable of a bundle, Gatekeeper rejects the signature
# ("code has no resources but signature indicates they must be present") and
# macOS tells the user the downloaded app is damaged. Managed assemblies also
# ship with the executable bit set, and codesign then treats those PE files as
# nested code and refuses to sign the bundle.
sign_app() {
  local app="$1"
  local f ft
  if [[ ! -f "$app/Contents/MacOS/Sextant" ]]; then
    echo "stamp-mac-app: no Sextant executable in $app" >&2
    exit 1
  fi
  # A non-deep sign treats every +x file under Contents/MacOS as nested code.
  # The managed assemblies are PE images and cannot be signed, so the bundle
  # sign aborts. Clear that bit first. --deep then seals the bundle and re-signs
  # the Mach-O files in place.
  while IFS= read -r -d '' f; do
    ft="$(file -b "$f")"
    if [[ "$ft" == *Mach-O* ]]; then
      chmod +x "$f"
    elif [[ -x "$f" ]]; then
      chmod a-x "$f"
    fi
  done < <(find "$app" -type f -print0)
  codesign --force --deep --sign - "$app"
  codesign --verify --strict "$app"
}

base="$(basename "$dir")"
parent="$(basename "$(dirname "$dir")")"
if [[ "$base" == "MacOS" && "$parent" == "Contents" ]]; then
  contents="$(dirname "$dir")"
  stamp_contents "$contents"
  sign_app "$(dirname "$contents")"
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
sign_app "$app"
