<#
  Builds the release files of Shiftline in one go and puts them into <repo>\release\<version>\ :
    Shiftline-Setup-<version>.exe      the installer
    Shiftline.Plugin.dll               the plugin
    Shiftline.Plugin.dll.sha256        the checksum of that DLL
    Shiftline.Tracks.zip               the track shapes (data\tracks), installed next to the plugin's data
    Shiftline.Tracks.zip.sha256        the checksum of that zip
  Upload these files to the GitHub release. The plugin and its updater need the exact names.

  Usage (from the repository folder):
    powershell -ExecutionPolicy Bypass -File tools\make-release.ps1
    powershell -ExecutionPolicy Bypass -File tools\make-release.ps1 -SkipBuild    # package the DLL that is already built
    powershell -ExecutionPolicy Bypass -File tools\make-release.ps1 -UpdateReadme # write the installed SimHub version into the README

  Before building, the script compares "SimHub x.y.z" in README.md with the installed SimHubWPF.exe and stops when they
  differ. -UpdateReadme writes the installed version into the README; commit it and run the script again.

  Needs: Visual Studio (MSBuild), Inno Setup 6, and the environment variable SIMHUB_INSTALL_PATH.
  The version comes from AssemblyInformationalVersion in plugin\Shiftline\Properties\AssemblyInfo.cs.
#>
param([switch]$SkipBuild, [switch]$UpdateReadme)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'plugin\Shiftline\Shiftline.csproj'
$dll = Join-Path $root 'plugin\Shiftline\bin\Release\Shiftline.Plugin.dll'

# --- version: the informational version has to match the first three numbers of the assembly version
$info = Get-Content (Join-Path $root 'plugin\Shiftline\Properties\AssemblyInfo.cs') -Raw
if ($info -notmatch 'AssemblyInformationalVersion\("([^"]+)"\)') { throw 'AssemblyInformationalVersion not found in AssemblyInfo.cs' }
$version = $Matches[1]
if ($info -notmatch '\[assembly: AssemblyVersion\("(\d+)\.(\d+)\.(\d+)\.\d+"\)\]') { throw 'AssemblyVersion not found in AssemblyInfo.cs' }
$numbers = "$($Matches[1]).$($Matches[2]).$($Matches[3])"
if (-not $version.StartsWith($numbers)) { throw "AssemblyInformationalVersion ($version) does not match AssemblyVersion ($numbers)" }
Write-Host "Version $version"

# --- SimHub version: the README names the version the plugin was tested with, which is the one installed here
if ($env:SIMHUB_INSTALL_PATH) {
    $simhubExe = Join-Path $env:SIMHUB_INSTALL_PATH 'SimHubWPF.exe'
    if (-not (Test-Path $simhubExe)) { throw "SimHubWPF.exe not found in $env:SIMHUB_INSTALL_PATH" }
    # SimHub's own files all carry 1.0.0.0; the real version is the one its installer wrote into the Windows app list
    $keys = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
    $candidates = @(Get-ItemProperty $keys -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like 'SimHub version*' -and $_.DisplayVersion } |
        ForEach-Object { $_.DisplayVersion })
    $simhub = $null
    foreach ($candidate in $candidates) {
        if ($candidate -match '(\d+\.\d+\.\d+)') { $simhub = $Matches[1]; break }
    }
}
if ($env:SIMHUB_INSTALL_PATH -and -not $simhub) {
    Write-Warning "The SimHub version was not found in the Windows app list. The README check is skipped."
}
elseif ($simhub) {
    $readmePath = Join-Path $root 'README.md'
    $readme = [System.IO.File]::ReadAllText($readmePath)
    $named = [regex]::Matches($readme, 'SimHub (\d+\.\d+\.\d+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    $wrong = @($named | Where-Object { $_ -ne $simhub })
    if ($wrong.Count -gt 0) {
        if (-not $UpdateReadme) {
            throw "README.md names SimHub $($wrong -join ', '), installed is $simhub. Run with -UpdateReadme, then commit the README and run again."
        }
        $readme = [regex]::Replace($readme, 'SimHub \d+\.\d+\.\d+', "SimHub $simhub")
        [System.IO.File]::WriteAllText($readmePath, $readme, (New-Object System.Text.UTF8Encoding($true)))
        throw "README.md now names SimHub $simhub. Commit it and run the script again."
    }
    Write-Host "SimHub $simhub (README matches)"
}

# --- 1. build the release DLL
if (-not $SkipBuild) {
    if (-not $env:SIMHUB_INSTALL_PATH) { throw 'The environment variable SIMHUB_INSTALL_PATH is not set' }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found (is Visual Studio installed?)' }
    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { throw 'MSBuild not found' }
    Write-Host 'Building (Release) ...'
    & $msbuild $project /t:Rebuild /p:Configuration=Release /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw 'The build failed' }
}
if (-not (Test-Path $dll)) { throw "The DLL does not exist: $dll" }

# --- 2. checksum of the DLL that was just built
$hash = (Get-FileHash $dll -Algorithm SHA256).Hash
Write-Host "SHA256 $hash"

# --- 3a. track shapes: data\tracks as Shiftline.Tracks.zip (entry names with "/") and the version they belong to
$tracksDir = Join-Path $root 'data\tracks'
if (-not (Test-Path (Join-Path $tracksDir 'manifest.json'))) { throw 'data\tracks\manifest.json not found' }
$changed = git -C $root status --porcelain -- data/tracks
if ($changed) { throw 'data\tracks has changes that are not committed. Commit them (or undo them) first, the release has to match Git.' }
$staging = Join-Path $root 'installer\Output'
New-Item -ItemType Directory -Force $staging | Out-Null
$tracksZip = Join-Path $staging 'Shiftline.Tracks.zip'
if (Test-Path $tracksZip) { Remove-Item $tracksZip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($tracksZip, 'Create')
try {
    $base = (Resolve-Path $tracksDir).Path.TrimEnd('\') + '\'
    Get-ChildItem $tracksDir -Recurse -File | Sort-Object FullName | ForEach-Object {
        $entry = $_.FullName.Substring($base.Length).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal)
    }
}
finally { $zip.Dispose() }
[IO.File]::WriteAllText((Join-Path $staging 'Shiftline.Tracks.version'), $version, [Text.Encoding]::ASCII)
$tracksHash = (Get-FileHash $tracksZip -Algorithm SHA256).Hash
Write-Host "Track shapes: $((Get-Item $tracksZip).Length) bytes, SHA256 $tracksHash"

# --- 3. installer (it packs the DLL from bin\Release and the track shapes from installer\Output)
$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path $iscc)) { throw 'Inno Setup 6 not found (ISCC.exe)' }
Write-Host 'Compiling the installer ...'
& $iscc "/DAppVersion=$version" (Join-Path $root 'installer\Shiftline.iss') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The installer could not be compiled' }
$setup = Join-Path $root "installer\Output\Shiftline-Setup-$version.exe"
if (-not (Test-Path $setup)) { throw "The installer was not created: $setup" }

# --- 4. collect the files and check them against each other
$out = Join-Path $root "release\$version"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item $dll $out
Copy-Item $setup $out
[IO.File]::WriteAllText((Join-Path $out 'Shiftline.Plugin.dll.sha256'), $hash, [Text.Encoding]::ASCII)

$copied = (Get-FileHash (Join-Path $out 'Shiftline.Plugin.dll') -Algorithm SHA256).Hash
$written = (Get-Content (Join-Path $out 'Shiftline.Plugin.dll.sha256') -Raw).Trim()
if ($copied -ne $written) { throw 'The checksum file does not match the DLL in the release folder' }
Copy-Item $tracksZip $out
[IO.File]::WriteAllText((Join-Path $out 'Shiftline.Tracks.zip.sha256'), $tracksHash, [Text.Encoding]::ASCII)
if ((Get-FileHash (Join-Path $out 'Shiftline.Tracks.zip') -Algorithm SHA256).Hash -ne $tracksHash) { throw 'The checksum file does not match the track zip in the release folder' }

Write-Host ''
Write-Host "Done. Upload these files to the GitHub release v$($version):" -ForegroundColor Green
Get-ChildItem $out | ForEach-Object { Write-Host ('  {0}  ({1:N0} bytes)' -f $_.Name, $_.Length) }
Invoke-Item $out
