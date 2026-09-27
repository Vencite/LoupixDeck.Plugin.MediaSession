using System.Globalization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.MediaSession.Commands;

internal enum NowPlayingLayout { ArtworkAndText, TextOnly, ArtworkOnly }

internal sealed class NowPlayingCommand(Func<MediaSnapshot> getSnapshot, Func<Task> playPause) : IDisplayImageCommand
{
    private const long TouchFeedbackHoldMilliseconds = 250;
    private readonly object _frameSync = new();
    private readonly Dictionary<string, RenderFrame> _lastFrames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _refreshBlockedUntil = new(StringComparer.Ordinal);

    private static readonly CommandParameter[] Parameters =
    [
        new("ShowArtwork", typeof(bool)) { DefaultValue = "true" },
        new("ShowTitle", typeof(bool)) { DefaultValue = "true" },
        new("ShowArtist", typeof(bool)) { DefaultValue = "true" },
        new("Layout", typeof(NowPlayingLayout)) { DefaultValue = nameof(NowPlayingLayout.ArtworkAndText) },
        new("ScrollTitle", typeof(bool)) { DefaultValue = "false" },
        new("ScrollArtist", typeof(bool)) { DefaultValue = "false" }
    ];

    public TimeSpan UpdateInterval => TimeSpan.FromMilliseconds(250);
    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton;
    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = "MediaSession.NowPlaying",
        DisplayName = "Now Playing",
        Group = "Media Session",
        Icon = "\U000F040C",
        Description = "Show the current system media session; press to play or pause.",
        ParameterTemplate = "({ShowArtwork}, {ShowTitle}, {ShowArtist}, {Layout}, {ScrollTitle}, {ScrollArtist})",
        Parameters = Parameters
    };

    public Task Execute(CommandContext ctx)
    {
        var key = FrameKey(ctx);
        if (key is not null)
        {
            // Let the host finish its ~100 ms press flash before replacing the image it captured.
            lock (_frameSync) _refreshBlockedUntil[key] = Environment.TickCount64 + TouchFeedbackHoldMilliseconds;
        }
        return playPause();
    }

    public bool RenderImage(CommandContext ctx, IRenderCanvas canvas)
    {
        if (IsRefreshBlocked(ctx)) return false;

        var snapshot = getSnapshot();
        bool showArtwork = ReadBool(ctx, 0, true);
        bool showTitle = ReadBool(ctx, 1, true);
        bool showArtist = ReadBool(ctx, 2, true);
        var layout = ReadLayout(ctx);
        bool scrollTitle = ReadBool(ctx, 4, false);
        bool scrollArtist = ReadBool(ctx, 5, false);
        bool artworkOnly = layout == NowPlayingLayout.ArtworkOnly;
        bool textOnly = layout == NowPlayingLayout.TextOnly;
        bool drawArtwork = !textOnly && showArtwork && snapshot.ArtworkPixels is { Length: > 0 };

        if (artworkOnly)
        {
            var frame = new RenderFrame(layout, canvas.Width, canvas.Height,
                drawArtwork ? snapshot.ArtworkPixels : null,
                drawArtwork ? snapshot.ArtworkWidth : 0, drawArtwork ? snapshot.ArtworkHeight : 0,
                null, null);
            if (IsUnchanged(ctx, frame)) return false;

            canvas.Clear(PluginColor.Black);
            if (drawArtwork) DrawArtwork(canvas, snapshot, 0, 0, canvas.Width, canvas.Height, crop: true);
            RememberFrame(ctx, frame);
            return true;
        }

        const int horizontalPadding = 12;
        const int verticalPadding = 5;
        int textTop = verticalPadding;
        int textHeight = canvas.Height - 2 * verticalPadding;
        if (drawArtwork)
        {
            int artworkHeight = (int)(canvas.Height * 0.46f);
            textTop = artworkHeight + verticalPadding;
            textHeight = canvas.Height - textTop - verticalPadding;
        }

        bool showTitleText = showTitle || !snapshot.HasActiveMedia;
        bool showArtistText = showArtist && snapshot.HasActiveMedia && !string.IsNullOrWhiteSpace(snapshot.Artist);
        int lineCount = (showTitleText ? 1 : 0) + (showArtistText ? 1 : 0);
        int width = Math.Max(1, canvas.Width - 2 * horizontalPadding);
        var elapsed = Elapsed();
        string? title = showTitleText
            ? snapshot.HasActiveMedia ? snapshot.Title : "No media"
            : null;
        string? artist = showArtistText ? snapshot.Artist : null;
        bool titleIsScrolling = title is not null && scrollTitle && canvas.MeasureText(title, 13, bold: true) > width;
        bool artistIsScrolling = artist is not null && scrollArtist && canvas.MeasureText(artist, 11) > width;
        string? visibleTitle = title is null ? null : VisibleText(title, canvas, width, 13, true, scrollTitle, elapsed);
        string? visibleArtist = artist is null ? null : VisibleText(artist, canvas, width, 11, false, scrollArtist, elapsed);
        var frameKey = new RenderFrame(layout, canvas.Width, canvas.Height,
            drawArtwork ? snapshot.ArtworkPixels : null,
            drawArtwork ? snapshot.ArtworkWidth : 0, drawArtwork ? snapshot.ArtworkHeight : 0,
            visibleTitle, visibleArtist);
        if (IsUnchanged(ctx, frameKey)) return false;

        canvas.Clear(PluginColor.Black);
        if (drawArtwork)
            DrawArtwork(canvas, snapshot, 0, 0, canvas.Width, (int)(canvas.Height * 0.46f));
        if (lineCount == 0)
        {
            RememberFrame(ctx, frameKey);
            return true;
        }

        int lineHeight = textHeight / lineCount;
        if (showTitleText)
        {
            DrawLine(visibleTitle!, titleIsScrolling, 13, bold: true,
                canvas, horizontalPadding, textTop, width, lineHeight);
            textTop += lineHeight;
        }
        if (showArtistText)
            DrawLine(visibleArtist!, artistIsScrolling, 11, bold: false, canvas, horizontalPadding, textTop,
                width, canvas.Height - verticalPadding - textTop);
        RememberFrame(ctx, frameKey);
        return true;
    }

    private static void DrawLine(string text, bool scrolling, float fontSize, bool bold,
        IRenderCanvas canvas, int x, int y, int width, int height)
    {
        canvas.DrawText(text,
            x, y, width, height, PluginColor.White, fontSize,
            scrolling ? TextHAlign.Left : TextHAlign.Center, TextVAlign.Middle, bold: bold);
    }

    internal static string VisibleText(string value, IRenderCanvas canvas, int width, float fontSize,
        bool bold, bool scroll, TimeSpan elapsed)
    {
        if (canvas.MeasureText(value, fontSize, bold) <= width) return value;
        if (!scroll) return Fit(value, canvas, width, fontSize, bold);

        const string gap = "   ";
        var repeated = string.Concat(value, gap, value, gap, value);
        var starts = StringInfo.ParseCombiningCharacters(repeated);
        var advances = new float[starts.Length + 1];
        for (int i = 0; i < starts.Length; i++)
        {
            int end = i + 1 == starts.Length ? repeated.Length : starts[i + 1];
            advances[i + 1] = advances[i] + canvas.MeasureText(repeated[starts[i]..end], fontSize, bold);
        }

        int valueElements = StringInfo.ParseCombiningCharacters(value).Length;
        int gapElements = StringInfo.ParseCombiningCharacters(gap).Length;
        float cycleWidth = advances[valueElements + gapElements];
        float offset = (float)(Math.Max(0, elapsed.TotalSeconds) * 24 % cycleWidth);
        int start = 0;
        while (start < starts.Length && advances[start + 1] <= offset) start++;
        if (start == starts.Length) return "";

        int endIndex = start;
        while (endIndex < starts.Length && advances[endIndex + 1] - advances[start] <= width)
            endIndex++;
        if (endIndex == start) return Fit(value, canvas, width, fontSize, bold);
        return repeated[starts[start]..(endIndex == starts.Length ? repeated.Length : starts[endIndex])];
    }

    private static string Fit(string value, IRenderCanvas canvas, int width, float fontSize, bool bold)
    {
        const string suffix = "…";
        var starts = StringInfo.ParseCombiningCharacters(value);
        int low = 0, high = starts.Length;
        while (low < high)
        {
            int middle = low + (high - low + 1) / 2;
            int end = middle == starts.Length ? value.Length : starts[middle];
            if (canvas.MeasureText(value[..end] + suffix, fontSize, bold) <= width) low = middle;
            else high = middle - 1;
        }
        int prefixEnd = low == starts.Length ? value.Length : starts[low];
        return low == 0 ? suffix : value[..prefixEnd].TrimEnd() + suffix;
    }

    private static bool ReadBool(CommandContext context, int index, bool fallback) =>
        bool.TryParse(Read(context, index, fallback ? "true" : "false"), out var value) ? value : fallback;

    private static string Read(CommandContext context, int index, string fallback) =>
        context.Parameters.Length > index ? context.Parameters[index] : fallback;

    private static NowPlayingLayout ReadLayout(CommandContext context) =>
        Enum.TryParse<NowPlayingLayout>(Read(context, 3, nameof(NowPlayingLayout.ArtworkAndText)), true, out var layout) &&
        Enum.IsDefined(layout)
            ? layout
            : NowPlayingLayout.ArtworkAndText;

    private bool IsUnchanged(CommandContext context, RenderFrame frame)
    {
        var key = FrameKey(context);
        if (key is null) return false;
        lock (_frameSync)
            return _lastFrames.TryGetValue(key, out var previous) && previous == frame;
    }

    private bool IsRefreshBlocked(CommandContext context)
    {
        var key = FrameKey(context);
        if (key is null) return false;
        lock (_frameSync)
        {
            if (!_refreshBlockedUntil.TryGetValue(key, out var until)) return false;
            if (Environment.TickCount64 < until) return true;
            _refreshBlockedUntil.Remove(key);
            return false;
        }
    }

    private void RememberFrame(CommandContext context, RenderFrame frame)
    {
        var key = FrameKey(context);
        if (key is null) return;
        lock (_frameSync) _lastFrames[key] = frame;
    }

    private static string? FrameKey(CommandContext context) => context.ButtonKey is { Length: > 0 } buttonKey
        ? $"{context.Device?.Slug}\0{buttonKey}"
        : null;

    private static TimeSpan Elapsed() => TimeSpan.FromMilliseconds(Environment.TickCount64);

    private static void DrawArtwork(IRenderCanvas canvas, MediaSnapshot snapshot, int x, int y, int width, int height,
        bool crop = false)
    {
        if (snapshot.ArtworkPixels is not { Length: > 0 } pixels || snapshot.ArtworkWidth <= 0 || snapshot.ArtworkHeight <= 0)
            return;
        float scale = crop
            ? Math.Max((float)width / snapshot.ArtworkWidth, (float)height / snapshot.ArtworkHeight)
            : Math.Min((float)width / snapshot.ArtworkWidth, (float)height / snapshot.ArtworkHeight);
        int drawWidth = (int)(snapshot.ArtworkWidth * scale);
        int drawHeight = (int)(snapshot.ArtworkHeight * scale);
        canvas.PushTransform();
        canvas.Translate(x + (width - drawWidth) / 2, y + (height - drawHeight) / 2);
        canvas.Scale(scale, scale);
        canvas.DrawPixels(pixels, snapshot.ArtworkWidth, snapshot.ArtworkHeight);
        canvas.PopTransform();
    }

    private sealed record RenderFrame(NowPlayingLayout Layout, int CanvasWidth, int CanvasHeight,
        uint[]? ArtworkPixels, int ArtworkWidth, int ArtworkHeight, string? Title, string? Artist);

}
