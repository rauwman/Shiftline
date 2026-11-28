<#
  Developer menu for Shiftline. Short, readable output that can be pasted into a chat.

  Usage (from the repository folder):
    powershell -ExecutionPolicy Bypass -File tools\dev.ps1            # menu
    powershell -ExecutionPolicy Bypass -File tools\dev.ps1 -Run start # one item without the menu

  Items: start (chat start summary), build (Debug build, errors only), check (gen.py, check.py, Node tests),
  commit (proposals from .dev\COMMIT.txt or a simple suggestion, key l = suggestion from a local Ollama model, trailer, asks before push),
  release (version, notes check, test points, build, tag, GitHub release; with -DryRun it only shows the steps),
  ollama (status, load, unload, close the local model). tools\dev.cmd starts the menu with a double click.
  Output of start/build/check goes to .dev\*.txt and the clipboard, the release summary to .dev\RELEASE.txt.
  Only the commit and release items write to Git (add, commit, tag, push, always after asking). The other items only read it.
#>
param([string]$Run = '', [switch]$DryRun)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dev = Join-Path $root '.dev'

# The chats of the project. To rename a chat or add one, change only this list (the menu and the filters follow).
# Name = menu text and heading of START.txt; Areas = issue areas shown; Ids = INTERCHAT id prefixes;
# Match = regex for the chat name as written in INTERCHAT (old and new name).
$script:Chats = @(
    @{ Name = 'Basis';      Areas = @('no area');                           Ids = @('BA');       Match = 'Basis|Plugin' },
    @{ Name = 'Features';   Areas = @('area: led', 'area: overlays', 'area: track map'); Ids = @('OV', 'FE'); Match = 'Features|Overlays' },
    @{ Name = 'Track Data'; Areas = @('area: track data', 'area: track map'); Ids = @('TD');     Match = 'Track Data' }
)

# Local model for commit suggestions (key l in item 7). To swap the model, change only this block.
$script:LocalModel = 'gpt-oss:20b'
$script:LocalThink = 'medium'      # gpt-oss: low, medium or high. For other models use $false.
$script:OllamaUrl = 'http://localhost:11434'
$script:LocalContext = 16384       # context in tokens
$script:LocalKeepAlive = '30m'     # how long the model stays in VRAM after use; menu item 10 (u) frees it at once
$script:LocalPrewarm = $true       # item 7 loads the model in the background when Ollama already runs
$script:OllamaApp = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama app.exe'   # started when Ollama is not running
$script:LocalDiffLimit = 24000     # characters of diff sent to the model
$script:ListOnly = '^(plugin/Shiftline/(Settings|TabSettings|TestScenarios)\.cs|data/tracks/)'   # generated or data files: only the name is sent

$script:out = New-Object System.Collections.Generic.List[string]
function Out-Line([string]$text) { Write-Host $text; $script:out.Add($text) }
function Write-Head([string]$text) { Write-Host ''; Write-Host ('== ' + $text) -ForegroundColor Cyan; $script:out.Add(''); $script:out.Add('== ' + $text) }

function Show-ChatStart {
    param([string]$Chat)
    $cfg = $script:Chats | Where-Object { $_.Name -eq $Chat } | Select-Object -First 1
    $script:out.Clear()
    $script:out.Add('Shiftline chat start' + $(if ($Chat) { ': ' + $Chat } else { '' }) + ', ' + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
    Push-Location $root
    try {
        Write-Head 'Git'
        git fetch --quiet origin 2>$null
        $branch = (git rev-parse --abbrev-ref HEAD).Trim()
        $counts = (git rev-list --left-right --count "HEAD...origin/$branch" 2>$null)
        if ($counts) {
            $parts = $counts -split '\s+'
            Out-Line ("branch $branch, ahead $($parts[0]), behind $($parts[1])")
        } else {
            Out-Line ("branch $branch (no remote branch)")
        }
        $dirty = @(git status --short)
        if ($dirty.Count -eq 0) { Out-Line 'working tree clean' }
        else { Out-Line ("changed files: $($dirty.Count)"); $dirty | Select-Object -First 15 | ForEach-Object { Out-Line ('  ' + $_) } }
        $tag = (git describe --tags --abbrev=0 2>$null)
        if ($tag) {
            $since = (git rev-list --count "$tag..HEAD").Trim()
            Out-Line ("last tag $tag, $since commits since")
        }
        Out-Line 'last commits:'
        git log --oneline -5 | ForEach-Object { Out-Line ('  ' + $_) }

        Write-Head 'Issues (open)'
        if (Get-Command gh -ErrorAction SilentlyContinue) {
            $json = gh issue list --state open --limit 100 --json number,title,labels,milestone 2>$null
            if ($json) {
                $issues = $json | ConvertFrom-Json
                $groups = @{}
                foreach ($i in $issues) {
                    $area = ($i.labels | Where-Object { $_.name -like 'area:*' } | Select-Object -First 1).name
                    if (-not $area) { $area = 'no area' }
                    if (-not $groups.ContainsKey($area)) { $groups[$area] = @() }
                    $ms = ''
                    if ($i.milestone) { $ms = ' [' + $i.milestone.title + ']' }
                    $groups[$area] += ('#' + $i.number + ' ' + $i.title + $ms)
                }
                $keys = $groups.Keys | Sort-Object
                if ($Chat) { $keys = $keys | Where-Object { $cfg.Areas -contains $_ } }
                foreach ($k in $keys) {
                    Out-Line ($k + ':')
                    $groups[$k] | ForEach-Object { Out-Line ('  ' + $_) }
                }
            } else { Out-Line 'gh returned nothing (not logged in?)' }
        } else { Out-Line 'gh not found' }

        Write-Head 'Notes'
        $conflicts = @(Get-ChildItem $dev -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '*conflicted copy*' })
        if ($conflicts.Count -gt 0) { $conflicts | ForEach-Object { Out-Line ('CONFLICT: ' + $_.Name) } }
        else { Out-Line 'no conflicted copies' }
        $inter = Join-Path $dev 'INTERCHAT.md'
        if (Test-Path $inter) {
            $open = @(Get-Content $inter -Encoding UTF8 | Where-Object { $_ -match 'Status: open' })
            if ($Chat) {
                $who = 'to [^,]*(' + $cfg.Match + ')'
                $ans = 'answer to (' + ($cfg.Ids -join '|') + ')-'
                $open = @($open | Where-Object { $_ -match $who -or $_ -match $ans })
            }
            $label = 'open INTERCHAT items'
            if ($Chat) { $label += ' addressed to ' + $Chat }
            Out-Line ($label + ': ' + $open.Count)
            foreach ($line in $open) {
                $t = $line.Trim()
                if ($t.Length -gt 160) { $t = $t.Substring(0, 160) + '...' }
                Out-Line ('  ' + $t)
            }
        }
    } finally { Pop-Location }
    $file = Join-Path $dev 'START.txt'
    $script:out | Set-Content -Path $file -Encoding UTF8
    try { ($script:out -join "`r`n") | Set-Clipboard } catch { }
    Write-Host ''
    Write-Host ('Saved to .dev\START.txt and copied to the clipboard.') -ForegroundColor Green
}

function Save-Out([string]$name) {
    $file = Join-Path $dev $name
    $script:out | Set-Content -Path $file -Encoding UTF8
    try { ($script:out -join "`r`n") | Set-Clipboard } catch { }
    Write-Host ''
    Write-Host ('Saved to .dev\' + $name + ' and copied to the clipboard.') -ForegroundColor Green
}

function Find-Python {
    foreach ($c in @('py', 'python', 'python3')) {
        if (Get-Command $c -ErrorAction SilentlyContinue) { return $c }
    }
    return $null
}

function Invoke-Build {
    $ErrorActionPreference = 'Continue'
    $script:out.Clear()
    $script:out.Add('Shiftline build (Debug), ' + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
    if (-not $env:SIMHUB_INSTALL_PATH) { Out-Line 'SIMHUB_INSTALL_PATH is not set'; Save-Out 'BUILD.txt'; return }
    if (Get-Process -Name 'SimHubWPF' -ErrorAction SilentlyContinue) {
        Write-Host 'SimHub is running. A Debug build writes the DLL into the SimHub folder, so close SimHub first.' -ForegroundColor Yellow
        $a = Read-Host 'Build anyway? (y/n)'
        if ($a -ne 'y') { return }
    }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { Out-Line 'vswhere.exe not found'; Save-Out 'BUILD.txt'; return }
    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { Out-Line 'MSBuild not found'; Save-Out 'BUILD.txt'; return }
    $project = Join-Path $root 'plugin\Shiftline\Shiftline.csproj'
    Write-Host 'Building ...'
    $lines = & $msbuild $project /t:Build /p:Configuration=Debug /v:minimal /nologo 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $prefix = $root + '\'
    $errors = @($lines | Where-Object { $_ -match ': error ' } | ForEach-Object { ($_ -replace [regex]::Escape($prefix), '') -replace '\s*\[[^\]]+\.csproj\]$', '' } | Select-Object -Unique)
    $warnings = @($lines | Where-Object { $_ -match ': warning ' } | ForEach-Object { ($_ -replace [regex]::Escape($prefix), '') -replace '\s*\[[^\]]+\.csproj\]$', '' } | Select-Object -Unique)
    Write-Head 'Result'
    if ($code -eq 0) { Out-Line ('build OK, ' + $warnings.Count + ' warnings') } else { Out-Line ('build FAILED (exit ' + $code + '), ' + $errors.Count + ' errors, ' + $warnings.Count + ' warnings') }
    if ($errors.Count -gt 0) { Write-Head 'Errors'; $errors | Select-Object -First 30 | ForEach-Object { Out-Line $_ } }
    if ($warnings.Count -gt 0) { Write-Head 'Warnings'; $warnings | Select-Object -First 15 | ForEach-Object { Out-Line $_ } }
    if ($code -ne 0 -and $errors.Count -eq 0) { Write-Head 'Last lines'; $lines | Select-Object -Last 15 | ForEach-Object { Out-Line $_ } }
    Save-Out 'BUILD.txt'
}

function Invoke-Check {
    # Native commands write to stderr (Python tracebacks); with 'Stop' PowerShell 5.1 would abort on the first line.
    $ErrorActionPreference = 'Continue'
    $script:out.Clear()
    $script:out.Add('Shiftline checks, ' + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
    $tools = Join-Path $root 'tools'
    Push-Location $tools
    try {
        $py = Find-Python
        Write-Head 'Python'
        if (-not $py) { Out-Line 'python not found' }
        else {
            $g = & $py -I gen.py ../plugin/Shiftline 2>&1 | ForEach-Object { "$_" }
            if ($LASTEXITCODE -eq 0) { Out-Line 'gen.py OK' } else { Out-Line ('gen.py FAILED (exit ' + $LASTEXITCODE + ')'); $g | Select-Object -Last 10 | ForEach-Object { Out-Line ('  ' + $_) } }
            $c = & $py -I check.py ../plugin/Shiftline gen_summary.json 2>&1 | ForEach-Object { "$_" }
            if ($LASTEXITCODE -eq 0) { Out-Line 'check.py OK' } else {
                Out-Line ('check.py FAILED (exit ' + $LASTEXITCODE + ')')
                $c | Where-Object { $_ -match 'FAIL|Error|error' } | Select-Object -First 20 | ForEach-Object { Out-Line ('  ' + $_) }
            }
            $genFiles = @('../plugin/Shiftline/Settings.cs', '../plugin/Shiftline/TestScenarios.cs', '../plugin/Shiftline/TabSettings.cs')
            $gen = @(git status --short -- $genFiles)
            if ($gen.Count -gt 0) {
                $real = @(git diff --ignore-space-at-eol --name-only -- $genFiles)
                if ($real.Count -gt 0) { Out-Line 'generated files changed by gen.py (commit them):' } else { Out-Line 'generated files differ from Git only in line endings (CRLF/LF):' }
                $gen | ForEach-Object { Out-Line ('  ' + $_) }
            }
        }
        Write-Head 'Node tests'
        if (-not (Get-Command node -ErrorAction SilentlyContinue)) { Out-Line 'node not found' }
        else {
            foreach ($t in @('test_modes.js', 'test_layout.js', 'test_ui_values.js', 'test_effects.js', 'test_loader.js')) {
                $o = @(& node $t 2>&1 | ForEach-Object { "$_" })
                $code = $LASTEXITCODE
                $fails = @($o | Where-Object { $_ -match '^(FAIL|EXC|bad colour)' })
                $errs = $o | Where-Object { $_ -match 'errs:\s*([1-9]\d*)' }
                $failed = ($code -ne 0) -or ($fails.Count -gt 0) -or $errs
                if ($failed) {
                    Out-Line ($t + ' FAILED')
                    $fails | Select-Object -First 15 | ForEach-Object { Out-Line ('  ' + $_) }
                    if ($errs) { Out-Line ('  ' + $errs) }
                } else { Out-Line ($t + ' OK') }
            }
        }
    } finally { Pop-Location }
    Save-Out 'CHECK.txt'
}

# --- commit helpers -------------------------------------------------------------------------------------------------
# .dev\COMMIT.txt holds proposals written by the chat that made the changes. Blocks are separated by a line with ---
#   files: tools/check.py, tools/gen.py      (comma separated, paths as in git status)
#   subject: Fix check.py ...                (English, imperative)
#   body: one optional line
#   issue: 12                                (optional)

function Convert-CommitBlock([string[]]$lines) {
    $b = @{ Files = @(); Subject = ''; Body = ''; Issue = ''; Raw = ($lines -join "`r`n") }
    foreach ($l in $lines) {
        if ($l -match '^\s*#' -or -not $l.Trim()) { continue }
        $i = $l.IndexOf(':')
        if ($i -lt 1) { continue }
        $key = $l.Substring(0, $i).Trim().ToLower()
        $val = $l.Substring($i + 1).Trim()
        switch ($key) {
            'files'   { $b.Files = @($val -split '\s*,\s*' | Where-Object { $_ } | ForEach-Object { $_.Replace('\', '/') }) }
            'subject' { $b.Subject = $val }
            'body'    { $b.Body = $val }
            'issue'   { $b.Issue = $val.TrimStart('#') }
        }
    }
    if ($b.Files.Count -eq 0 -or -not $b.Subject) { return $null }
    return $b
}

function Read-CommitFile {
    $file = Join-Path $dev 'COMMIT.txt'
    $blocks = @()
    if (-not (Test-Path $file)) { return $blocks }
    $cur = @()
    foreach ($line in (@(Get-Content $file -Encoding UTF8) + @('---'))) {
        if ($line.Trim() -eq '---') {
            if ($cur.Count -gt 0) {
                $b = Convert-CommitBlock $cur
                if ($b) { $blocks += $b }
            }
            $cur = @()
        } else { $cur += $line }
    }
    return $blocks
}

function Get-ChangedFiles {
    $list = @()
    foreach ($l in @(git status --porcelain -uall)) {
        $p = $l.Substring(3)
        if ($p -match ' -> ') { $p = ($p -split ' -> ')[-1] }
        $list += $p.Trim('"')
    }
    return $list
}

function Get-FallbackSuggestion([string[]]$files) {
    $names = @($files | ForEach-Object { Split-Path $_ -Leaf })
    $shown = (($names | Select-Object -First 3) -join ', ')
    if ($names.Count -gt 3) { $shown += ' and ' + ($names.Count - 3) + ' more' }
    $new = 0
    foreach ($f in $files) { if (-not (git ls-files -- $f)) { $new++ } }
    $verb = 'Update'
    if ($new -eq $files.Count) { $verb = 'Add' }
    $parts = @()
    $stat = (git diff HEAD --shortstat -- $files 2>$null)
    if ($stat) { $parts += ($stat | Out-String).Trim() }
    if ($new -gt 0) { $parts += ($new.ToString() + ' new file(s)') }
    return @{ Subject = ($verb + ' ' + $shown); Body = ($parts -join '; ') }
}

function Ask-Default([string]$prompt, [string]$default) {
    $shown = $default
    if (-not $shown) { $shown = 'empty' }
    $v = Read-Host ($prompt + ' [' + $shown + ']')
    if ($v -eq '') { return $default }
    return $v
}

function Save-Commit([string[]]$files, [string]$subject, [string]$body, [string]$issue) {
    $msg = $subject + "`n"
    if ($body) { $msg += "`n" + $body + "`n" }
    if ($issue) { $msg += "`nFixes #" + $issue.TrimStart('#') + "`n" }
    $msg += "`nCo-Authored-By: Claude <noreply@anthropic.com>`n"
    Write-Host ''
    Write-Host 'Files:'
    $files | ForEach-Object { Write-Host ('  ' + $_) }
    Write-Host 'Message:'
    Write-Host $msg
    $ok = Read-Host 'Commit? (Enter or y = yes, n = no)'
    if ($ok -and $ok -ne 'y') { Write-Host 'Skipped.'; return $false }
    $tmp = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmp, $msg, (New-Object System.Text.UTF8Encoding($false)))
    git add -- $files | Out-Host
    git commit -F $tmp -- $files | Out-Host
    $code = $LASTEXITCODE
    Remove-Item $tmp
    if ($code -ne 0) { Write-Host 'Commit failed.' -ForegroundColor Red; return $false }
    return $true
}

# --- Ollama control (menu item 10, auto start for key l, background prewarm) ------------------------------------------
function Test-Ollama {
    try { [void](Invoke-RestMethod -Uri ($script:OllamaUrl + '/api/version') -TimeoutSec 2); return $true } catch { return $false }
}

# Starts Ollama when it is not running (the tray app, else "ollama serve") and waits up to 20 seconds.
function Start-Ollama {
    if (Test-Ollama) { return $true }
    if (Test-Path $script:OllamaApp) { Start-Process -FilePath $script:OllamaApp }
    else {
        $exe = (Get-Command ollama -ErrorAction SilentlyContinue).Source
        if (-not $exe) { Write-Host 'Ollama not found. Install it from ollama.com.' -ForegroundColor Yellow; return $false }
        Start-Process -FilePath $exe -ArgumentList 'serve' -WindowStyle Hidden
    }
    Write-Host 'Starting Ollama ...' -ForegroundColor Cyan
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 500
        if (Test-Ollama) { return $true }
    }
    Write-Host 'Ollama did not start within 20 seconds.' -ForegroundColor Yellow
    return $false
}

# Loads the model (keep = $script:LocalKeepAlive) or unloads it (keep = 0). Documented Ollama API: empty generate request.
function Set-OllamaModel([object]$keep) {
    $req = @{ model = $script:LocalModel; keep_alive = $keep } | ConvertTo-Json
    try {
        [void](Invoke-RestMethod -Uri ($script:OllamaUrl + '/api/generate') -Method Post -ContentType 'application/json' -Body $req -TimeoutSec 300)
        return $true
    } catch {
        Write-Host ('Ollama error: ' + $_.Exception.Message) -ForegroundColor Yellow
        return $false
    }
}

function Show-OllamaStatus {
    if (-not (Test-Ollama)) { Write-Host 'Ollama is not running.'; return }
    Write-Host ('Ollama is running (' + $script:OllamaUrl + ').')
    try { $ps = Invoke-RestMethod -Uri ($script:OllamaUrl + '/api/ps') -TimeoutSec 5 } catch { $ps = $null }
    $models = @()
    if ($ps -and $ps.models) { $models = @($ps.models) }
    if ($models.Count -eq 0) { Write-Host 'No model loaded (no VRAM used by Ollama).'; return }
    foreach ($m in $models) {
        $gb = [math]::Round(([double]$m.size_vram) / 1GB, 1)
        $until = ''
        if ($m.expires_at) { try { $until = ', unloads at ' + ([datetime]$m.expires_at).ToLocalTime().ToString('HH:mm') } catch { } }
        Write-Host ('Loaded: ' + $m.name + ', ' + $gb + ' GB VRAM' + $until)
    }
}

function Stop-OllamaAll {
    if (Test-Ollama) { [void](Set-OllamaModel 0) }
    $procs = @(Get-Process -Name 'ollama app', 'ollama', 'llama-server' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path -like '*Ollama*' })
    if ($procs.Count -eq 0) { Write-Host 'Ollama is not running.'; return }
    $procs | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Host 'Ollama closed.' -ForegroundColor Green
}

function Show-OllamaMenu {
    while ($true) {
        Write-Host ''
        Write-Host ('Local model: ' + $script:LocalModel) -ForegroundColor Cyan
        Show-OllamaStatus
        $a = Read-Host 'l = load (starts Ollama if needed), u = unload (frees the VRAM), x = close Ollama, Enter = back'
        switch ($a) {
            'l' {
                if (Start-Ollama) {
                    Write-Host ('Loading ' + $script:LocalModel + ' ...')
                    if (Set-OllamaModel $script:LocalKeepAlive) { Write-Host 'Model loaded.' -ForegroundColor Green }
                }
            }
            'u' {
                if (-not (Test-Ollama)) { Write-Host 'Ollama is not running.' }
                elseif (Set-OllamaModel 0) { Write-Host 'Model unloaded.' -ForegroundColor Green }
            }
            'x' { if (Confirm-Step 'Close Ollama (model and tray app)') { Stop-OllamaAll } }
            '' { return }
            default { Write-Host 'Unknown choice' }
        }
    }
}

# Loads the model in the background while the commit dialog shows its proposals. Only when Ollama already runs.
function Start-OllamaPrewarm {
    if (-not $script:LocalPrewarm) { return }
    if (-not (Test-Ollama)) { return }
    try {
        Add-Type -AssemblyName System.Net.Http
        if (-not $script:Http) { $script:Http = New-Object System.Net.Http.HttpClient; $script:Http.Timeout = [TimeSpan]::FromMinutes(5) }
        $req = @{ model = $script:LocalModel; keep_alive = $script:LocalKeepAlive } | ConvertTo-Json
        $content = New-Object System.Net.Http.StringContent($req, [System.Text.Encoding]::UTF8, 'application/json')
        $script:PrewarmTask = $script:Http.PostAsync($script:OllamaUrl + '/api/generate', $content)
        Write-Host ('Loading ' + $script:LocalModel + ' in the background (for key l) ...') -ForegroundColor DarkGray
    } catch { }
}

# --- local model (Ollama) -------------------------------------------------------------------------------------------
# Key "l" in the commit dialogs asks a local model for a subject and a body. Nothing is sent anywhere else: the model
# runs on this PC (Ollama, http://localhost:11434). It starts only when you press l.

function Convert-ModelText([string]$s) {
    foreach ($c in 0x2010, 0x2011, 0x2012, 0x2013, 0x2014) { $s = $s.Replace([string][char]$c, '-') }
    foreach ($c in 0x2018, 0x2019) { $s = $s.Replace([string][char]$c, "'") }
    foreach ($c in 0x201C, 0x201D) { $s = $s.Replace([string][char]$c, '"') }
    return $s
}

# Returns @{ Subject; Body } or $null (Ollama not running, empty answer). The caller then keeps the simple suggestion.
function Get-LocalSuggestion([string[]]$files) {
    $listOnly = @($files | Where-Object { $_ -match $script:ListOnly })
    $diffFiles = @($files | Where-Object { $_ -notmatch $script:ListOnly })
    $tracked = @($diffFiles | Where-Object { git ls-files -- $_ })
    $untracked = @($diffFiles | Where-Object { -not (git ls-files -- $_) })
    $parts = @()
    $stat = (git diff HEAD --stat -- $files 2>$null) -join "`n"
    if ($stat) { $parts += $stat }
    if ($listOnly.Count -gt 0) { $parts += ("Generated or data files (name only, diff left out):`n" + ($listOnly -join "`n")) }
    $diff = ''
    if ($tracked.Count -gt 0) { $diff = (git diff HEAD -- $tracked 2>$null) -join "`n" }
    foreach ($f in $untracked) {
        $path = Join-Path $root ($f.Replace('/', '\'))
        if (Test-Path $path) { $diff += "`n`nnew file " + $f + ":`n" + ((Get-Content $path -TotalCount 80 -ErrorAction SilentlyContinue) -join "`n") }
    }
    if ($diff.Length -gt $script:LocalDiffLimit) { $diff = $diff.Substring(0, $script:LocalDiffLimit) + "`n[diff cut]" }
    $user = ($parts -join "`n`n") + "`n`n" + $diff
    $styles = (git log --format='- %s' -n 15) -join "`n"
    $sys = 'You write git commit messages for the Shiftline project (a SimHub plugin). Reply with the message only. Line 1 is the subject: imperative English, at most 72 characters, naming the concrete file, feature or area (an area prefix like "Track data:" is fine). Line 2 is empty. Line 3 is ONE body sentence of at most 100 characters saying what changed and why. No lists, no markdown, no quotes, no trailers. Match the style of these recent subjects:' + "`n" + $styles
    if (-not (Start-Ollama)) { return $null }
    $req = @{
        model = $script:LocalModel; stream = $false; think = $script:LocalThink; keep_alive = $script:LocalKeepAlive
        options = @{ num_ctx = $script:LocalContext; temperature = 0.2 }
        messages = @(@{ role = 'system'; content = $sys }, @{ role = 'user'; content = $user })
    } | ConvertTo-Json -Depth 6
    Write-Host ('Asking ' + $script:LocalModel + ' ... (the first call loads the model, this can take half a minute)') -ForegroundColor Cyan
    try {
        $r = Invoke-RestMethod -Uri ($script:OllamaUrl + '/api/chat') -Method Post -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes($req)) -TimeoutSec 300
    } catch {
        Write-Host ('Local model not reachable (is Ollama running?): ' + $_.Exception.Message) -ForegroundColor Yellow
        return $null
    }
    $text = Convert-ModelText ([string]$r.message.content)
    $lines = @($text -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($lines.Count -eq 0) { Write-Host 'The local model returned nothing.' -ForegroundColor Yellow; return $null }
    $subject = $lines[0].Trim([char]34, [char]96)
    $body = ''
    if ($lines.Count -gt 1) { $body = $lines[1] }
    Write-Host ('Suggestion from the local model (' + [math]::Round($r.total_duration / 1e9, 1) + ' s):') -ForegroundColor Cyan
    Write-Host ('  subject: ' + $subject)
    Write-Host ('  body:    ' + $body)
    if ($subject.Length -gt 72) { Write-Host ('  the subject has ' + $subject.Length + ' characters (limit 72), shorten it in the next step') -ForegroundColor Yellow }
    return @{ Subject = $subject; Body = $body }
}

function Invoke-Commit {
    $ErrorActionPreference = 'Continue'
    Push-Location $root
    try {
        git fetch --quiet origin 2>$null
        $branch = (git rev-parse --abbrev-ref HEAD).Trim()
        $behind = (git rev-list --count "HEAD..origin/$branch" 2>$null)
        if ($behind -and [int]$behind -gt 0) { Write-Host ("origin/$branch has $behind new commits. Run git pull first.") -ForegroundColor Yellow; return }
        $changed = @(Get-ChangedFiles)
        if ($changed.Count -eq 0) { Write-Host 'Nothing to commit.'; return }
        Start-OllamaPrewarm

        # 1. proposals from COMMIT.txt
        $commitFile = Join-Path $dev 'COMMIT.txt'
        $left = New-Object System.Collections.ArrayList
        $usable = @()
        $unusable = 0
        foreach ($b in @(Read-CommitFile)) {
            [void]$left.Add($b)
            $missing = @($b.Files | Where-Object { $changed -notcontains $_ })
            if ($missing.Count -eq 0) { $usable += $b } else { $unusable++ }
        }
        if ($unusable -gt 0) { Write-Host ("$unusable proposal(s) in COMMIT.txt do not match the changed files and are ignored.") -ForegroundColor Yellow }
        $committed = 0
        foreach ($b in $usable) {
            Write-Host ''
            Write-Host 'Proposal from COMMIT.txt:' -ForegroundColor Cyan
            Write-Host ('  files:   ' + ($b.Files -join ', '))
            Write-Host ('  subject: ' + $b.Subject)
            Write-Host ('  body:    ' + $b.Body)
            Write-Host ('  issue:   ' + $b.Issue)
            $a = Read-Host 'Enter = use it, e = edit, l = ask the local model, s = skip this one, n = stop'
            if ($a -eq 'n') { break }
            if ($a -eq 's') { continue }
            $subject = $b.Subject; $body = $b.Body; $issue = $b.Issue
            if ($a -eq 'l') {
                $s = Get-LocalSuggestion $b.Files
                if ($s) { $subject = $s.Subject; $body = $s.Body }
                $a = 'e'
            }
            if ($a -eq 'e') {
                $subject = Ask-Default 'Subject' $subject
                $body = Ask-Default 'Body' $body
                $issue = Ask-Default 'Issue number' $issue
            }
            if (Save-Commit $b.Files $subject $body $issue) { $committed++; $left.Remove($b) }
        }
        if ($committed -gt 0 -and (Test-Path $commitFile)) {
            if ($left.Count -eq 0) { Set-Content -Path $commitFile -Value '# all proposals committed' -Encoding UTF8 }
            else { Set-Content -Path $commitFile -Value (($left | ForEach-Object { $_.Raw }) -join "`r`n---`r`n") -Encoding UTF8 }
        }

        # 2. everything else by hand, with a simple suggestion
        $changed = @(Get-ChangedFiles)
        if ($changed.Count -gt 0) {
            Write-Host ''
            Write-Host 'Other changed files:'
            for ($i = 0; $i -lt $changed.Count; $i++) { Write-Host ('  ' + ($i + 1) + '  ' + $changed[$i]) }
            $sel = Read-Host 'Commit some of them? a = all, numbers like 1,3, n or Enter = no'
            if ($sel -and $sel -ne 'n') {
                if ($sel -eq 'a') { $pick = $changed }
                else {
                    $pick = @()
                    foreach ($n in ($sel -split '[,\s]+' | Where-Object { $_ })) {
                        $k = [int]$n - 1
                        if ($k -lt 0 -or $k -ge $changed.Count) { Write-Host "No file $n"; return }
                        $pick += $changed[$k]
                    }
                }
                if ($pick.Count -gt 0) {
                    $fb = Get-FallbackSuggestion $pick
                    $how = Read-Host 'Suggestion: Enter = simple one, l = ask the local model'
                    if ($how -eq 'l') {
                        $s = Get-LocalSuggestion $pick
                        if ($s) { $fb = @{ Subject = $s.Subject; Body = $s.Body } }
                    }
                    $subject = Ask-Default 'Subject (English)' $fb.Subject
                    $body = Ask-Default 'Body' $fb.Body
                    $issue = Read-Host 'Fixes issue number (optional)'
                    if (Save-Commit $pick $subject $body $issue) { $committed++ }
                }
            }
        }
        if ($committed -eq 0) { Write-Host 'Nothing committed.'; return }
        $p = Read-Host 'Push now? (y/n)'
        if ($p -eq 'y') { git push | Out-Host }
    } finally { Pop-Location }
}

# --- release assistant ----------------------------------------------------------------------------------------------
# Menu item 8 (item 9 = dry run, changes nothing). Steps:
#   1 preflight (git state, checks)   2 version   3 notes file   4 test points   5 write version, commit, push
#   6 build   7 try the installer   8 tag check   9 publish   10 summary in .dev\RELEASE.txt
# Every step that changes something asks first and any problem stops the run. Run it again to carry on: steps that are
# already done (version written, commit pushed) are skipped.
# .dev\TESTLIST.txt holds one test point per line: area | what | open or tested  (no "|" inside the text).
# The chats append lines, the menu marks them as tested and drops them after a release.

# Area of a changed file (first match wins); only a hint for what to look at.
$script:AreaRules = @(
    @{ Area = 'Track data';                  Pattern = '^data/tracks/' },
    @{ Area = 'Installer, updater, release'; Pattern = '^(installer/|tools/make-release|plugin/Shiftline/(TrackPack|UpdateService))' },
    @{ Area = 'LED';                         Pattern = '^(tools/(Shiftline\.plugin\.js|gen\.py|check\.py|loader\.js|test_)|plugin/Shiftline/(Led|Preset|ColourTheme))' },
    @{ Area = 'Overlays, Track Map';         Pattern = '^plugin/Shiftline/(Overlays/|.*(Overlay|Standings|BattleBar|TrackMap))' },
    @{ Area = 'Docs';                        Pattern = '^(docs/|README|LICENSE)' },
    @{ Area = 'Dev tools';                   Pattern = '^tools/' }
)

# Things that can only be tried once the release exists. They go into the summary.
$script:AfterReleaseChecks = @(
    'Install the previous release, then update in SimHub: Home page > Updates > Install and restart.',
    'SimHub starts again and the Home page shows the new version.',
    'PluginsData\Shiftline\Update\update.log has no error.',
    'C:\Users\Public\Documents\Shiftline\Tracks\Shiftline.Tracks.version names the new version.',
    'The Track Map shows a real track shape.'
)

function Confirm-Step([string]$question) {
    $a = Read-Host ($question + ' (y/n)')
    return ($a -eq 'y')
}

function Stop-Release([string]$text) {
    Write-Host ''
    Write-Host $text -ForegroundColor Yellow
    Write-Host 'Release stopped here. Run item 8 again to carry on, finished steps are skipped.'
}

function Get-AheadBehind([string]$branch) {
    $r = @{ Ahead = 0; Behind = 0 }
    $c = (git rev-list --left-right --count "HEAD...origin/$branch" 2>$null)
    if ($c) { $p = ($c -split '\s+'); $r.Ahead = [int]$p[0]; $r.Behind = [int]$p[1] }
    return $r
}

function Get-FileVersion {
    $info = [System.IO.File]::ReadAllText((Join-Path $root 'plugin\Shiftline\Properties\AssemblyInfo.cs'))
    if ($info -match 'AssemblyInformationalVersion\("([^"]+)"\)') { return $Matches[1] }
    return ''
}

function Set-TextKeepBom([string]$path, [string]$text) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $bom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($bom)))
}

# Writes the version into AssemblyInfo.cs (3 lines) and Shiftline.iss (AppVersion). Keeps BOM and line endings.
function Set-ReleaseVersion([string]$version) {
    $numbers = [regex]::Match($version, '^\d+\.\d+\.\d+').Value
    $edits = @(
        @{ File = 'plugin\Shiftline\Properties\AssemblyInfo.cs'; Pattern = '(\[assembly: AssemblyVersion\(")[^"]+("\)\])';              Value = ($numbers + '.0') },
        @{ File = 'plugin\Shiftline\Properties\AssemblyInfo.cs'; Pattern = '(\[assembly: AssemblyFileVersion\(")[^"]+("\)\])';          Value = ($numbers + '.0') },
        @{ File = 'plugin\Shiftline\Properties\AssemblyInfo.cs'; Pattern = '(\[assembly: AssemblyInformationalVersion\(")[^"]+("\)\])'; Value = $version },
        @{ File = 'installer\Shiftline.iss';                     Pattern = '(#define AppVersion ")[^"]+(")';                          Value = $version }
    )
    foreach ($file in @($edits | ForEach-Object { $_.File } | Select-Object -Unique)) {
        $path = Join-Path $root $file
        $text = [System.IO.File]::ReadAllText($path)
        $new = $text
        foreach ($e in @($edits | Where-Object { $_.File -eq $file })) {
            if (-not [regex]::IsMatch($new, $e.Pattern)) { throw ('Version line not found in ' + $file + ': ' + $e.Pattern) }
            $new = [regex]::Replace($new, $e.Pattern, ('${1}' + $e.Value + '${2}'))
        }
        if ($new -ne $text) { Set-TextKeepBom $path $new; Out-Line ($file + ': set to ' + $version) }
        else { Out-Line ($file + ': already ' + $version) }
    }
}

function Test-TagFree([string]$version) {
    $t = 'v' + $version
    if (@(git tag -l $t).Count -gt 0) {
        Write-Host ('Tag ' + $t + ' already exists locally.') -ForegroundColor Yellow
        return $false
    }
    $remote = @(git ls-remote --tags origin ('refs/tags/' + $t))
    if ($remote.Count -gt 0) {
        Write-Host ('Tag ' + $t + ' already exists on origin (a deleted release leaves its tag behind). Check where it points before you go on.') -ForegroundColor Yellow
        return $false
    }
    return $true
}

function Push-IfAhead([string]$branch) {
    git fetch --quiet origin 2>$null
    $ab = Get-AheadBehind $branch
    if ($ab.Behind -gt 0) { Write-Host ('origin/' + $branch + ' has ' + $ab.Behind + ' new commits. Run git pull first.') -ForegroundColor Yellow; return $false }
    if ($ab.Ahead -eq 0) { return $true }
    if (-not (Confirm-Step ('Push ' + $ab.Ahead + ' commit(s) to origin/' + $branch + ' now'))) { return $false }
    git push | Out-Host
    return ($LASTEXITCODE -eq 0)
}

# --- test list
function Read-TestList {
    $file = Join-Path $dev 'TESTLIST.txt'
    $items = @()
    if (-not (Test-Path $file)) { return $items }
    foreach ($line in @(Get-Content $file -Encoding UTF8)) {
        if ($line -match '^\s*#' -or -not $line.Trim()) { continue }
        $p = @($line -split '\s*\|\s*')
        if ($p.Count -lt 3) { continue }
        $items += @{ Area = $p[0].Trim(); What = $p[1].Trim(); Status = $p[2].Trim().ToLower(); Raw = $line }
    }
    return $items
}

function Update-TestList([string]$mode, [string]$raw) {
    $file = Join-Path $dev 'TESTLIST.txt'
    if (-not (Test-Path $file)) { return }
    Copy-Item $file ($file + '.bak') -Force
    $lines = @(Get-Content $file -Encoding UTF8)
    if ($mode -eq 'mark') {
        $lines = @($lines | ForEach-Object { if ($_ -eq $raw) { $_ -replace '\|\s*open\s*$', '| tested' } else { $_ } })
    } elseif ($mode -eq 'drop') {
        $lines = @($lines | Where-Object { $_ -match '^\s*#' -or $_ -notmatch '\|\s*tested\s*$' })
    }
    Set-Content -Path $file -Value $lines -Encoding UTF8
}

# Runs tools\make-release.ps1; offers -UpdateReadme when the SimHub version in the README is out of date.
function Invoke-MakeRelease {
    $script2 = Join-Path $root 'tools\make-release.ps1'
    $extra = @{}
    for ($try = 0; $try -lt 3; $try++) {
        $msg = ''
        try { & $script2 @extra } catch { $msg = $_.Exception.Message }
        if (-not $msg) { return $true }
        Write-Host $msg -ForegroundColor Red
        if ($msg -match 'README\.md names SimHub') {
            if (-not (Confirm-Step 'Write the installed SimHub version into README.md')) { return $false }
            $extra = @{ UpdateReadme = $true }
            continue
        }
        if ($msg -match 'README\.md now names SimHub') {
            $extra = @{}
            if (-not (Save-Commit @('README.md') 'Update the SimHub version in the README' '' '')) { return $false }
            continue
        }
        return $false
    }
    return $false
}

function Invoke-Release {
    param([switch]$DryRun)
    $ErrorActionPreference = 'Continue'
    $script:out.Clear()
    $title = 'Shiftline release'
    if ($DryRun) { $title += ' (dry run, nothing is changed)' }
    $script:out.Add($title + ', ' + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
    Push-Location $root
    try {
        # 1 -------------------------------------------------------------------------------------------------------
        Write-Head '1 Preflight'
        git fetch --quiet origin 2>$null
        $branch = (git rev-parse --abbrev-ref HEAD).Trim()
        if ($branch -ne 'main') { Stop-Release ('Branch is ' + $branch + ', a release is made from main.'); return }
        $ab = Get-AheadBehind $branch
        if ($ab.Behind -gt 0) { Stop-Release ('origin/main has ' + $ab.Behind + ' new commits. Run git pull first.'); return }
        $allowed = @('plugin/Shiftline/Properties/AssemblyInfo.cs', 'installer/Shiftline.iss', 'README.md')
        $other = @(Get-ChangedFiles | Where-Object { $allowed -notcontains $_ })
        if ($other.Count -gt 0) {
            Out-Line 'uncommitted changes:'
            $other | Select-Object -First 15 | ForEach-Object { Out-Line ('  ' + $_) }
            Stop-Release 'Commit or undo these first (menu item 7).'
            return
        }
        $tag = (git describe --tags --abbrev=0 2>$null)
        if ($tag) { $tag = $tag.Trim() }
        Out-Line ('branch ' + $branch + ', ' + $ab.Ahead + ' unpushed commit(s), last tag ' + $tag)
        if ($tag) {
            $subjects = @(git log --oneline "$tag..HEAD")
            Out-Line ('commits since ' + $tag + ': ' + $subjects.Count)
            $subjects | Select-Object -First 30 | ForEach-Object { Out-Line ('  ' + $_) }
            $areaCounts = @{}
            foreach ($f in @(git diff --name-only "$tag..HEAD")) {
                $area = 'Plugin (other)'
                foreach ($r in $script:AreaRules) { if ($f -match $r.Pattern) { $area = $r.Area; break } }
                if ($areaCounts.ContainsKey($area)) { $areaCounts[$area]++ } else { $areaCounts[$area] = 1 }
            }
            if ($areaCounts.Count -gt 0) {
                Out-Line 'changed since the last tag (what to look at):'
                $areaCounts.Keys | Sort-Object | ForEach-Object { Out-Line ('  ' + $_ + ': ' + $areaCounts[$_] + ' file(s)') }
            }
        }
        if ($DryRun) { Out-Line 'would run the checks (gen.py, check.py, Node tests)' }
        else {
            $mine = @($script:out)
            Invoke-Check
            $failed = @($script:out | Where-Object { $_ -match 'FAILED|not found' })
            $script:out.Clear()
            $mine | ForEach-Object { $script:out.Add($_) }
            if ($failed.Count -gt 0) { Out-Line 'checks FAILED (details in .dev\CHECK.txt)'; Stop-Release 'Fix the checks first.'; return }
            Out-Line 'checks OK'
            $other = @(Get-ChangedFiles | Where-Object { $allowed -notcontains $_ })
            if ($other.Count -gt 0) {
                $other | ForEach-Object { Out-Line ('  ' + $_) }
                Stop-Release 'The checks changed files (gen.py output). Commit them first (menu item 7).'
                return
            }
        }

        # 2 -------------------------------------------------------------------------------------------------------
        Write-Head '2 Version'
        $suggest = ''
        if ($tag -match '^v(\d+)\.(\d+)\.(\d+)') { $suggest = $Matches[1] + '.' + $Matches[2] + '.' + ([int]$Matches[3] + 1) }
        $fileVersion = Get-FileVersion
        if ($fileVersion -and ('v' + $fileVersion) -ne $tag) { $suggest = $fileVersion }
        $version = (Ask-Default 'Version to release' $suggest).Trim()
        if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { Stop-Release ('"' + $version + '" is not a version like 0.4.2 or 0.5.0-beta.1.'); return }
        Out-Line ('version ' + $version)
        if (-not (Test-TagFree $version)) { Stop-Release 'The tag is taken.'; return }

        # 3 -------------------------------------------------------------------------------------------------------
        Write-Head '3 Release notes'
        $notes = Join-Path $root ('release\notes-' + $version + '.md')
        if (-not (Test-Path $notes) -or (Get-Item $notes).Length -lt 20) {
            Stop-Release ('release\notes-' + $version + '.md is missing. Ask the Basis chat to write the release notes (Release ' + $version + ' vorbereiten), then run this item again.')
            return
        }
        Out-Line ('notes: release\notes-' + $version + '.md')

        # 4 -------------------------------------------------------------------------------------------------------
        Write-Head '4 Test points'
        $open = @(Read-TestList | Where-Object { $_.Status -eq 'open' })
        $deferred = @()
        if ($open.Count -eq 0) { Out-Line 'no open test points in .dev\TESTLIST.txt' }
        foreach ($t in $open) {
            Out-Line ('[' + $t.Area + '] ' + $t.What)
            if ($DryRun) { continue }
            $a = Read-Host '  t = tested, d = release without it, Enter = stop'
            if ($a -eq 't') { Update-TestList 'mark' $t.Raw; Out-Line '  -> tested' }
            elseif ($a -eq 'd') { $deferred += $t; Out-Line '  -> not tested, goes into the summary' }
            else { Stop-Release 'Test it first, or answer d to release without it.'; return }
        }

        if ($DryRun) {
            Write-Head 'Would do next'
            Out-Line ('5 write ' + $version + ' into AssemblyInfo.cs and Shiftline.iss, commit "Shiftline ' + $version + '", push')
            Out-Line '6 run tools\make-release.ps1 (Release build, track zip, installer), check the 5 files'
            Out-Line '7 ask you to try the installer over your installation'
            Out-Line ('8 check that tag v' + $version + ' is free and everything is pushed')
            $pre = ''
            if ($version.Contains('-')) { $pre = ' as pre-release' }
            Out-Line ('9 tag v' + $version + ', push the tag, gh release create' + $pre + ' with the notes and the 5 files')
            Out-Line '10 write .dev\RELEASE.txt (summary, not tested, after-release checks)'
            Write-Host ''
            Write-Host 'Dry run done. Nothing was changed.' -ForegroundColor Green
            return
        }

        # 5 -------------------------------------------------------------------------------------------------------
        Write-Head '5 Version, commit, push'
        Set-ReleaseVersion $version
        $pending = @(Get-ChangedFiles | Where-Object { $allowed -contains $_ })
        if ($pending.Count -gt 0) {
            if (-not (Save-Commit $pending ('Shiftline ' + $version) '' '')) { Stop-Release 'The version commit was not made.'; return }
        } else { Out-Line 'version commit: nothing to commit (already done)' }
        if (-not (Push-IfAhead $branch)) { Stop-Release 'Push did not happen.'; return }

        # 6 -------------------------------------------------------------------------------------------------------
        Write-Head '6 Release files'
        if (-not (Confirm-Step 'Build the release files now (Release build, track zip, installer; SimHub may keep running)')) { Stop-Release 'Build skipped.'; return }
        if (-not (Invoke-MakeRelease)) { Stop-Release 'make-release.ps1 failed, see above.'; return }
        $relDir = Join-Path $root ('release\' + $version)
        $expected = @(('Shiftline-Setup-' + $version + '.exe'), 'Shiftline.Plugin.dll', 'Shiftline.Plugin.dll.sha256', 'Shiftline.Tracks.zip', 'Shiftline.Tracks.zip.sha256')
        $missing = @($expected | Where-Object { -not (Test-Path (Join-Path $relDir $_)) })
        $surplus = @(Get-ChildItem $relDir -File | Where-Object { $expected -notcontains $_.Name })
        if ($missing.Count -gt 0) { Stop-Release ('Missing in release\' + $version + ': ' + ($missing -join ', ')); return }
        if ($surplus.Count -gt 0) { Stop-Release ('Unexpected files in release\' + $version + ': ' + (($surplus | ForEach-Object { $_.Name }) -join ', ')); return }
        Out-Line ('5 files in release\' + $version + ' OK')

        # 7 -------------------------------------------------------------------------------------------------------
        Write-Head '7 Try the installer'
        Out-Line ('Run release\' + $version + '\Shiftline-Setup-' + $version + '.exe over your installation and start SimHub.')
        Out-Line 'Look for: the plugin loads without errors in the log, and C:\Users\Public\Documents\Shiftline\Tracks holds Shiftline.Tracks.zip and Shiftline.Tracks.version.'
        if (-not (Confirm-Step 'Installer tried and fine')) { Stop-Release 'Try the installer first (the build is repeated when you run this item again).'; return }

        # 8 -------------------------------------------------------------------------------------------------------
        Write-Head '8 Tag check'
        if (-not (Test-TagFree $version)) { Stop-Release 'The tag is taken.'; return }
        if (@(Get-ChangedFiles).Count -gt 0) { Stop-Release 'There are uncommitted changes. Commit them (item 7) or undo them; the release has to match Git.'; return }
        if (-not (Push-IfAhead $branch)) { Stop-Release 'Everything has to be pushed before the tag is made.'; return }
        $short = (git rev-parse --short HEAD).Trim()
        Out-Line ('tag v' + $version + ' is free, HEAD ' + $short + ' is on origin')

        # 9 -------------------------------------------------------------------------------------------------------
        Write-Head '9 Publish'
        $prerelease = $version.Contains('-')
        $files = @(Get-ChildItem $relDir -File | ForEach-Object { $_.FullName })
        $tagName = 'v' + $version
        $title2 = 'Shiftline ' + $version
        if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
            Out-Line 'gh not found. Run these by hand:'
            Out-Line ('  git tag ' + $tagName)
            Out-Line ('  git push origin ' + $tagName)
            $cmd = '  gh release create ' + $tagName + ' --verify-tag --title "' + $title2 + '" --notes-file release\notes-' + $version + '.md'
            if ($prerelease) { $cmd += ' --prerelease' }
            Out-Line ($cmd + ' release\' + $version + '\*')
            Stop-Release 'Not published (gh missing).'
            return
        }
        $kind = ''
        if ($prerelease) { $kind = ' (pre-release)' }
        Out-Line ('ready: tag ' + $tagName + ' on ' + $short + ', GitHub release "' + $title2 + '"' + $kind + ', ' + $files.Count + ' files, notes from release\notes-' + $version + '.md')
        if (-not (Confirm-Step 'Publish now (tag, push tag, GitHub release)')) { Stop-Release 'Not published.'; return }
        git tag $tagName
        git push origin $tagName | Out-Host
        if ($LASTEXITCODE -ne 0) { Stop-Release ('Pushing the tag failed. It exists locally; remove it with: git tag -d ' + $tagName); return }
        $ghArgs = @('release', 'create', $tagName, '--verify-tag', '--title', $title2, '--notes-file', $notes)
        if ($prerelease) { $ghArgs += '--prerelease' }
        $ghArgs += $files
        & gh @ghArgs | Out-Host
        if ($LASTEXITCODE -ne 0) { Stop-Release ('gh release create failed. The tag ' + $tagName + ' is already on origin: create the release by hand (docs\RELEASE.md, section 3) or delete the tag first.'); return }
        $url = (& gh release view $tagName --json url -q .url 2>$null)

        # 10 ------------------------------------------------------------------------------------------------------
        Write-Head '10 Summary'
        Out-Line ('version ' + $version + ', commit ' + $short + ', tag ' + $tagName)
        if ($url) { Out-Line ('release: ' + $url) }
        foreach ($f in @('Shiftline.Plugin.dll.sha256', 'Shiftline.Tracks.zip.sha256')) {
            Out-Line ($f.Replace('.sha256', '') + ' SHA256 ' + (Get-Content (Join-Path $relDir $f) -Raw).Trim())
        }
        if ($deferred.Count -gt 0) {
            Out-Line 'released WITHOUT testing:'
            $deferred | ForEach-Object { Out-Line ('  [' + $_.Area + '] ' + $_.What) }
        }
        Out-Line 'now try (report the result in the chat, it goes into HISTORY):'
        $script:AfterReleaseChecks | ForEach-Object { Out-Line ('  - ' + $_) }
        Update-TestList 'drop' ''
        Save-Out 'RELEASE.txt'
    } catch {
        Stop-Release ('Error: ' + $_.Exception.Message)
    } finally { Pop-Location }
}

function Show-Menu {
    while ($true) {
        Write-Host ''
        Write-Host 'Shiftline dev menu' -ForegroundColor Cyan
        for ($i = 0; $i -lt $script:Chats.Count; $i++) { Write-Host ('  ' + ($i + 1) + '  Chat start: ' + $script:Chats[$i].Name) }
        Write-Host '  4  Chat start: everything'
        Write-Host '  5  Build Debug (errors and warnings only)'
        Write-Host '  6  Checks: gen.py, check.py, Node tests'
        Write-Host '  7  Commit (with trailer, asks before push)'
        Write-Host '  8  Release (version, build, tag, GitHub release)'
        Write-Host '  9  Release dry run (shows the steps, changes nothing)'
        Write-Host ' 10  Local model (Ollama): status, load, unload, close'
        Write-Host '  q  Quit'
        $choice = Read-Host 'Choice'
        switch ($choice) {
            { $_ -in '1', '2', '3' } { Show-ChatStart -Chat $script:Chats[[int]$choice - 1].Name }
            '4' { Show-ChatStart }
            '5' { Invoke-Build }
            '6' { Invoke-Check }
            '7' { Invoke-Commit }
            '8' { Invoke-Release }
            '9' { Invoke-Release -DryRun }
            '10' { Show-OllamaMenu }
            'q' { return }
            default { Write-Host 'Unknown choice' }
        }
    }
}

switch ($Run) {
    ''      { Show-Menu }
    'start' { Show-ChatStart }
    'build' { Invoke-Build }
    'check' { Invoke-Check }
    'commit' { Invoke-Commit }
    'release' { Invoke-Release -DryRun:$DryRun }
    'ollama' { Show-OllamaMenu }
    default { throw "Unknown item: $Run" }
}
