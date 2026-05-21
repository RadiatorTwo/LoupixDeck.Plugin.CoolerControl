using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.CoolerControl;

/// <summary>
/// Activates a CoolerControl mode by UID. The command name is kept identical
/// to the former built-in command so existing button assignments keep working.
/// </summary>
internal sealed class CoolerControlSetModeCommand(CoolerControlApiController controller) : IPluginCommand
{
    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = "System.CoolerControlSetMode",
        DisplayName = "Set Mode",
        Group = "Cooler Control",
        ParameterTemplate = "({UID})",
        Parameters = [new CommandParameter("UID", typeof(string))],
        // Surfaced per mode through the dynamic "Modes" submenu.
        HiddenFromMenu = true
    };

    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton;

    public async Task Execute(CommandContext ctx)
    {
        if (ctx.Parameters.Length != 1)
        {
            Console.WriteLine("System.CoolerControlSetMode: invalid parameter count");
            return;
        }

        await controller.SetMode(ctx.Parameters[0]);
    }
}
