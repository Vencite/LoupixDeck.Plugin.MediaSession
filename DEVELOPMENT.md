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

## First release checklist

1. Run the build and smoke checks above. Confirm version `0.1.0` in plugin.json and MediaSessionPlugin.Metadata, and SDK `1.26.0` in the manifest and package reference.
2. Run the release workflow manually on the final commit. Inspect the artifact: plugin.json, entry assembly and icon at the ZIP root, required dependencies included, no LoupixDeck.PluginSdk.dll.
3. Install that ZIP through the Plugins window on Windows with LoupixDeck 1.34.0 or later. Check play/pause, Next, Previous, all layouts, scrolling, missing artwork, no active media session and saved bindings after restarting the host.
4. Publish tag `v0.1.0` with the notes in RELEASE_NOTES.md. Wait for the release workflow and verify its ZIP, plugin.json and SHA256SUMS attachments.
5. Add `mediasession` to RadiatorTwo/LoupixDeck's plugin-store.json through a pull request. Use repository `Vencite/LoupixDeck.Plugin.MediaSession`, platforms `["Windows"]`, minSdkVersion `1.26.0` and commandPrefixes `["MediaSession."]`. Copy the published workflow's store-entry.json as the release field; a manual run's entry is only a preview.
6. After the catalogue PR is merged, verify installation from the Store and enable the plugin for the device. Future releases also need a catalogue update.

## Sources

Before changing SDK usage, inspect the current plugin, relevant plugins under Vencite/LoupixDeck.Plugin.*, RadiatorTwo/LoupixDeck, RadiatorTwo/LoupixDeck.PluginSdk, then official LoupixDeck documentation/issues/PRs. Check project, manifest, command and rendering contracts against upstream source.
