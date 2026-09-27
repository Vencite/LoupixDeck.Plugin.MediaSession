namespace LoupixDeck.Plugin.MediaSession;

internal enum MediaPlaybackState { Unknown, Playing, Paused, Stopped }

internal sealed record SessionCandidate<T>(T Session, MediaPlaybackState Playback) where T : class;

internal sealed record MediaSnapshot(
    string SourceAppId,
    string Title,
    string Artist,
    string Album,
    MediaPlaybackState Playback,
    byte[]? ArtworkBytes,
    uint[]? ArtworkPixels,
    int ArtworkWidth,
    int ArtworkHeight,
    bool HasActiveMedia)
{
    public bool HasSameMetadata(MediaSnapshot other) =>
        SourceAppId == other.SourceAppId && Title == other.Title &&
        Artist == other.Artist && Album == other.Album && HasActiveMedia == other.HasActiveMedia;

    public bool HasSameDisplayedContent(MediaSnapshot other) =>
        HasActiveMedia == other.HasActiveMedia &&
        Title == other.Title && Artist == other.Artist &&
        ReferenceEquals(ArtworkPixels, other.ArtworkPixels) &&
        ArtworkWidth == other.ArtworkWidth && ArtworkHeight == other.ArtworkHeight;

    public MediaSnapshot WithArtworkFrom(MediaSnapshot previous) => this with
    {
        ArtworkBytes = previous.ArtworkBytes,
        ArtworkPixels = previous.ArtworkPixels,
        ArtworkWidth = previous.ArtworkWidth,
        ArtworkHeight = previous.ArtworkHeight
    };

    public static MediaSnapshot Empty { get; } = new("", "No media", "", "", MediaPlaybackState.Unknown,
        null, null, 0, 0, false);

    public static MediaSnapshot FromProperties(string appId, string? title, string? artist, string? album,
        MediaPlaybackState playback, byte[]? artworkBytes, uint[]? pixels, int width, int height) =>
        new(appId, string.IsNullOrWhiteSpace(title) ? "No title" : title.Trim(), artist?.Trim() ?? "",
            album?.Trim() ?? "", playback, artworkBytes, pixels, width, height, true);
}

internal static class SessionSelection
{
    public static T? Select<T>(IReadOnlyList<SessionCandidate<T>> sessions, T? current) where T : class
    {
        var playing = sessions.Where(x => x.Playback == MediaPlaybackState.Playing).ToArray();
        if (playing.Length == 1) return playing[0].Session;
        if (playing.Length > 1)
            return playing.FirstOrDefault(x => ReferenceEquals(x.Session, current))?.Session ?? playing[0].Session;
        return sessions.FirstOrDefault(x => ReferenceEquals(x.Session, current))?.Session ?? current;
    }
}

internal static class SessionRefresh
{
    public static async Task<(T? Session, MediaPlaybackState Playback)> SelectAsync<T>(
        IReadOnlyList<T> sessions, T? current, Func<T, Task<MediaPlaybackState>> readPlayback) where T : class
    {
        var candidates = new List<SessionCandidate<T>>(sessions.Count);
        foreach (var session in sessions)
        {
            MediaPlaybackState state;
            try { state = await readPlayback(session); }
            catch (Exception) { state = MediaPlaybackState.Unknown; }
            candidates.Add(new(session, state));
        }
        var selected = SessionSelection.Select(candidates, current);
        return (selected, selected is null ? MediaPlaybackState.Unknown :
            candidates.FirstOrDefault(candidate => ReferenceEquals(candidate.Session, selected))?.Playback ?? MediaPlaybackState.Unknown);
    }
}

internal static class ArtworkMapping
{
    public static uint[] FromBgra(ReadOnlySpan<byte> bgra, int width, int height)
    {
        int count = checked(width * height);
        if (width <= 0 || height <= 0 || bgra.Length < checked(count * 4))
            throw new ArgumentException("Artwork pixel data does not match its dimensions.", nameof(bgra));
        var pixels = new uint[count];
        for (int i = 0, p = 0; i < count; i++, p += 4)
            pixels[i] = (uint)(bgra[p + 3] << 24 | bgra[p + 2] << 16 | bgra[p + 1] << 8 | bgra[p]);
        return pixels;
    }
}
