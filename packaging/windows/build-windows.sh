#!/bin/sh
# Exports the Windows release build as one self-contained .exe.
#
# Usage: packaging/windows/build-windows.sh [version]
#
# Needs: Godot 4.7 .NET as $GODOT (default: godot) with the Windows export
# templates installed, and the .NET 8 SDK. Runs on Linux. Writes
# build/Ball_Madness-<version>-windows-x86_64.exe.
#
# The "Windows" preset embeds the game data (PCK) and the .NET assemblies and
# runtime into the executable, so there is no data folder to ship beside it.
# On first launch the game unpacks the .NET files into its user data folder.
set -eu

APP_VERSION="${1:-dev}"
# MSBuild reads environment variables as properties, so an exported VERSION
# that isn't a valid version number breaks the C# build.
unset VERSION
GODOT="${GODOT:-godot}"

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
EXPORT_DIR="$ROOT/build/windows"
EXE="$EXPORT_DIR/BallMadness.exe"
OUTPUT="$ROOT/build/Ball_Madness-$APP_VERSION-windows-x86_64.exe"

rm -rf "$EXPORT_DIR"
mkdir -p "$EXPORT_DIR"

# The first headless run imports the assets; the export then builds the C#
# project in Release and embeds it with the game data into the executable.
"$GODOT" --headless --path "$ROOT" --import
"$GODOT" --headless --path "$ROOT" --export-release "Windows" "$EXE"

# Godot only warns when the C# build fails and still writes an executable
# that can't start. Stop here instead, and rerun the build to show its errors.
if [ ! -f "$EXE" ] || ! grep -q -a ".godot/mono/publish/x86_64/BallMadness.dll" "$EXE"; then
	echo "The .NET assemblies are missing from the export; the C# build failed:" >&2
	dotnet publish "$ROOT/BallMadness.csproj" -c ExportRelease -r win-x64 --self-contained \
		-p:GodotTargetPlatform=windows -o "$ROOT/build/dotnet-publish" >&2 || true
	exit 1
fi

mv "$EXE" "$OUTPUT"
echo "Built $OUTPUT"
