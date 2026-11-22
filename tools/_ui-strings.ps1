# Lists the Chinese UI string literals in the given sources, ignoring // comments
# and /// doc comments (this codebase quotes phrases inside comments a lot).
# usage: .\tools\_ui-strings.ps1 [-Files a,b] [-Out json]
param([string[]]$Files = @(), [string]$Out = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Files.Count -eq 0) {
    $Files = @('SettingsWindow.cs', 'DockWindow.cs', 'SystemPanel.cs', 'SystemAreaView.cs',
               'StartupAndTray.cs', 'Program.cs', 'FlyoutWindow.cs', 'PositionEditorWindow.cs', 'Ui.cs')
}

function Get-CodePart([string]$line) {
    $sb = New-Object System.Text.StringBuilder
    $inStr = $false
    $esc = $false
    for ($i = 0; $i -lt $line.Length; $i++) {
        $c = $line[$i]
        if ($inStr) {
            [void]$sb.Append($c)
            if ($esc) { $esc = $false }
            elseif ($c -eq '\') { $esc = $true }
            elseif ($c -eq '"') { $inStr = $false }
            continue
        }
        if ($c -eq '"') { $inStr = $true; [void]$sb.Append($c); continue }
        if ($c -eq '/' -and $i + 1 -lt $line.Length -and $line[$i + 1] -eq '/') { break }
        [void]$sb.Append($c)
    }
    return $sb.ToString()
}

$rows = @()
foreach ($name in $Files) {
    $path = Join-Path $root ("src\" + $name)
    if (-not (Test-Path $path)) { continue }
    $lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $code = Get-CodePart $lines[$i]
        if ([string]::IsNullOrWhiteSpace($code)) { continue }
        $isLog = $code -match 'Log\(|Debug\.'
        foreach ($m in [regex]::Matches($code, '"((?:[^"\\]|\\.)*)"')) {
            $s = $m.Groups[1].Value
            if ($s -notmatch '[\u4e00-\u9fff]') { continue }
            $rows += [pscustomobject]@{ file = $name; line = $i + 1; text = $s; log = $isLog }
        }
    }
}

if ($Out) {
    $rows | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Out -Encoding UTF8
    Write-Output ("wrote " + $Out + "  (" + $rows.Count + " strings)")
}
else {
    $seen = @{}
    foreach ($r in $rows) {
        if ($seen.ContainsKey($r.text)) { continue }
        $seen[$r.text] = $true
        "{0,-24} {1,4}  {2}{3}" -f $r.file, $r.line, $r.text, $(if ($r.log) { '   [log]' } else { '' })
    }
    Write-Output ("---- unique: " + $seen.Count + " / total: " + $rows.Count)
}
