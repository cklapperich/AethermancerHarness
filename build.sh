#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PLUGIN_DIR="$HOME/.steam/debian-installation/steamapps/common/Aethermancer/BepInEx/plugins"

echo "Building AethermancerHarness..."
cd "$SCRIPT_DIR"
dotnet build

echo ""
echo "DLL copied to: $PLUGIN_DIR/AethermancerHarness.dll"
echo "Build complete!"
