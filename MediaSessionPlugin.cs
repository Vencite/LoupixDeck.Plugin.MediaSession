using LoupixDeck.PluginSdk;
using LoupixDeck.Plugin.MediaSession.Commands;
using LoupixDeck.Plugin.MediaSession.Windows;

namespace LoupixDeck.Plugin.MediaSession;

public sealed class MediaSessionPlugin : LoupixPlugin
{
    private readonly MediaSessionService _service = new();
    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "mediasession",
        Name = "Media Session",
        Version = new Version(0, 1, 0),
        SdkVersion = SdkInfo.Version,
        Author = "Vencite",
        Description = "Show and control the current Windows media session from LoupixDeck.",
        Icon = ReadPluginIcon()
    };

    public override IEnumerable<IPluginCommand> GetCommands() =>
    [
        new NowPlayingCommand(() => _service.Snapshot, _service.PlayPauseAsync),
        new TransportCommand(_service, TransportAction.Next),
        new TransportCommand(_service, TransportAction.Previous)
    ];

    public override void Initialize(IPluginHost host)
    {
        base.Initialize(host);
        _service.Initialize(host);
    }

    public override void Shutdown()
    {
        _service.Dispose();
        base.Shutdown();
    }

    private static byte[]? ReadPluginIcon()
    {
        using Stream? stream = typeof(MediaSessionPlugin).Assembly.GetManifestResourceStream(
            "LoupixDeck.Plugin.MediaSession.icon.png");
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
