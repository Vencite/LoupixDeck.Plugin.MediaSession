#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONFIGURATION=Release
CHECK=0
for arg in "$@"; do
  case "$arg" in
    Debug|Release) CONFIGURATION="$arg" ;;
    --check) CHECK=1 ;;
    -h|--help) echo "Usage: $(basename "$0") [Debug|Release] [--check]"; exit 0 ;;
    *) echo "Usage: $(basename "$0") [Debug|Release] [--check]" >&2; exit 2 ;;
  esac
done

for tool in dotnet powershell.exe wslpath python3; do
  command -v "$tool" >/dev/null 2>&1 || { echo "Missing required tool: $tool" >&2; exit 1; }
done
PLUGIN_ID="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$ROOT/plugin.json")"
ENTRY_ASSEMBLY="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["entryAssembly"])' "$ROOT/plugin.json")"
PROJECT="$(find "$ROOT" -maxdepth 1 -name '*.csproj' -print -quit)"
[ -n "$PROJECT" ] || { echo "Plugin project file not found." >&2; exit 1; }

if powershell.exe -NoProfile -Command 'if (Get-Process -Name LoupixDeck -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }' >/dev/null 2>&1; then
  echo "LoupixDeck is running and may lock the plugin DLL. Close it, then rerun." >&2
  exit 1
fi

dotnet build "$PROJECT" -c "$CONFIGURATION" --nologo
if [ "$CHECK" -eq 1 ]; then
  SMOKE="$(find "$ROOT/tests" -name '*Smoke.csproj' -print -quit)"
  [ -n "$SMOKE" ] || { echo "Smoke project not found." >&2; exit 1; }
  dotnet run --project "$SMOKE" -c "$CONFIGURATION"
fi

BUILD="$ROOT/bin/$CONFIGURATION"
[ -f "$BUILD/$ENTRY_ASSEMBLY" ] || { echo "Build output missing: $BUILD/$ENTRY_ASSEMBLY" >&2; exit 1; }
USERPROFILE_WIN="$(powershell.exe -NoProfile -Command '[Environment]::GetFolderPath("UserProfile")' | tr -d '\r')"
[ -n "$USERPROFILE_WIN" ] || { echo "Could not resolve Windows UserProfile path." >&2; exit 1; }
DEST="$(wslpath -u "$USERPROFILE_WIN")/.config/LoupixDeck/plugins/$PLUGIN_ID"
mkdir -p "$DEST"
find "$BUILD" -mindepth 1 -maxdepth 1 ! -name 'LoupixDeck.PluginSdk.*' -exec cp -r -- {} "$DEST/" \;
cp "$ROOT/plugin.json" "$DEST/plugin.json"
[ -f "$DEST/$ENTRY_ASSEMBLY" ] && [ -f "$DEST/plugin.json" ] || { echo "Deploy verification failed in $DEST." >&2; exit 1; }
echo "Deployed $PLUGIN_ID ($CONFIGURATION). Start LoupixDeck on Windows to load it."
