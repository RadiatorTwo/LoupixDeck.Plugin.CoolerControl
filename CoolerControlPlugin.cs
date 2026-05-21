using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.CoolerControl;

/// <summary>
/// Entry point of the CoolerControl plugin. Contributes the "Set Mode" command
/// and a live "Modes" submenu, and exposes the daemon URL as a setting.
/// </summary>
public sealed class CoolerControlPlugin : LoupixPlugin, IMenuContributor, IPluginSettingsPage
{
    private const string KeyUrl = "url";
    private const string DefaultUrl = "http://127.0.0.1:11987/";

    private readonly CoolerControlApiController _controller = new();
    private IPluginHost? _host;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "coolercontrol",
        Name = "CoolerControl",
        Version = new Version(1, 0, 0),
        SdkVersion = new Version(1, 1, 0),
        Author = "RadiatorTwo",
        Description = "Activate CoolerControl modes from the device via the CoolerControl daemon API."
    };

    public override void Initialize(IPluginHost host)
    {
        _host = host;
        ApplySettings();
    }

    public override IEnumerable<IPluginCommand> GetCommands()
    {
        return [new CoolerControlSetModeCommand(_controller)];
    }

    // ───────── IMenuContributor — dynamic "Modes" submenu ─────────

    public async Task<IReadOnlyList<MenuNode>> GetMenuNodes(ButtonTargets target)
    {
        // Mode switching is offered for touch buttons only.
        if (target != ButtonTargets.TouchButton)
            return [];

        var modeChildren = new List<MenuNode>();

        try
        {
            var modes = await _controller.GetModes();
            foreach (var mode in modes)
            {
                var name = mode["name"]?.ToString();
                var uid = mode["uid"]?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(name))
                    continue;

                modeChildren.Add(new MenuNode
                {
                    Name = name,
                    CommandName = "System.CoolerControlSetMode",
                    Parameters = new Dictionary<string, string> { { "UID", uid } }
                });
            }
        }
        catch (Exception ex)
        {
            modeChildren.Add(new MenuNode { Name = $"Connection failed: {ex.Message}" });
        }

        var modesFolder = new MenuNode { Name = "Modes", Children = modeChildren };
        return [new MenuNode { Name = "Cooler Control", Children = [modesFolder] }];
    }

    // ───────── IPluginSettingsPage ─────────

    public IReadOnlyList<PluginSettingDescriptor> SettingsSchema { get; } =
    [
        new PluginSettingDescriptor
        {
            Key = KeyUrl, Label = "Daemon URL", Kind = PluginSettingKind.Text,
            DefaultValue = DefaultUrl,
            Description = "Base URL of the CoolerControl daemon REST API."
        }
    ];

    public IReadOnlyList<PluginSettingAction> SettingsActions => _settingsActions ??=
    [
        new PluginSettingAction
        {
            Label = "Test Connection",
            Invoke = async () =>
            {
                ApplySettings();
                try
                {
                    var modes = await _controller.GetModes();
                    return $"Connected — {modes.Count} mode(s)";
                }
                catch (Exception ex)
                {
                    return $"Failed: {ex.Message}";
                }
            }
        }
    ];

    private IReadOnlyList<PluginSettingAction>? _settingsActions;

    public void OnSettingsSaved() => ApplySettings();

    private void ApplySettings()
    {
        if (_host == null)
            return;

        var url = _host.Settings.Get(KeyUrl, DefaultUrl) ?? DefaultUrl;
        _controller.Configure(url);
    }
}
