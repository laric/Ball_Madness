#!/bin/sh
# Exports the Linux release build and packs it into an AppImage.
#
# Usage: packaging/linux/build-appimage.sh [version]
#
# Needs: Godot 4.7 .NET as $GODOT (default: godot) with its export templates
# installed, the .NET 8 SDK, desktop-file-utils, and appimagetool as
# $APPIMAGETOOL (default: appimagetool). Writes
# build/Ball_Madness-<version>-x86_64.AppImage.
set -eu

APP_VERSION="${1:-dev}"
# MSBuild reads environment variables as properties, so an exported VERSION
# that isn't a valid version number breaks the C# build.
unset VERSION
GODOT="${GODOT:-godot}"
APPIMAGETOOL="${APPIMAGETOOL:-appimagetool}"

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PACKAGING="$ROOT/packaging/linux"
EXPORT_DIR="$ROOT/build/linux"
APPDIR="$ROOT/build/AppDir"
OUTPUT="$ROOT/build/Ball_Madness-$APP_VERSION-x86_64.AppImage"

rm -rf "$EXPORT_DIR" "$APPDIR"
mkdir -p "$EXPORT_DIR" "$APPDIR/usr/bin"

# The first headless run imports the assets; the export then builds the C#
# project in Release and writes the binary plus its .NET data folder.
"$GODOT" --headless --path "$ROOT" --import
"$GODOT" --headless --path "$ROOT" --export-release "Linux" "$EXPORT_DIR/BallMadness.x86_64"

# Godot only warns when the C# build fails and still writes a binary that
# can't start. Stop here instead, and rerun the build to show its errors.
if [ ! -f "$EXPORT_DIR/data_BallMadness_linuxbsd_x86_64/BallMadness.dll" ]; then
	echo "The .NET assemblies are missing from the export; the C# build failed:" >&2
	dotnet publish "$ROOT/BallMadness.csproj" -c ExportRelease -r linux-x64 --self-contained \
		-p:GodotTargetPlatform=linuxbsd -o "$ROOT/build/dotnet-publish" >&2 || true
	exit 1
fi

cp -r "$EXPORT_DIR"/. "$APPDIR/usr/bin/"
cp "$PACKAGING/AppRun" "$APPDIR/AppRun"
chmod +x "$APPDIR/AppRun" "$APPDIR/usr/bin/BallMadness.x86_64"
cp "$PACKAGING/ball-madness.desktop" "$APPDIR/"
cp "$PACKAGING/ball-madness.svg" "$APPDIR/"
ln -s ball-madness.svg "$APPDIR/.DirIcon"

ARCH=x86_64 "$APPIMAGETOOL" --no-appstream "$APPDIR" "$OUTPUT"
echo "Built $OUTPUT"
