#!/usr/bin/env bash
# Template-only initializer; safe to keep in generated repositories.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
python3 - "$ROOT" "$@" <<'PYCODE'
import json
import re
import shutil
import sys
from pathlib import Path

root = Path(sys.argv[1]).resolve()
argv = sys.argv[2:]
args = {}
index = 0
while index < len(argv):
    key = argv[index]
    if key not in {"--name", "--id", "--display-name", "--description", "--platform"}:
        raise SystemExit(f"Unknown argument: {key}")
    if key in args or index + 1 >= len(argv):
        raise SystemExit(f"Missing or repeated value for {key}")
    args[key] = argv[index + 1]
    index += 2

required = {"--name", "--id", "--display-name", "--description", "--platform"}
missing = required - args.keys()
if missing:
    raise SystemExit("Missing required arguments: " + ", ".join(sorted(missing)))

name = args["--name"]
plugin_id = args["--id"]
display = args["--display-name"]
description = args["--description"]
platform = args["--platform"]

if not re.fullmatch(r"[A-Z][A-Za-z0-9]*", name):
    raise SystemExit("--name must be a PascalCase identifier using ASCII letters and digits.")
if not re.fullmatch(r"[a-z][a-z0-9-]*", plugin_id):
    raise SystemExit("--id must start with a lowercase letter and contain lowercase letters, digits or hyphens.")
if platform not in {"Windows", "All", "Linux"}:
    raise SystemExit("--platform must be Windows, All or Linux.")
for option, value in (("--display-name", display), ("--description", description)):
    if not value.strip() or "\n" in value or "\r" in value:
        raise SystemExit(f"{option} must be non-empty and on one line.")

token = lambda suffix: "__PLUGIN_" + suffix + "__"
if not (root / ("LoupixDeck.Plugin." + token("NAME") + ".csproj")).is_file():
    raise SystemExit("This repository is already initialized; refusing to modify it.")

namespace = f"LoupixDeck.Plugin.{name}"
assembly = namespace

def csharp(value):
    return value.replace("\\", "\\\\").replace('"', '\\"').replace("\r", "\\r").replace("\n", "\\n").replace("\t", "\\t")

raw = {
    token("NAME"): name,
    token("ID"): plugin_id,
    token("DISPLAY_NAME"): display,
    token("DESCRIPTION"): description,
    token("PLATFORM"): platform,
    token("NAMESPACE"): namespace,
    token("ASSEMBLY"): assembly,
}
raw["tests/TemplateSmoke/TemplateSmoke.csproj"] = f"tests/{name}Smoke/{name}Smoke.csproj"
cs = {
    token("DISPLAY_NAME"): csharp(display),
    token("DESCRIPTION"): csharp(description),
}

def template_files():
    for path in root.iterdir():
        if path.is_file() and path.name not in {"AGENTS.md", "CLAUDE.md"}:
            yield path
    private = {".git", "bin", "obj", "docs", "_references", ".agents", ".codex", ".codemap"}
    for directory in (".github", "tests", "templates"):
        base = root / directory
        if base.is_dir():
            for path in base.rglob("*"):
                if path.is_file() and not (private & set(path.relative_to(root).parts)):
                    yield path

for path in template_files():
    if path == root / "scripts/init-plugin.sh" or path.name == "plugin.json":
        continue
    if path.suffix.lower() in {".png", ".jpg", ".jpeg", ".ico", ".ttf", ".dll"}:
        continue
    try:
        content = path.read_text(encoding="utf-8")
    except UnicodeDecodeError:
        continue
    replacements = raw | (cs if path.suffix == ".cs" else {})
    for placeholder, value in replacements.items():
        content = content.replace(placeholder, value)
    path.write_text(content, encoding="utf-8")

manifest_path = root / "plugin.json"
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
manifest.update({
    "id": plugin_id,
    "name": display,
    "entryAssembly": f"{assembly}.dll",
    "platform": platform,
    "description": description,
    "projectUrl": f"https://github.com/Vencite/{assembly}",
})
manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

(root / ("LoupixDeck.Plugin." + token("NAME") + ".csproj")).rename(root / f"{assembly}.csproj")
(root / (token("NAME") + "Plugin.cs")).rename(root / f"{name}Plugin.cs")
smoke_source = root / "tests/TemplateSmoke"
smoke_target = root / f"tests/{name}Smoke"
if smoke_target != smoke_source and smoke_target.exists():
    raise SystemExit(f"Smoke-test directory already exists: {smoke_target.name}")
smoke_source.rename(smoke_target)
(root / f"tests/{name}Smoke/TemplateSmoke.csproj").rename(root / f"tests/{name}Smoke/{name}Smoke.csproj")

shutil.copyfile(root / "templates/README.plugin.md", root / "README.md")
(root / "templates/README.plugin.md").unlink()
try:
    (root / "templates").rmdir()
except OSError:
    pass

unresolved = []
for path in template_files():
    if path == root / "scripts/init-plugin.sh" or path.suffix.lower() in {".png", ".jpg", ".jpeg", ".ico", ".ttf", ".dll"}:
        continue
    try:
        if re.search(r"__PLUGIN_[A-Z_]+__", path.read_text(encoding="utf-8")):
            unresolved.append(str(path.relative_to(root)))
    except UnicodeDecodeError:
        pass
if unresolved:
    raise SystemExit("Unresolved placeholders in: " + ", ".join(unresolved))

ci = root / ".github/workflows/ci.yml"
if ci.is_file():
    ci.write_text(ci.read_text(encoding="utf-8").replace("      - run: bash scripts/test-init-plugin.sh\n", ""), encoding="utf-8")
(root / "scripts/test-init-plugin.sh").unlink(missing_ok=True)

print(f"Initialized {display} ({plugin_id}) in {root}")
PYCODE
