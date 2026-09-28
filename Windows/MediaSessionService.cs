using System.Collections.Generic;
using Windows.Graphics.Imaging;
using Windows.Media.Control;
using Windows.Foundation;
using Windows.Storage.Streams;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.MediaSession.Windows;

internal sealed class MediaSessionService : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<GlobalSystemMediaTransportControlsSession, SessionHandlers> _sessions =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GlobalSystemMediaTransportControlsSession, ArtworkCache> _artwork =
        new(ReferenceEqualityComparer.Instance);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private IPluginHost? _host;
    private GlobalSystemMediaTransportControlsSession? _selected;
    private MediaSnapshot _snapshot = MediaSnapshot.Empty;
    private int _disposed;
    private int _refreshRunning;
    private int _refreshPending;
    private long _refreshRevision;

    public MediaSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public void Initialize(IPluginHost host)
    {
        _host = host;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            lock (_sync)
            {
                if (_disposed != 0) return;
                _manager = manager;
                manager.CurrentSessionChanged += OnCurrentSessionChanged;
                manager.SessionsChanged += OnSessionsChanged;
            }
            RequestRefresh();
            // Some external media keys don't produce a prompt session metadata event.
            _ = Task.Run(PollForMetadataChangesAsync);
        }
        catch (Exception)
        {
            Publish(MediaSnapshot.Empty, null);
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args) => RequestRefresh();

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args) => RequestRefresh();

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => RequestRefresh();

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => RequestRefresh();

    private async Task PollForMetadataChangesAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var nextArtworkRefresh = DateTimeOffset.MinValue;
        while (Volatile.Read(ref _disposed) == 0 && await timer.WaitForNextTickAsync())
        {
            var selected = Volatile.Read(ref _selected);
            if (selected is null) continue;
            if (Snapshot.ArtworkPixels is null)
            {
                if (DateTimeOffset.UtcNow >= nextArtworkRefresh)
                {
                    nextArtworkRefresh = DateTimeOffset.UtcNow.AddSeconds(3);
                    RequestRefresh();
                }
                continue;
            }

            try
            {
                var properties = await selected.TryGetMediaPropertiesAsync();
                if (Volatile.Read(ref _disposed) != 0 ||
                    !ReferenceEquals(Volatile.Read(ref _selected), selected)) continue;

                string sourceAppId;
                try { sourceAppId = selected.SourceAppUserModelId; }
                catch (Exception) { sourceAppId = ""; }
                var polled = MediaSnapshot.FromProperties(sourceAppId, properties.Title, properties.Artist,
                    properties.AlbumTitle, MediaPlaybackState.Unknown, null, null, 0, 0);
                bool changed;
                lock (_sync)
                {
                    if (_disposed != 0 || !ReferenceEquals(_selected, selected)) continue;
                    changed = !_snapshot.HasSameMetadata(polled);
                }
                if (changed) RequestRefresh();
            }
            catch (Exception) { }
        }
    }

    private void RequestRefresh()
    {
        lock (_sync)
        {
            if (_disposed != 0) return;
            Interlocked.Increment(ref _refreshRevision);
            Interlocked.Exchange(ref _refreshPending, 1);
        }
        if (Interlocked.CompareExchange(ref _refreshRunning, 1, 0) == 0)
            _ = Task.Run(RefreshLoopAsync);
    }

    private async Task RefreshLoopAsync()
    {
        try
        {
            do
            {
                Interlocked.Exchange(ref _refreshPending, 0);
                var revision = Volatile.Read(ref _refreshRevision);
                try { await RefreshOnceAsync(revision); }
                catch (Exception) { }
            } while (Volatile.Read(ref _disposed) == 0 && Interlocked.Exchange(ref _refreshPending, 0) != 0);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshRunning, 0);
            if (Volatile.Read(ref _disposed) == 0 && Interlocked.Exchange(ref _refreshPending, 0) != 0)
                RequestRefresh();
        }
    }

    private async Task RefreshOnceAsync(long revision)
    {
        var manager = _manager;
        if (manager is null || Volatile.Read(ref _disposed) != 0) return;

        GlobalSystemMediaTransportControlsSession[] sessions;
        try
        {
            sessions = manager.GetSessions().ToArray();
        }
        catch (Exception)
        {
            return;
        }

        GlobalSystemMediaTransportControlsSession? current;
        try { current = manager.GetCurrentSession(); }
        catch (Exception) { return; }
        if (current is not null && !sessions.Any(session => ReferenceEquals(session, current)))
            sessions = [.. sessions, current];
        try { UpdateSessionSubscriptions(sessions); }
        catch (Exception) { }
        if (Volatile.Read(ref _disposed) != 0) return;
        var selection = await SessionRefresh.SelectAsync(sessions, current, async session =>
        {
            return MapPlayback(session.GetPlaybackInfo().PlaybackStatus);
        });
        var selected = selection.Session;
        if (selected is null)
        {
            Publish(MediaSnapshot.Empty, null, revision);
            return;
        }

        var selectedPlayback = selection.Playback;
        string sourceAppId;
        try { sourceAppId = selected.SourceAppUserModelId; }
        catch (Exception) { sourceAppId = ""; }
        try
        {
            var properties = await selected.TryGetMediaPropertiesAsync();
            MediaSnapshot previous;
            bool reuseArtwork;
            lock (_sync)
            {
                if (_disposed != 0 || revision != _refreshRevision) return;
                previous = _snapshot;
                reuseArtwork = properties.Thumbnail is not null && ReferenceEquals(_selected, selected);
                if (properties.Thumbnail is null) _artwork.Remove(selected);
            }

            var metadata = MediaSnapshot.FromProperties(sourceAppId, properties.Title,
                properties.Artist, properties.AlbumTitle, selectedPlayback, null, null, 0, 0);
            if (reuseArtwork) metadata = metadata.WithArtworkFrom(previous);
            Publish(metadata, selected, revision);
            if (properties.Thumbnail is not null && IsArtworkRefreshCurrent(selected, revision))
                _ = Task.Run(() => LoadArtworkAndPublishAsync(selected, properties.Thumbnail,
                    sourceAppId, properties.Title, properties.Artist, properties.AlbumTitle,
                    selectedPlayback, revision));
        }
        catch (Exception)
        {
            if (Volatile.Read(ref _disposed) == 0 && !ReferenceEquals(Volatile.Read(ref _selected), selected))
                Publish(MediaSnapshot.FromProperties(sourceAppId, null, null, null,
                    selectedPlayback, null, null, 0, 0), selected, revision);
        }
    }

    private void UpdateSessionSubscriptions(IReadOnlyCollection<GlobalSystemMediaTransportControlsSession> sessions)
    {
        lock (_sync)
        {
            if (_disposed != 0) return;
            var present = new HashSet<GlobalSystemMediaTransportControlsSession>(sessions, ReferenceEqualityComparer.Instance);
            foreach (var removed in _sessions.Keys.Where(session => !present.Contains(session)).ToArray())
            {
                var handlers = _sessions[removed];
                try { removed.MediaPropertiesChanged -= handlers.Properties; } catch (Exception) { }
                try { removed.PlaybackInfoChanged -= handlers.Playback; } catch (Exception) { }
                _sessions.Remove(removed);
                _artwork.Remove(removed);
            }
            foreach (var session in sessions)
            {
                if (_sessions.ContainsKey(session)) continue;
                TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> properties = OnMediaPropertiesChanged;
                TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> playback = OnPlaybackInfoChanged;
                _sessions.Add(session, new(properties, playback));
                try
                {
                    session.MediaPropertiesChanged += properties;
                    session.PlaybackInfoChanged += playback;
                }
                catch (Exception)
                {
                    try { session.MediaPropertiesChanged -= properties; } catch (Exception) { }
                    try { session.PlaybackInfoChanged -= playback; } catch (Exception) { }
                    _sessions.Remove(session);
                }
            }
        }
    }

    private async Task LoadArtworkAndPublishAsync(GlobalSystemMediaTransportControlsSession session,
        IRandomAccessStreamReference reference, string sourceAppId, string? title, string? artist,
        string? album, MediaPlaybackState playback, long revision)
    {
        var artwork = await ReadArtworkAsync(session, reference, revision);
        if (artwork is null)
        {
            return;
        }
        if (!IsArtworkRefreshCurrent(session, revision)) return;
        Publish(MediaSnapshot.FromProperties(sourceAppId, title, artist, album, playback,
            artwork.Value.Bytes, artwork.Value.Pixels, artwork.Value.Width, artwork.Value.Height),
            session, revision);
    }

    private async Task<(byte[] Bytes, uint[] Pixels, int Width, int Height)?> ReadArtworkAsync(
        GlobalSystemMediaTransportControlsSession session, IRandomAccessStreamReference? reference, long revision)
    {
        try
        {
        if (reference is null || !IsArtworkRefreshCurrent(session, revision)) return null;
        using var stream = await reference.OpenReadAsync();
        // ponytail: cap artwork at 8 MiB and 512 px to bound per-session memory; raise if devices need larger covers.
        if (stream.Size is 0 or > 8 * 1024 * 1024) return null;
        var bytes = new byte[(int)stream.Size];
        using (var reader = new DataReader(stream.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)bytes.Length);
            reader.ReadBytes(bytes);
        }

        lock (_sync)
        {
            if (!IsArtworkRefreshCurrentLocked(session, revision)) return null;
            if (_artwork.TryGetValue(session, out var cached) && bytes.AsSpan().SequenceEqual(cached.Bytes))
                return (cached.Bytes, cached.Pixels, cached.Width, cached.Height);
        }

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        double scale = Math.Min(1d, 512d / Math.Max(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight));
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.OrientedPixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.OrientedPixelHeight * scale))
        };
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var bgra = data.DetachPixelData();
        int width = (int)transform.ScaledWidth;
        int height = (int)transform.ScaledHeight;
        var pixels = ArtworkMapping.FromBgra(bgra, width, height);

        lock (_sync)
        {
            if (!IsArtworkRefreshCurrentLocked(session, revision)) return null;
            _artwork[session] = new(bytes, pixels, width, height);
        }
        return (bytes, pixels, width, height);
        }
        catch (Exception) { return null; }
    }

    private bool IsArtworkRefreshCurrent(GlobalSystemMediaTransportControlsSession session, long revision)
    {
        lock (_sync) return IsArtworkRefreshCurrentLocked(session, revision);
    }

    private bool IsArtworkRefreshCurrentLocked(GlobalSystemMediaTransportControlsSession session, long revision) =>
        _disposed == 0 && _refreshRevision == revision && ReferenceEquals(_selected, session);

    public async Task PlayPauseAsync()
    {
        var session = Volatile.Read(ref _selected);
        if (session is null) return;
        try
        {
            await session.TryTogglePlayPauseAsync();
        }
        catch (Exception) { }
        RequestRefresh();
    }

    public async Task SkipAsync(bool next)
    {
        var session = Volatile.Read(ref _selected);
        if (session is null) return;
        try
        {
            if (next) await session.TrySkipNextAsync();
            else await session.TrySkipPreviousAsync();
        }
        catch (Exception) { }
        RequestRefresh();
    }

    private void Publish(MediaSnapshot snapshot, GlobalSystemMediaTransportControlsSession? selected, long? revision = null)
    {
        IPluginHost? host;
        bool displayedContentChanged;
        lock (_sync)
        {
            if (_disposed != 0 || (revision.HasValue && revision.Value != _refreshRevision)) return;
            displayedContentChanged = !_snapshot.HasSameDisplayedContent(snapshot);
            Volatile.Write(ref _selected, selected);
            Volatile.Write(ref _snapshot, snapshot);
            host = _host;
        }
        if (!displayedContentChanged || Volatile.Read(ref _disposed) != 0) return;
        try { host?.RequestButtonRefresh("MediaSession.NowPlaying"); }
        catch (Exception) { }
    }

    private static MediaPlaybackState MapPlayback(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackState.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackState.Paused,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackState.Stopped,
        _ => MediaPlaybackState.Unknown
    };

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed != 0) return;
            Volatile.Write(ref _disposed, 1);
            var manager = _manager;
            if (manager is not null)
            {
                try { manager.CurrentSessionChanged -= OnCurrentSessionChanged; } catch (Exception) { }
                try { manager.SessionsChanged -= OnSessionsChanged; } catch (Exception) { }
            }
            foreach (var (session, handlers) in _sessions)
            {
                try { session.MediaPropertiesChanged -= handlers.Properties; } catch (Exception) { }
                try { session.PlaybackInfoChanged -= handlers.Playback; } catch (Exception) { }
            }
            _sessions.Clear();
            _artwork.Clear();
            _manager = null;
            _host = null;
            _selected = null;
            Volatile.Write(ref _snapshot, MediaSnapshot.Empty);
        }
    }

    private sealed record SessionHandlers(
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> Properties,
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> Playback);

    private sealed record ArtworkCache(byte[] Bytes, uint[] Pixels, int Width, int Height);
}
