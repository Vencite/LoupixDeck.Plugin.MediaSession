#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$ROOT/docs" "$ROOT/_references"
if [ ! -e "$ROOT/AGENTS.md" ]; then
  cp "$ROOT/templates/AGENTS.md" "$ROOT/AGENTS.md"
fi
if [ ! -e "$ROOT/docs/INDEX.md" ]; then
  cat > "$ROOT/docs/INDEX.md" <<'DOC'
# Local notes

Keep short, durable findings here. Add one-line pointers to detailed notes.
DOC
fi

sync_repo() {
  local name="$1"
  local url="$2"
  local dir="$ROOT/_references/$name"
  if [ -d "$dir/.git" ]; then
    echo "Updating $name..."
    git -C "$dir" pull --ff-only
  else
    git clone --depth=1 "$url" "$dir"
  fi
}

sync_repo LoupixDeck https://github.com/RadiatorTwo/LoupixDeck.git
sync_repo LoupixDeck.PluginSdk https://github.com/RadiatorTwo/LoupixDeck.PluginSdk.git
printf 'Workspace ready: %s\n' "$ROOT"
