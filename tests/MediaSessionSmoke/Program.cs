using System.Globalization;
using LoupixDeck.Plugin.MediaSession;
using LoupixDeck.Plugin.MediaSession.Commands;
using LoupixDeck.PluginSdk;

var a = new FakeSession("a");
var b = new FakeSession("b");
var c = new FakeSession("c");

Assert(SessionSelection.Select([new(a, MediaPlaybackState.Playing), new(b, MediaPlaybackState.Paused)], b) == a,
    "A single playing session wins over a paused current session.");
Assert(SessionSelection.Select([new(a, MediaPlaybackState.Playing), new(b, MediaPlaybackState.Playing)], b) == b,
    "Current session wins when several are playing.");
Assert(SessionSelection.Select([new(a, MediaPlaybackState.Playing), new(b, MediaPlaybackState.Playing)], c) == a,
    "First playing session wins if current is not playing.");
Assert(SessionSelection.Select([new(a, MediaPlaybackState.Paused)], a) == a,
    "Current session is used when nothing is playing.");
Assert(SessionSelection.Select<FakeSession>([new(a, MediaPlaybackState.Playing)], null) == a,
    "A sole playing session works without a current session.");
Assert(SessionSelection.Select<FakeSession>([], null) is null, "No sessions selects no session.");
Assert(SessionSelection.Select([new(a, MediaPlaybackState.Paused)], c) == c,
    "Current session remains the fallback even if it is absent from the session list.");

var mapped = MediaSnapshot.FromProperties("app", "  Title  ", " Artist ", " Album ",
    MediaPlaybackState.Playing, null, null, 0, 0);
Assert(mapped.HasActiveMedia && mapped.Title == "Title" && mapped.Artist == "Artist" && mapped.Album == "Album",
    "Metadata mapping trims supplied values and tolerates missing artwork.");
Assert(mapped.HasSameMetadata(mapped with { Playback = MediaPlaybackState.Paused }) &&
       !mapped.HasSameMetadata(mapped with { Title = "Next title" }),
    "Metadata polling ignores playback-only changes and detects a new track.");
Assert(mapped.HasSameDisplayedContent(mapped with { Playback = MediaPlaybackState.Paused, Album = "Other album" }),
    "Playback and album changes alone do not invalidate the rendered button.");
var metadataUpdate = MediaSnapshot.FromProperties("app", "New title", "New artist", null,
    MediaPlaybackState.Playing, null, null, 0, 0).WithArtworkFrom(mapped);
Assert(metadataUpdate.Title == "New title" && metadataUpdate.ArtworkPixels == mapped.ArtworkPixels &&
       metadataUpdate.ArtworkBytes == mapped.ArtworkBytes,
    "Metadata can update immediately while reusing the previous session artwork.");
Assert(!mapped.HasSameDisplayedContent(mapped with { Artist = "Another artist" }),
    "A visible metadata change invalidates the rendered button.");
var missing = MediaSnapshot.FromProperties("app", " ", null, null, MediaPlaybackState.Paused, null, null, 0, 0);
Assert(missing.Title == "No title" && missing.Artist == "" && missing.Album == "" && missing.ArtworkBytes is null,
    "Missing metadata and artwork have safe values.");
Assert(!MediaSnapshot.Empty.HasActiveMedia && MediaSnapshot.Empty.Title == "No media",
    "Empty state represents no active media.");
Assert(ArtworkMapping.FromBgra([0x33, 0x22, 0x11, 0xff], 1, 1).SequenceEqual([0xff112233u]),
    "Windows BGRA artwork bytes map to the SDK ARGB pixel format.");

var failing = new FakeSession("broken", throws: true);
var changed = new FakeSession("changed", MediaPlaybackState.Playing);
var first = await SessionRefresh.SelectAsync([failing, changed], null, ReadPlayback);
Assert(first.Session == changed && first.Playback == MediaPlaybackState.Playing,
    "A failing session read does not block another session.");
changed.Playback = MediaPlaybackState.Paused;
var second = await SessionRefresh.SelectAsync([failing, changed], changed, ReadPlayback);
Assert(second.Session == changed && second.Playback == MediaPlaybackState.Paused,
    "A later refresh observes changed playback state.");

var renderSnapshot = MediaSnapshot.FromProperties("app", string.Concat(Enumerable.Repeat("🙂", 40)), null, null,
    MediaPlaybackState.Paused, [1, 2, 3], [0xff112233], 1, 1);
bool toggled = false;
var renderCommand = new NowPlayingCommand(() => renderSnapshot, () =>
{
    toggled = true;
    return Task.CompletedTask;
});
var renderContext = new CommandContext
{
    Parameters = ["true", "true", "false", "ArtworkAndText"],
    Target = ButtonTargets.TouchButton,
    Host = null!,
    ButtonKey = "now-playing-button"
};
var canvas = new RecordingCanvas(90, 90);
Assert(renderCommand.RenderImage(renderContext, canvas), "Now Playing returns a rendered button.");
Assert(canvas.PixelDraws == 1 && canvas.ArtworkDrawnAfterClear,
    "Artwork is drawn after the canvas is cleared.");
var unchangedCanvas = new RecordingCanvas(90, 90);
Assert(!renderCommand.RenderImage(renderContext, unchangedCanvas) && unchangedCanvas.PixelDraws == 0,
    "An unchanged frame does not replace the existing button bitmap.");
renderSnapshot = renderSnapshot with { Playback = MediaPlaybackState.Playing };
Assert(!renderCommand.RenderImage(renderContext, new RecordingCanvas(90, 90)),
    "A play/pause status change does not redraw an otherwise identical frame.");
renderSnapshot = renderSnapshot with { Title = "A different title" };
Assert(renderCommand.RenderImage(renderContext, new RecordingCanvas(90, 90)),
    "A visible title change redraws the button.");
Assert(canvas.PixelDraws == 1 && canvas.Text.All(draw => draw.Text is not ("Paused" or "Playing")),
    "Artwork renders without a redundant playback status label.");
var titleDraw = canvas.Text.First(draw => draw.Text.EndsWith('…')).Text;
Assert(HasValidSurrogates(titleDraw), "Long Unicode titles are shortened on text-element boundaries.");
var nowPlaying = new NowPlayingCommand(() => renderSnapshot, () => Task.CompletedTask);
Assert(nowPlaying.Descriptor.CommandName == "MediaSession.NowPlaying" &&
       nowPlaying.Descriptor.Parameters.Select(parameter => parameter.Name).SequenceEqual(
           ["ShowArtwork", "ShowTitle", "ShowArtist", "Layout", "ScrollTitle", "ScrollArtist"]),
    "Now Playing keeps its command ID and declares stable parameter ordering.");
Assert(nowPlaying.Descriptor.Parameters[3].ParameterType.IsEnum &&
       Enum.GetNames(nowPlaying.Descriptor.Parameters[3].ParameterType).SequenceEqual(
           ["ArtworkAndText", "TextOnly", "ArtworkOnly"]),
    "Layout is a dropdown with the three supported modes.");
Assert(!string.IsNullOrEmpty(NowPlayingCommand.VisibleText("A long scrolling media title", canvas, 36, 10, false, true, TimeSpan.Zero)) &&
       NowPlayingCommand.VisibleText("A long scrolling media title", canvas, 36, 10, false, true, TimeSpan.FromSeconds(2)) !=
       NowPlayingCommand.VisibleText("A long scrolling media title", canvas, 36, 10, false, true, TimeSpan.Zero),
    "Enabled scrolling changes the visible text window over time.");
string longTitle = "A long scrolling media title";
float scrollCycleWidth = canvas.MeasureText(longTitle, 10) + canvas.MeasureText("   ", 10);
var justPastTitle = NowPlayingCommand.VisibleText(longTitle, canvas, 60, 10, false, true,
    TimeSpan.FromSeconds((canvas.MeasureText(longTitle, 10) + 1) / 24d));
var nearScrollWrap = NowPlayingCommand.VisibleText(longTitle, canvas, 60, 10, false, true,
    TimeSpan.FromSeconds((scrollCycleWidth - 1) / 24d));
var atScrollWrap = NowPlayingCommand.VisibleText(longTitle, canvas, 60, 10, false, true,
    TimeSpan.FromSeconds(scrollCycleWidth / 24d));
Assert(!string.IsNullOrWhiteSpace(justPastTitle) && !string.IsNullOrWhiteSpace(nearScrollWrap) &&
       atScrollWrap == NowPlayingCommand.VisibleText(longTitle, canvas, 60, 10, false, true, TimeSpan.Zero),
    "A scrolling title immediately loops into its next copy without an empty cycle.");
Assert(justPastTitle.TrimStart().StartsWith("A", StringComparison.Ordinal),
    "The next copy starts as soon as the previous copy reaches its end.");
var scrolled = NowPlayingCommand.VisibleText("🙂🙂🙂 a long scrolling title", canvas, 36, 10,
    false, true, TimeSpan.FromSeconds(2));
Assert(HasValidSurrogates(scrolled) && canvas.MeasureText(scrolled, 10) <= 36,
    "Scrolled text stays inside its padded line and on Unicode text-element boundaries.");
var combiningText = "e\u0301 long scrolling title";
var combiningScroll = NowPlayingCommand.VisibleText(combiningText, canvas, 36, 10,
    false, true, TimeSpan.FromSeconds(1));
Assert(HasValidTextElementStarts(combiningScroll) && canvas.MeasureText(combiningScroll, 10) <= 36,
    "Scrolled text does not split combining characters and remains within the line width.");
await renderCommand.Execute(renderContext);
Assert(toggled, "Pressing Now Playing invokes its play/pause action.");
var pressedSnapshot = MediaSnapshot.FromProperties("app", "Before press", "Artist", null,
    MediaPlaybackState.Paused, null, null, 0, 0);
var pressCommand = new NowPlayingCommand(() => pressedSnapshot, () =>
{
    pressedSnapshot = pressedSnapshot with { Title = "After press" };
    return Task.CompletedTask;
});
var pressContext = new CommandContext
{
    Parameters = ["false", "true", "true", "TextOnly"],
    Target = ButtonTargets.TouchButton,
    Host = null!,
    ButtonKey = "pressed-button"
};
Assert(pressCommand.RenderImage(pressContext, new RecordingCanvas(90, 90)),
    "The press-feedback test starts with a rendered frame.");
var otherButtonContext = new CommandContext
{
    Parameters = pressContext.Parameters,
    Target = ButtonTargets.TouchButton,
    Host = null!,
    ButtonKey = "other-button"
};
Assert(pressCommand.RenderImage(otherButtonContext, new RecordingCanvas(90, 90)),
    "A second button keeps an independent rendered frame.");
await pressCommand.Execute(pressContext);
Assert(!pressCommand.RenderImage(pressContext, new RecordingCanvas(90, 90)),
    "A metadata redraw waits briefly after a tap so touch feedback can finish with its captured bitmap.");
Assert(pressCommand.RenderImage(otherButtonContext, new RecordingCanvas(90, 90)),
    "A different button can show changed metadata during the pressed button's feedback hold.");
await Task.Delay(270);
Assert(pressCommand.RenderImage(pressContext, new RecordingCanvas(90, 90)),
    "The updated frame renders after the touch feedback hold expires.");
var textOnly = new RecordingCanvas(96, 96);
var textOnlyContext = new CommandContext
{
    Parameters = ["true", "true", "true", "TextOnly"],
    Target = ButtonTargets.TouchButton,
    Host = null!
};
renderCommand.RenderImage(textOnlyContext, textOnly);
Assert(textOnly.PixelDraws == 0 && textOnly.Text.All(draw => draw.Text is not ("Paused" or "Playing")),
    "Text-only layout skips artwork and does not add a playback status label.");
var artworkOnly = new RecordingCanvas(90, 90);
var artworkOnlyContext = new CommandContext
{
    Parameters = ["true", "true", "true", "ArtworkOnly", "false", "false"],
    Target = ButtonTargets.TouchButton,
    Host = null!
};
renderCommand.RenderImage(artworkOnlyContext, artworkOnly);
Assert(artworkOnly.PixelDraws == 1 && artworkOnly.Text.Count == 0,
    "ArtworkOnly fills the tile with artwork and draws no text.");
var wideArtworkSnapshot = MediaSnapshot.FromProperties("app", "Wide image", null, null,
    MediaPlaybackState.Playing, null, new uint[16 * 9], 16, 9);
var croppedCanvas = new RecordingCanvas(90, 90);
new NowPlayingCommand(() => wideArtworkSnapshot, () => Task.CompletedTask)
    .RenderImage(artworkOnlyContext, croppedCanvas);
Assert(croppedCanvas.PixelDraws == 1 && croppedCanvas.ScaleX > 90f / 16 &&
       croppedCanvas.TranslateX < 0 && croppedCanvas.TranslateY == 0,
    "ArtworkOnly center-crops a landscape image to fill the square tile.");

Console.WriteLine("Media Session smoke checks passed.");

static Task<MediaPlaybackState> ReadPlayback(FakeSession session) => session.throws
    ? Task.FromException<MediaPlaybackState>(new InvalidOperationException("Session disappeared."))
    : Task.FromResult(session.Playback);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static bool HasValidSurrogates(string value)
{
    for (int i = 0; i < value.Length; i++)
    {
        if (char.IsHighSurrogate(value[i]))
        {
            if (i + 1 >= value.Length || !char.IsLowSurrogate(value[++i])) return false;
        }
        else if (char.IsLowSurrogate(value[i])) return false;
    }
    return true;
}

static bool HasValidTextElementStarts(string value)
{
    foreach (int start in StringInfo.ParseCombiningCharacters(value))
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(value, start);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            return false;
    }
    return true;
}

sealed class FakeSession(string id, MediaPlaybackState playback = MediaPlaybackState.Unknown, bool throws = false)
{
    public string Id { get; } = id;
    public MediaPlaybackState Playback { get; set; } = playback;
    public bool throws { get; } = throws;
}

sealed class RecordingCanvas(int width, int height) : IRenderCanvas
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public List<(string Text, int X, int Y, int Width, int Height, float FontSize)> Text { get; } = [];
    public int PixelDraws { get; private set; }
    public bool ArtworkDrawnAfterClear { get; private set; } = true;
    public float ScaleX { get; private set; } = 1;
    public float ScaleY { get; private set; } = 1;
    public float TranslateX { get; private set; }
    public float TranslateY { get; private set; }
    public void Clear(PluginColor color) { _cleared = true; }
    public void FillRectangle(int x, int y, int width, int height, PluginColor color) { }
    public void DrawRectangle(int x, int y, int width, int height, int strokeWidth, PluginColor color) { }
    public void FillRoundedRectangle(int x, int y, int width, int height, int radius, PluginColor color) { }
    public void DrawRoundedRectangle(int x, int y, int width, int height, int radius, int strokeWidth, PluginColor color) { }
    public void FillCircle(int centerX, int centerY, int radius, PluginColor color) { }
    public void DrawCircle(int centerX, int centerY, int radius, int strokeWidth, PluginColor color) { }
    public void FillEllipse(int x, int y, int width, int height, PluginColor color) { }
    public void DrawEllipse(int x, int y, int width, int height, int strokeWidth, PluginColor color) { }
    public void DrawArc(int x, int y, int width, int height, float startAngle, float sweepAngle, int strokeWidth, PluginColor color) { }
    public void FillArc(int x, int y, int width, int height, float startAngle, float sweepAngle, PluginColor color) { }
    public void DrawLine(int x1, int y1, int x2, int y2, int strokeWidth, PluginColor color) { }
    public void DrawText(string text, int x, int y, int width, int height, PluginColor color, float fontSize,
        bool bold = false, bool italic = false, bool centered = true, bool outlined = false, PluginColor outlineColor = default) =>
        Text.Add((text, x, y, width, height, fontSize));
    public void DrawText(string text, int x, int y, int width, int height, PluginColor color, float fontSize,
        TextHAlign hAlign, TextVAlign vAlign, bool bold = false, bool italic = false, bool outlined = false,
        PluginColor outlineColor = default) => Text.Add((text, x, y, width, height, fontSize));
    public float MeasureText(string text, float fontSize, bool bold = false, bool italic = false) =>
        StringInfo.ParseCombiningCharacters(text).Length * fontSize;
    public void DrawSymbol(string symbolId, int x, int y, int width, int height, PluginColor tint) { }
    public void DrawSymbol(string symbolId, int x, int y, int width, int height, SymbolStyle style) { }
    public void DrawImage(byte[] imageBytes, int x, int y, int width, int height) { }
    public void DrawImage(byte[] imageBytes, int x, int y, int width, int height, byte opacity, PluginColor tint = default) { }
    public void DrawPixels(ReadOnlySpan<uint> pixels, int width, int height, int x = 0, int y = 0)
    {
        PixelDraws++;
        ArtworkDrawnAfterClear &= _cleared;
    }
    public void PushTransform() { }
    public void PopTransform() { }
    public void Translate(float dx, float dy) { TranslateX += dx; TranslateY += dy; }
    public void Rotate(float degrees) { }
    public void Scale(float sx, float sy) { ScaleX *= sx; ScaleY *= sy; }
    private bool _cleared;
}
