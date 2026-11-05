<#
  Builds the release files of Shiftline in one go and puts them into <repo>\release\<version>\ :
    Shiftline-Setup-<version>.exe      the installer
    Shiftline.Plugin.dll               the plugin
    Shiftline.Plugin.dll.sha256        the checksum of that DLL
  Upload these three files to the GitHub release.

  Usage (from the repository folder):
    powershell -ExecutionPolicy Bypass -File tools\make-release.ps1
    powershell -ExecutionPolicy Bypass -File tools\make-release.ps1 -SkipBuild    # package the DLL that is already built

  Needs: Visual Studio (MSBuild), Inno Setup 6, and the environment variable SIMHUB_INSTALL_PATH.
  The version comes from AssemblyInformationalVersion in plugin\Shiftline\Properties\AssemblyInfo.cs.
#>
param([switch]$SkipBuild)

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

# --- 3. installer (it packs the DLL from bin\Release)
$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path $iscc)) { throw 'Inno Setup 6 not found (ISCC.exe)' }
Write-Host 'Compiling the installer ...'
& $iscc "/DAppVersion=$version" (Join-Path $root 'installer\Shiftline.iss') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The installer could not be compiled' }
$setup = Join-Path $root "installer\Output\Shiftline-Setup-$version.exe"
if (-not (Test-Path $setup)) { throw "The installer was not created: $setup" }

# --- 4. collect the three files and check them against each other
$out = Join-Path $root "release\$version"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item $dll $out
Copy-Item $setup $out
[IO.File]::WriteAllText((Join-Path $out 'Shiftline.Plugin.dll.sha256'), $hash, [Text.Encoding]::ASCII)

$copied = (Get-FileHash (Join-Path $out 'Shiftline.Plugin.dll') -Algorithm SHA256).Hash
$written = (Get-Content (Join-Path $out 'Shiftline.Plugin.dll.sha256') -Raw).Trim()
if ($copied -ne $written) { throw 'The checksum file does not match the DLL in the release folder' }

Write-Host ''
Write-Host "Done. Upload these files to the GitHub release v$($version):" -ForegroundColor Green
Get-ChildItem $out | ForEach-Object { Write-Host ('  {0}  ({1:N0} bytes)' -f $_.Name, $_.Length) }
Invoke-Item $out
