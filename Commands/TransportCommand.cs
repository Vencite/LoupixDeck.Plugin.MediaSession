using LoupixDeck.Plugin.MediaSession.Windows;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.MediaSession.Commands;

internal enum TransportAction { Next, Previous }

internal sealed class TransportCommand(MediaSessionService service, TransportAction action) : IPluginCommand
{
    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton | ButtonTargets.SimpleButton;

    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = $"MediaSession.{action}",
        DisplayName = action == TransportAction.Next ? "Next" : "Previous",
        Group = "Media Session"
    };

    public Task Execute(CommandContext ctx) => service.SkipAsync(next: action == TransportAction.Next);
}
