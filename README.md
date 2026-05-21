# LoupixDeck.Plugin.CoolerControl

CoolerControl integration plugin for [LoupixDeck](https://github.com/RadiatorTwo/LoupixDeck),
built against [LoupixDeck.PluginSdk](https://github.com/RadiatorTwo/LoupixDeck.PluginSdk).

## Commands

`System.CoolerControlSetMode` — one menu entry per mode reported by the
CoolerControl daemon.

## Settings

Daemon URL (default `http://127.0.0.1:11987/`), configured in LoupixDeck's
plugin settings and stored in `plugins/coolercontrol/settings.json`.

## Build & deploy

```bash
dotnet build LoupixDeck.Plugin.CoolerControl.csproj -c Release
```

Copy the build output together with `plugin.json` into
`LoupixDeck/plugins/coolercontrol/`.
