# Rewrite one field of %APPDATA%\Fudock\settings.json (UTF-8 safe, numbers/bools/strings).
# usage: .\setfield.ps1 -Field AlwaysOnTop -Value true
param([string]$Field, [string]$Value)
$ErrorActionPreference = 'Stop'
$path = Join-Path $env:APPDATA 'Fudock\settings.json'
$raw = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
$re = '"' + $Field + '":(true|false|-?[0-9.]+|"[^"]*")'
if ($raw -notmatch $re) { throw "field $Field not found" }
$new = [regex]::Replace($raw, $re, '"' + $Field + '":' + $Value)
[System.IO.File]::WriteAllText($path, $new, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "$Field -> $Value"
