# Plugin icon generator

`make_icon.py` draws the plugin icon: a seven-blade fan rotor, matte, night blue.
It follows the Audio plugin's icon (same background, colors and shading; no level ring) and is original
artwork, no third-party source. The CoolerControl project logo is not used.

```bash
pip install pillow numpy
python tools/icon/make_icon.py tools/icon/out
cp tools/icon/out/icon_256.png icon.png
```

The script writes `icon_{256,128,64,32,16}.png` into the given folder; only the 256 px file is
used, as `icon.png` in the repo root. The `out/` folder is not committed.
