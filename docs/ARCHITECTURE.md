# Architecture

A map of the Shiftline repository for anyone who wants to change something. For building see [BUILD.md](BUILD.md), for releasing see [RELEASE.md](RELEASE.md).

## The big picture

```
  Game (iRacing, ...)
        |
        v
  SimHub   reads telemetry (RPM, gear, brake, flags, ...) and exposes it as properties
        |
        v
  +-------------------------------+      +--------------------------------+
  | Shiftline plugin (C#, WPF)    |      | Shiftline script (JavaScript)  |
  |  - settings pages             | ---> |  - computes the colour of      |
  |  - stores settings and presets|      |    every LED for each frame    |
  |  - themes, hotkeys, updates   |      |  - reads settings from the     |
  |  - carries the script inside  |      |    properties Shiftline.<Name> |
  +-------------------------------+      +---------------+----------------+
                                                         |
                       loader LED profile (imported once into SimHub)
                                                         v
                                        SimHub LED device (Arduino, WS2812B)
```

The two halves only talk through SimHub properties. The plugin publishes every setting as `Shiftline.<Name>` (for example `Shiftline.RpmFlashColour`); the script reads it with `$prop('Shiftline.RpmFlashColour')`.

The script is embedded in the plugin DLL. The plugin publishes it as `Shiftline.ScriptSource`, and the small loader profile (`profiles/Shiftline Loader.ledsprofile`) evaluates it. A plugin update therefore also updates the LED logic, and the profile is imported only once.

## Technologies

| Technology | Used for | Where |
|---|---|---|
| C# (.NET Framework 4.8, C# 7.3) | Plugin logic: settings, presets, updates, hotkeys | `plugin/Shiftline/*.cs` |
| XAML / WPF | The settings pages | `plugin/Shiftline/*.xaml` |
| JavaScript (Jint engine in SimHub) | LED logic | `tools/Shiftline.plugin.js`, `tools/loader.js` |
| Python 3 | Code generation and static checks | `tools/gen.py`, `tools/check.py`, `tools/make_profile.py` |
| Node.js | Mock tests of the script, no SimHub needed | `tools/test_*.js`, `tools/harness.js` |
| PowerShell | Release script and the update script | `tools/make-release.ps1`, embedded in `UpdateService.cs` |
| Inno Setup 6 | The installer | `installer/Shiftline.iss` |

The plugin is built with Visual Studio 2022 or newer. Python and Node are only needed for the tools and tests.

## Repository layout

```
.
├── README.md
├── LICENSE.txt
├── docs/                         BUILD.md, RELEASE.md, this file, images
├── installer/                    Inno Setup script and the info page text
├── profiles/                     the loader LED profile (the only profile)
├── tools/
│   ├── Shiftline.plugin.js       the LED script and ALL default values (SL_DEFAULTS)
│   ├── loader.js                 the few lines inside the loader profile
│   ├── gen.py                    generates Settings.cs and TestScenarios.cs
│   ├── check.py                  static checks, does not compile
│   ├── make_profile.py           builds the .ledsprofile file
│   ├── make-release.ps1          build + checksum + installer in one go
│   └── harness.js, test_*.js     mock tests of the script
└── plugin/Shiftline/             the plugin (Visual Studio project)
    ├── Shiftline.csproj          project file, lists every source file
    ├── Shiftline.cs              plugin entry point: Init, DataUpdate, actions
    ├── Settings.cs               GENERATED: the simple settings
    ├── Settings.App.cs           not generated: special settings (active preset, ...)
    ├── Settings.Themes.cs        not generated: the built-in colour themes
    ├── TestScenarios.cs          GENERATED: the test scenarios
    ├── SettingsControl.xaml(.cs) the frame with navigation and search
    ├── HomePage / LedPage / PresetsPage / ColoursPage / SettingsPage
    │                             the five pages (.xaml + .xaml.cs)
    ├── ShiftlineStyles.xaml      the look: colours, cards, switches, tabs
    ├── SlSlider, ColourPickerButton, GradientBar, LedPreview, IconFactory, IconTitle
    │                             small custom controls
    ├── PresetStore.cs            presets, known cars and games
    ├── ProfileStore.cs           settings profiles and backups
    ├── UpdateService.cs          update check and installation via GitHub releases
    ├── ScriptSource.cs           reads the embedded script from the DLL
    ├── PluginLog.cs, Converters.cs
    ├── Assets/                   background image, logo
    └── Properties/AssemblyInfo.cs   the version number
```

**Do not edit `Settings.cs` and `TestScenarios.cs` by hand.** `tools/gen.py` overwrites them.

## How a setting travels

Example: `RpmFlashColour`.

1. **Defined** in `SL_DEFAULTS` in `tools/Shiftline.plugin.js`. The default value there also decides the type (boolean, number, text, colour).
2. **Generated:** `gen.py` turns it into a property in `Settings.cs`.
3. **Control:** a page binds a control to it, for example `Colour="{Binding RpmFlashColour, Mode=TwoWay}"` in `LedPage.xaml`.
4. **Stored:** the plugin saves it with SimHub's plugin settings and writes it into the active preset (`PresetStore.cs`).
5. **Published:** `Shiftline.cs` attaches a SimHub property `Shiftline.RpmFlashColour` for every setting in `Init`.
6. **Read:** the script reads all values in `sl_loadConfig()` and uses them when it computes the LEDs.

So a new setting is always: `SL_DEFAULTS` first, then `gen.py`, then a control on a page. `check.py` reports settings without a control.

## How the script builds a frame

The entry point is `sl_frame()` near the end of `tools/Shiftline.plugin.js`. It returns 48 colours, one per possible LED. Only as many as the configured layout are used.

- The script paints **layers** on top of each other. The list is `SL_LAYER_DEFS` (id, label, function): RPM and centre display, brake bar, flags, spotter, pit lane, ERS, fuel and more. The user can change the priority order in the settings.
- When no game is running, `sl_idleLayer` shows the idle animation.
- Brightness and night mode are applied to the whole frame at the end.
- The effects master switch (`FxMasterOn`) drops every layer except RPM and brake.

## Recipes

### Change a default value
1. Edit it in `SL_DEFAULTS` in `tools/Shiftline.plugin.js`.
2. Run `gen.py`.
3. Check whether a test expects the old value (for example `tools/test_layout.js`).
4. Rebuild the plugin, because the script lives inside the DLL.

Existing presets keep their saved values. Use the reset option in the settings to see new defaults.

### Add a setting
1. Add name and default to `SL_DEFAULTS`.
2. Read it in `sl_loadConfig()` and use it where it should take effect.
3. Run `gen.py`.
4. Add a control to a page, usually `LedPage.xaml`. Copying a similar control is the easiest way.
5. Add a test, then run `check.py` and the tests.

### Add an effect
1. Write a layer function (use `sl_spotterLayer` as a template).
2. Register it in `SL_LAYER_DEFS`.
3. Add its switch (`FxYourEffectOn`) and colours to `SL_DEFAULTS`, run `gen.py`.
4. Add a card on the Effects tab of `LedPage.xaml`.
5. Add a line to `tools/test_effects.js`.

### Change the look
- Colours, cards, switches, tabs: `plugin/Shiftline/ShiftlineStyles.xaml`.
- Background and logo: `plugin/Shiftline/Assets/`.
- The SimHub menu icon is drawn in `IconFactory.cs`.

### Change a built-in colour theme
Edit `BuiltInThemes` in `plugin/Shiftline/Settings.Themes.cs`. Built-in themes are reset to these values on start.

### Change the installer
Edit `installer/Shiftline.iss`. Page texts are in the `[Code]` section and in `installer/info.txt`.

### Make a release
See [RELEASE.md](RELEASE.md). In short: set the version in `Properties/AssemblyInfo.cs` and in `installer/Shiftline.iss`, run `tools/make-release.ps1`, then publish the three files as a GitHub release.

## Where user data lives

```
SimHub folder
├── Shiftline.Plugin.dll
└── PluginsData
    ├── Common\Shiftline.GeneralSettings.json   current settings
    └── Shiftline\
        ├── Presets\          one JSON file per preset
        ├── Backups\          settings backups
        ├── KnownCars.json, KnownGames.json
        └── Update\           update log and backup of the previous DLL
C:\Users\Public\Documents\Shiftline\            loader profile and readme (installer)
```

## Things to know

- The script is part of the DLL: after a script change, rebuild the plugin.
- After adding a file or page to the project, use *Reload Project* in Visual Studio. New pages need entries in `Shiftline.csproj`.
- Close SimHub before a Debug build, otherwise the DLL is locked.
- Generate the `.sha256` file after the last build, ideally with `make-release.ps1`. A stale checksum makes every update fail.
- `AssemblyInfo.cs`, `installer/Shiftline.iss` and the release tag must carry the same version.
- `check.py` and the Node tests cannot find compile errors. Always build in Visual Studio.
- Problems in the script show up in SimHub's system log and in the plugin's Settings page (system log).
