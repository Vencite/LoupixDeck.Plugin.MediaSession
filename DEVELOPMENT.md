# Development

This repository builds the Media Session plugin.

## Requirements

- .NET 10 SDK in WSL or Linux; the Windows targeting pack restores automatically.
- Windows 10 version 2004 or later, or Windows 11, to load and run the plugin.
- LoupixDeck with a compatible Plugin SDK for runtime checks

## Build and smoke check

    dotnet restore LoupixDeck.Plugin.MediaSession.csproj
    dotnet restore tests/MediaSessionSmoke/MediaSessionSmoke.csproj
    dotnet build LoupixDeck.Plugin.MediaSession.csproj -c Release --no-restore
    dotnet run --project tests/MediaSessionSmoke/MediaSessionSmoke.csproj -c Release --no-restore
    git diff --check

The host provides LoupixDeck.PluginSdk.dll. Keep the package reference compile-only with ExcludeAssets=runtime.

## Local installation

Run scripts/deploy-dev.sh from WSL. It builds the plugin and copies its output into the current Windows user's LoupixDeck plugin directory. Close LoupixDeck first so it releases the plugin files.

## Compatibility

Treat plugin.json id, assembly name, `MediaSession.NowPlaying`, `MediaSession.Next`, `MediaSession.Previous`, command parameter order and meaning, and settings keys as public compatibility surface. Preserve them across releases or provide a migration plan. Keep plugin.json sdkVersion aligned with PluginMetadata.SdkVersion and the SDK package. Use SdkInfo.Version when supported.

## Release

The release workflow calls the reusable workflow in RadiatorTwo/LoupixDeck.PluginSdk. A manual workflow_dispatch run packages an artifact without publishing. A published GitHub Release uses the tag v<plugin.json version>. The package must not include LoupixDeck.PluginSdk.dll.

## Sources

Before changing SDK usage, inspect the current plugin, relevant plugins under Vencite/LoupixDeck.Plugin.*, RadiatorTwo/LoupixDeck, RadiatorTwo/LoupixDeck.PluginSdk, then official LoupixDeck documentation/issues/PRs. Check project, manifest, command and rendering contracts against upstream source.
