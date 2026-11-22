# Debug helper: renders every icon style side by side (128px 1:1, plus 32px and
# 16px enlarged) so the letter / wave / hanzi variants can be compared.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root 'build'
if (-not (Test-Path $build)) { New-Item -ItemType Directory -Path $build -Force | Out-Null }

function Get-IcoEntry([string]$path, [int]$want) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $count = [BitConverter]::ToUInt16($bytes, 4)
    for ($i = 0; $i -lt $count; $i++) {
        $o = 6 + $i * 16
        $w = [int]$bytes[$o]
        if ($w -eq 0) { $w = 256 }
        if ($w -ne $want) { continue }
        $size = [BitConverter]::ToUInt32($bytes, $o + 8)
        $off = [BitConverter]::ToUInt32($bytes, $o + 12)
        $png = New-Object byte[] $size
        [Array]::Copy($bytes, $off, $png, 0, $size)
        $ms = New-Object System.IO.MemoryStream($png, $false)
        return [System.Drawing.Image]::FromStream($ms)
    }
    return $null
}

$styles = @(
    @{ name = 'pier  (bar = pier deck)'; file = 'ico-pier.ico' },
    @{ name = 'boat  (hull + water)';    file = 'ico-boat.ico' },
    @{ name = 'dock  (bar + dots)';   file = 'ico-dock.ico' },
    @{ name = 'letter  (F + bar)';    file = 'ico-letter.ico' },
    @{ name = 'fbar  (F crossbar=bar)'; file = 'ico-fbar.ico' },
    @{ name = 'wave  (bar on water)'; file = 'ico-wave.ico' },
    @{ name = 'hanzi  (wu glyph)';    file = 'ico-hanzi.ico' }
)

foreach ($s in $styles) {
    $target = Join-Path $build $s.file
    $styleName = $s.file.Replace('ico-', '').Replace('.ico', '')
    & (Join-Path $root 'tools\make-icon.ps1') -Style $styleName -Out $target | Out-Null
}

$sheet = New-Object System.Drawing.Bitmap(1620, 430, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::FromArgb(255, 0xF3, 0xF4, 0xF6))
$font = New-Object System.Drawing.Font('Consolas', 11)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0x33, 0x38, 0x40))

$x = 20
foreach ($s in $styles) {
    $ico = Join-Path $build $s.file
    $img128 = Get-IcoEntry $ico 128
    $img32 = Get-IcoEntry $ico 32
    $img16 = Get-IcoEntry $ico 16

    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($img128, $x, 30, 128, 128)

    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.DrawImage($img32, ($x + 4), 200, 96, 96)
    $g.DrawImage($img16, ($x + 130), 200, 96, 96)

    $g.DrawString('32px', $font, $brush, ($x + 20), 300)
    $g.DrawString('16px', $font, $brush, ($x + 148), 300)
    $g.DrawString($s.name, $font, $brush, $x, 340)
    $x = $x + 260
}

$g.Dispose()
$outPng = Join-Path $build 'icon-compare.png'
$sheet.Save($outPng, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
Write-Output ('compare sheet: ' + $outPng)
