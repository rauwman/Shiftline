# Releasing Shiftline

How to make a release that people can install with the installer and that the plugin can update itself to (Home page, "Updates").

## 1. Set the version

Edit the three entries in `plugin/Shiftline/Properties/AssemblyInfo.cs`, for example for version 0.2.1:

```
[assembly: AssemblyVersion("0.2.1.0")]
[assembly: AssemblyFileVersion("0.2.1.0")]
[assembly: AssemblyInformationalVersion("0.2.1")]
```

- The four-part number holds major, minor and patch only (the fourth part stays 0).
- `AssemblyInformationalVersion` is the version the plugin shows and compares with the newest release. A pre-release carries a suffix, for example `0.2.2-beta.1`. It has to match the first three numbers of the four-part version, otherwise the updater rejects the downloaded DLL.
- Set the same version as the default of `AppVersion` in `installer/Shiftline.iss`.

## 2. Build the release files

Close nothing, SimHub may keep running. From the repository folder:

```powershell
powershell -ExecutionPolicy Bypass -File tools\make-release.ps1
```

The script builds the Release DLL, creates the checksum of that DLL, compiles the installer and puts all three files into `release\<version>\` (the folder opens when it is done). It checks that the checksum file matches the DLL it copied. It needs Visual Studio, Inno Setup 6 and `SIMHUB_INSTALL_PATH`. With `-SkipBuild` it packages the DLL that is already in `plugin\Shiftline\bin\Release`.

Always upload the files from that folder. The order matters: the checksum has to be created after the **last** build of the DLL, and the installer after that. Doing it by hand: build Release in Visual Studio, `(Get-FileHash .\Shiftline.Plugin.dll -Algorithm SHA256).Hash | Set-Content -NoNewline Shiftline.Plugin.dll.sha256`, then compile `installer\Shiftline.iss` with Inno Setup 6 (`ISCC.exe /DAppVersion=0.2.1 installer\Shiftline.iss`).

The installer:
- copies `Shiftline.Plugin.dll` into the SimHub folder (the one with `SimHubWPF.exe`, default `C:\Program Files (x86)\SimHub`) and asks to close SimHub if it is running,
- puts the loader profile and the README into `C:\Users\Public\Documents\Shiftline`,
- when Shiftline is already installed, the first page offers "Update or repair" or "Uninstall". "Uninstall" starts the normal Windows uninstaller (also reachable from the Start menu and Windows Settings > Apps): it closes SimHub, asks whether to delete the settings and presets (`PluginsData\Shiftline` and `PluginsData\Common\Shiftline.GeneralSettings.json`) and offers to start SimHub again. The imported LED profile has to be removed in SimHub by hand.

## 3. Publish on GitHub

1. Commit and push everything, so that `main` holds the final state.
2. *Releases > Draft a new release.* Tag `v0.2.1` (always with a `v`; pre-releases with a suffix, for example `v0.2.2-beta.1`), target `main`.
3. Tick **Set as a pre-release** for versions with a `-beta` suffix. The plugin finds pre-releases too.
4. Attach `Shiftline.Plugin.dll`, `Shiftline.Plugin.dll.sha256` and `Shiftline-Setup-<version>.exe`. The file names of the DLL and the checksum have to be exactly these, otherwise the plugin does not find them.
5. The release notes show up behind the "Release notes" button in the plugin.

## How the update works for the user

1. The plugin asks GitHub (after SimHub starts, or with "Check for updates").
2. "Install and restart SimHub" downloads the DLL and checks the checksum, the assembly name and the version.
3. SimHub closes, a small PowerShell script replaces the DLL (Windows asks for administrator rights when needed) and starts SimHub again.
4. The old DLL is kept in `PluginsData\Shiftline\Update\previous`, the log is `PluginsData\Shiftline\Update\update.log`.

The repository has to be public, otherwise the plugin cannot read the releases. The update installer is untested so far, see the README.
