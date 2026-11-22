# Build Fudock.exe with the .NET Framework 4.8 compiler that ships with Windows.
# No .NET SDK required.  (ASCII-only on purpose: Windows PowerShell 5.1 reads
# BOM-less scripts as ANSI, so non-ASCII text here would be mangled.)
param([switch]$Run, [switch]$RebuildIcon)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$outDir = Join-Path $root 'build'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$ico = Join-Path $root 'app.ico'
if ($RebuildIcon -or !(Test-Path $ico)) {
    & (Join-Path $root 'tools\make-icon.ps1')
}

$sources = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw 'no source files found' }

$refs = @(
    "$fw\WPF\PresentationFramework.dll",
    "$fw\WPF\PresentationCore.dll",
    "$fw\WPF\WindowsBase.dll",
    "$fw\System.dll",
    "$fw\System.Core.dll",
    "$fw\System.Drawing.dll",
    "$fw\System.Windows.Forms.dll",
    "$fw\System.Web.Extensions.dll",
    "$fw\System.Xaml.dll",
    "$fw\System.Xml.dll",
    "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll",
    "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll"
)

$cargs = @(
    '/nologo',
    '/codepage:65001',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/warn:4',
    "/out:$outDir\Fudock.exe",
    "/win32icon:$ico",
    "/win32manifest:$root\app.manifest"
)
foreach ($r in $refs) { $cargs += "/r:$r" }
$cargs += $sources

Write-Host "Compiling $($sources.Count) source files ..." -ForegroundColor Cyan
& "$fw\csc.exe" @cargs
if ($LASTEXITCODE -ne 0) { throw "build failed, exit code $LASTEXITCODE" }

$exe = Join-Path $outDir 'Fudock.exe'
Write-Host ("OK: {0} ({1:N0} KB)" -f $exe, ((Get-Item $exe).Length / 1KB)) -ForegroundColor Green

if ($Run) {
    Start-Process -FilePath $exe
    Write-Host 'Fudock started.' -ForegroundColor Green
}
