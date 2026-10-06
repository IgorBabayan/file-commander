#!/usr/bin/env bash
# Builds File Commander as an x86_64 AppImage.
#   build/appimage/build-appimage.sh <version> [output folder]
# Used by .github/workflows/release.yml; also runs locally (needs the .NET 10 SDK and curl).
set -euo pipefail

VERSION="${1:?usage: build-appimage.sh <version> [output folder]}"
OUTPUT="$(mkdir -p "${2:-artifacts}" && cd "${2:-artifacts}" && pwd)"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
PROJECT="$ROOT/File.Commander/File.Commander/File.Commander.csproj"
ICON="$ROOT/File.Commander/File.Commander/Assets/logo.png"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
APPDIR="$WORK/AppDir"
mkdir -p "$APPDIR/usr/bin"

# Self-contained: the AppImage runs without a .NET runtime on the machine
dotnet publish "$PROJECT" \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    --output "$APPDIR/usr/bin" \
    -p:Version="$VERSION" \
    -p:DebugType=None \
    -p:DebugSymbols=false

install -m 755 "$HERE/AppRun" "$APPDIR/AppRun"
install -m 644 "$HERE/file-commander.desktop" "$APPDIR/file-commander.desktop"
install -m 644 "$ICON" "$APPDIR/file-commander.png"
ln -s file-commander.png "$APPDIR/.DirIcon"

TOOL="$WORK/appimagetool"
curl -fsSL -o "$TOOL" \
    https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x "$TOOL"

# Extract-and-run: CI runners have no FUSE. The name has to contain x86_64: the in-app updater looks for it
TARGET="$OUTPUT/File.Commander-$VERSION-x86_64.AppImage"
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$TOOL" --no-appstream "$APPDIR" "$TARGET"

echo "Built $TARGET"
