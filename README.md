# Media Session for LoupixDeck

Show the current Windows media session on a LoupixDeck touch button. Press the button to play or pause.

## Features

- Show artwork, title and artist from the selected Windows media session.
- Choose between artwork with text, text only and artwork only.
- Scroll long titles and artists in a continuous loop when enabled.
- Assign Next and Previous commands separately if wanted.

## Requirements

- Windows 10 version 2004 or later, or Windows 11.
- LoupixDeck using Plugin SDK 1.26.0 or later.
- A media source that publishes a Windows Global System Media Transport Controls session.

## Installation

No release has been published yet. To try the plugin locally, follow the build and dev deployment steps in [DEVELOPMENT.md](DEVELOPMENT.md). Once a release is available, download its ZIP from [GitHub Releases](https://github.com/Vencite/LoupixDeck.Plugin.MediaSession/releases) and install it from the LoupixDeck Plugins window.

## Use

Add **Now Playing** to a touch button. Press it to toggle playback for the selected Windows media session. The optional **Next** and **Previous** commands skip tracks.

## Button customization

Now Playing has these parameters in this order:

1. `ShowArtwork` - show artwork when available (default `true`). The `ArtworkOnly` layout center-crops artwork to fill the square button.
2. `ShowTitle` - show the track title (default `true`).
3. `ShowArtist` - show the artist (default `true`).
4. `Layout` - select a mode from the dropdown:
   - `ArtworkAndText` - artwork above the title and artist (default).
   - `TextOnly` - title and artist without artwork.
   - `ArtworkOnly` - artwork fills the whole button and is center-cropped to a square.
5. `ScrollTitle` - scroll a long title instead of shortening it (default `false`).
6. `ScrollArtist` - scroll a long artist instead of shortening it (default `false`).

Enable the matching scroll option to loop long text continuously; it does not pause on an empty frame.

## Current limitations

The plugin reads Windows media sessions. Spotify Desktop, Chrome, Edge and YouTube are examples of sources that may publish one; the plugin has no special Spotify, browser or YouTube integration. If an app or website does not expose its media through Windows Global System Media Transport Controls, the plugin cannot show or control it. Artwork is optional and appears only when the source publishes it.

## Development

See [DEVELOPMENT.md](DEVELOPMENT.md) for building, smoke checks, local deployment and release packaging.

## License

See [LICENSE](LICENSE).
