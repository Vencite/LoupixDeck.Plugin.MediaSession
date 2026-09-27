# Agent instructions

## Sources of truth

Use sources in this order:

1. This plugin's current code and compatibility surface.
2. Other relevant Vencite/LoupixDeck.Plugin.* repositories.
3. RadiatorTwo/LoupixDeck.
4. RadiatorTwo/LoupixDeck.PluginSdk.
5. Official LoupixDeck documentation, Issues and PRs.
6. The upstream project for any external technology used by this plugin.

Before changing SDK behavior, inspect current upstream source. Check the project file, manifest, commands and rendering contracts. Do not invent types, fields, capabilities or APIs.

## Compatibility

Preserve plugin ID, command IDs, parameter ordering and meaning, and settings keys. Treat saved profiles and button bindings as public API. Do not package LoupixDeck.PluginSdk.dll; the host supplies it.

## Implementation

Prefer the smallest solution that meets the current requirement. Add architecture only for a concrete need. Keep rendering synchronous and cheap; use prepared state for I/O-backed features.

## Release

Do not publish a release or tag without an explicit request. Verify manifest and metadata versions, package contents and compatibility before release preparation.

## Writing

Use short hyphens (-) in prose. When writing Issues, PRs or comments on the user's behalf, use natural, plain language without an AI tone.
