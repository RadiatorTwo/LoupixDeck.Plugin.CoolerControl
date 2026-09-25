# LoupixDeck.Plugin.CoolerControl

CoolerControl integration plugin for [LoupixDeck](https://github.com/RadiatorTwo/LoupixDeck),
built against [LoupixDeck.PluginSdk](https://github.com/RadiatorTwo/LoupixDeck.PluginSdk).

Talks to the CoolerControl daemon's REST API. When the daemon is not reachable, the tiles show
"NOT RUNNING" and recover automatically once it is back.

## Commands

- `System.CoolerControlSetMode` — one menu entry per mode reported by the CoolerControl daemon.
- `CoolerControl.Sensor` — one reading per command. Chain up to four on one button for a
  multi-row tile. Readings are offered as a live menu sorted by component (CPU, GPU, Memory,
  Storage, Mainboard, Cooling, Network, Other), device and quantity (temperature, clock, load,
  power, fans).
- `CoolerControl.Pages` — component pages CPU, GPU, DISK and a CPU summary; a key press shows the
  next page. Chain several `Pages` commands to build your own cycle. CoolerControl reports no
  memory usage and no transfer rates, so there are no RAM and NET pages, and DISK shows the
  hottest drive.

Both display commands draw pixel tiles (5×7 bitmap font, no anti-aliasing) with a gauge bar and
a 72-second history chart. Readings turn amber or red past their limits (CPU relative to the
**CPU TjMax** setting, GPU core, drives, a stalled fan while its temperature is high). The
**Transparent background** setting lets the page wallpaper show through.

The menu, the settings and the command texts are available in English, German and Spanish.
Requires LoupixDeck with Plugin SDK 1.26 or later.

## Settings

Configured in LoupixDeck's plugin settings and stored in `plugins/coolercontrol/settings.json`.

- **Daemon URL** — default `http://127.0.0.1:11987/`. CoolerControl 4.0 and later serve HTTPS
  with a self-signed certificate by default; use `https://127.0.0.1:11987/` then. The certificate
  is accepted for a daemon on the same machine only.
- **Access token** — required for CoolerControl 4.0 and later, which no longer accept the default
  password. Create one in CoolerControl under Access Protection; give it write access to switch
  modes. Leave empty for older daemons.
- **Transparent background**, **CPU TjMax (°C)** — see above.

## Build & deploy

```bash
dotnet build LoupixDeck.Plugin.CoolerControl.csproj -c Release
```

Copy the build output together with `plugin.json` and the `strings.*.json` files into
`LoupixDeck/plugins/coolercontrol/`.
