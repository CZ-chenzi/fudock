# One-shot tool: wraps Chinese string literals in the UI sources with L.T("...").
# Comments are preserved untouched (the code base quotes phrases in comments a lot)
# and lines that log are skipped on purpose (logs stay in Chinese).
# ASCII-only: Windows PowerShell 5.1 reads BOM-less scripts as ANSI.
param(
    [string[]]$Files = @(),
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Files.Count -eq 0) {
    $Files = @('SettingsWindow.cs', 'DockWindow.cs', 'SystemPanel.cs', 'SystemAreaView.cs',
               'StartupAndTray.cs', 'Program.cs', 'FlyoutWindow.cs', 'PositionEditorWindow.cs', 'Ui.cs')
}

function Split-Comment([string]$line) {
    $inStr = $false
    $esc = $false
    for ($i = 0; $i -lt $line.Length; $i++) {
        $c = $line[$i]
        if ($inStr) {
            if ($esc) { $esc = $false }
            elseif ($c -eq '\') { $esc = $true }
            elseif ($c -eq '"') { $inStr = $false }
            continue
        }
        if ($c -eq '"') { $inStr = $true; continue }
        if ($c -eq '/' -and $i + 1 -lt $line.Length -and $line[$i + 1] -eq '/') {
            return @($line.Substring(0, $i), $line.Substring($i))
        }
    }
    return @($line, '')
}

$enc = New-Object System.Text.UTF8Encoding($false)
$totalWrapped = 0
$report = @()

foreach ($name in $Files) {
    $path = Join-Path $root ("src\" + $name)
    if (-not (Test-Path $path)) { Write-Host ("WARN missing " + $name) -ForegroundColor Yellow; continue }
    $lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
    $changed = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $parts = Split-Comment $lines[$i]
        $code = $parts[0]
        $rest = $parts[1]
        if ([string]::IsNullOrWhiteSpace($code)) { continue }
        if ($code -match 'SettingsStore\.Log\(|Debug\.|TrayBridge') { continue }
        $lits = [regex]::Matches($code, '"((?:[^"\\]|\\.)*)"')
        if ($lits.Count -eq 0) { continue }

        $newCode = $code
        $wrapped = 0
        for ($k = $lits.Count - 1; $k -ge 0; $k--) {
            $m = $lits[$k]
            $literal = $m.Value
            $inner = $m.Groups[1].Value
            if ($inner -notmatch '[\u4e00-\u9fff]') { continue }
            # already wrapped?
            $before = $newCode.Substring(0, $m.Index)
            if ($before.EndsWith('L.T(')) { continue }
            $newCode = $newCode.Substring(0, $m.Index) + 'L.T(' + $literal + ')' + $newCode.Substring($m.Index + $literal.Length)
            $wrapped++
        }
        if ($wrapped -gt 0) {
            $lines[$i] = $newCode + $rest
            $changed += $wrapped
        }
    }
    if ($changed -gt 0) {
        if ($Apply) { [System.IO.File]::WriteAllLines($path, $lines, $enc) }
        $report += ("{0,-26} {1,3} places" -f $name, $changed)
        $totalWrapped += $changed
    }
}

$report
Write-Output ("---- wrapped: " + $totalWrapped + $(if ($Apply) { " (written)" } else { " (dry run, pass -Apply)" }))
