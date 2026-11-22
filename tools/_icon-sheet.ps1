# Debug helper: renders a contact sheet of the ICO's PNG entries so the icon can
# be judged at real sizes (small sizes enlarged with nearest-neighbour).
# usage: .\_icon-sheet.ps1 [-Ico path] [-Out png]
param([string]$Ico = '', [string]$Out = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$ico = $Ico
if ([string]::IsNullOrEmpty($ico)) { $ico = Join-Path $root 'app.ico' }
$outPng = $Out
if ([string]::IsNullOrEmpty($outPng)) { $outPng = Join-Path $root 'build\icon-sheet.png' }

$bytes = [System.IO.File]::ReadAllBytes($ico)
$count = [BitConverter]::ToUInt16($bytes, 4)
$entries = @{}
for ($i = 0; $i -lt $count; $i++) {
    $o = 6 + $i * 16
    $w = [int]$bytes[$o]
    if ($w -eq 0) { $w = 256 }
    $size = [BitConverter]::ToUInt32($bytes, $o + 8)
    $off = [BitConverter]::ToUInt32($bytes, $o + 12)
    $png = New-Object byte[] $size
    [Array]::Copy($bytes, $off, $png, 0, $size)
    $ms = New-Object System.IO.MemoryStream($png, $false)
    $entries[$w] = [System.Drawing.Image]::FromStream($ms)
}

$sheet = New-Object System.Drawing.Bitmap(920, 470, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear([System.Drawing.Color]::FromArgb(255, 0xF3, 0xF4, 0xF6))

# big sizes, 1:1
$g.DrawImage($entries[256], 24, 24, 256, 256)
$g.DrawImage($entries[128], 320, 88, 128, 128)
$g.DrawImage($entries[64], 480, 152, 64, 64)
$g.DrawImage($entries[48], 570, 168, 48, 48)

$font = New-Object System.Drawing.Font('Consolas', 10)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0x55, 0x5A, 0x63))

# small sizes, 4x nearest-neighbour (what the taskbar actually shows)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$x = 24
foreach ($s in @(48, 32, 24, 16)) {
    $w = $s * 4
    $g.DrawImage($entries[$s], $x, 320, $w, $w)
    $g.DrawString(($s.ToString() + 'px'), $font, $brush, $x, 320 + $w + 8)
    $x = $x + $w + 40
}

$g.Dispose()
$sheet.Save($outPng, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
Write-Output ("sheet written: " + $outPng)
