<#
  Developer menu for Shiftline. Short, readable output that can be pasted into a chat.

  Usage (from the repository folder):
    powershell -ExecutionPolicy Bypass -File tools\dev.ps1            # menu
    powershell -ExecutionPolicy Bypass -File tools\dev.ps1 -Run start # one item without the menu

  Items: start (chat start summary), build (Debug build, errors only), check (gen.py, check.py, Node tests),
  commit (proposals from .dev\COMMIT.txt or a simple suggestion, trailer, asks before push). Output of start/build/check goes to .dev\*.txt and the clipboard.
  Only the commit item writes to Git (add, commit, and push after asking). The other items only read it.
#>
param([string]$Run = '')

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
            $a = Read-Host 'Enter = use it, e = edit, s = skip this one, n = stop'
            if ($a -eq 'n') { break }
            if ($a -eq 's') { continue }
            $subject = $b.Subject; $body = $b.Body; $issue = $b.Issue
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

function Show-Menu {
    while ($true) {
        Write-Host ''
        Write-Host 'Shiftline dev menu' -ForegroundColor Cyan
        for ($i = 0; $i -lt $script:Chats.Count; $i++) { Write-Host ('  ' + ($i + 1) + '  Chat start: ' + $script:Chats[$i].Name) }
        Write-Host '  4  Chat start: everything'
        Write-Host '  5  Build Debug (errors and warnings only)'
        Write-Host '  6  Checks: gen.py, check.py, Node tests'
        Write-Host '  7  Commit (with trailer, asks before push)'
        Write-Host '  q  Quit'
        $choice = Read-Host 'Choice'
        switch ($choice) {
            { $_ -in '1', '2', '3' } { Show-ChatStart -Chat $script:Chats[[int]$choice - 1].Name }
            '4' { Show-ChatStart }
            '5' { Invoke-Build }
            '6' { Invoke-Check }
            '7' { Invoke-Commit }
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
    default { throw "Unknown item: $Run" }
}
