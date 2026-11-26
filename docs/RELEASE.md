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

The script builds the Release DLL, creates the checksum of that DLL, zips the track shapes (`data\tracks`) into `Shiftline.Tracks.zip` with its checksum, compiles the installer and puts all five files into `release\<version>\` (the folder opens when it is done). It checks that the checksum files match the files it copied, and it stops when `data\tracks` has changes that are not committed. It needs Visual Studio, Inno Setup 6 and `SIMHUB_INSTALL_PATH`. With `-SkipBuild` it packages the DLL that is already in `plugin\Shiftline\bin\Release`.

Before it builds, the script compares the SimHub version named in `README.md` ("SimHub x.y.z") with the installed `SimHubWPF.exe` and stops when they differ. Run it once with `-UpdateReadme`: it writes the installed version into the README; commit that and run the script again.

Always upload the files from that folder. The order matters: the checksum has to be created after the **last** build of the DLL, and the installer after that. Doing it by hand: build Release in Visual Studio, `(Get-FileHash .\Shiftline.Plugin.dll -Algorithm SHA256).Hash | Set-Content -NoNewline Shiftline.Plugin.dll.sha256`, zip `data\tracks` into `installer\Output\Shiftline.Tracks.zip` (entry names with `/`), write the version into `installer\Output\Shiftline.Tracks.version`, create the checksum of the zip, then compile `installer\Shiftline.iss` with Inno Setup 6 (`ISCC.exe /DAppVersion=0.2.1 installer\Shiftline.iss`). Using the script is much safer.

The installer:
- copies `Shiftline.Plugin.dll` into the SimHub folder (the one with `SimHubWPF.exe`, default `C:\Program Files (x86)\SimHub`) and asks to close SimHub if it is running,
- puts the loader profile and the README into `C:\Users\Public\Documents\Shiftline`, and the licences of the fonts, flags and track data into its `Licenses` folder,
- puts the track shapes (`Shiftline.Tracks.zip` and `Shiftline.Tracks.version`) into `C:\Users\Public\Documents\Shiftline\Tracks`, replacing them completely; uninstalling removes that folder,
- when Shiftline is already installed, the first page offers "Update or repair" or "Uninstall". "Uninstall" starts the normal Windows uninstaller (also reachable from the Start menu and Windows Settings > Apps): it closes SimHub, asks whether to delete the settings and presets (`PluginsData\Shiftline` and `PluginsData\Common\Shiftline.GeneralSettings.json`) and offers to start SimHub again. The imported LED profile has to be removed in SimHub by hand.

## 3. Publish on GitHub

1. Commit and push everything, so that `main` holds the final state.
2. *Releases > Draft a new release.* Tag `v0.2.1` (always with a `v`; pre-releases with a suffix, for example `v0.2.2-beta.1`), target `main`.
3. Tick **Set as a pre-release** for versions with a `-beta` suffix. The plugin finds pre-releases too.
4. Attach all five files of `release\<version>\`: `Shiftline.Plugin.dll`, `Shiftline.Plugin.dll.sha256`, `Shiftline.Tracks.zip`, `Shiftline.Tracks.zip.sha256` and `Shiftline-Setup-<version>.exe`. The names have to be exactly these, otherwise the plugin does not find them. There is one release for everything: new or changed tracks reach users only with a plugin release.
5. The release notes show up behind the "Release notes" button in the plugin.

## How the update works for the user

1. The plugin asks GitHub (after SimHub starts, or with "Check for updates").
2. "Install and restart SimHub" downloads the DLL and checks the checksum, the assembly name and the version. It also downloads the track shapes of the same release and checks their checksum.
3. SimHub closes, a small PowerShell script replaces the DLL (Windows asks for administrator rights when needed) and the track shapes, and starts SimHub again.
4. At every start the plugin checks that `Public\Documents\Shiftline\Tracks` holds the track shapes of its own version (`Shiftline.Tracks.version`). If not (an update by an older plugin that only replaced the DLL, or a DLL copied by hand), it downloads `Shiftline.Tracks.zip` of its own release tag once and checks the checksum. On failure the Track Map shows a ring or a line and the next start tries again. Debug builds skip this; building puts the current `data\tracks` there.
5. The old DLL is kept in `PluginsData\Shiftline\Update\previous`, the log is `PluginsData\Shiftline\Update\update.log`.

The repository has to be public, otherwise the plugin cannot read the releases.
