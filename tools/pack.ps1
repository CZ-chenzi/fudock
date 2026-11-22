# Builds the release package(s) under dist\:
#   default      : Fudock-<version>.zip   - exe + LICENSE + quick start (zh) + quick start (en)
#   -WithDocs    : Fudock-<version>-docs.zip - the above plus README and the screenshots it uses
# ASCII-only on purpose: Windows PowerShell 5.1 reads BOM-less scripts as ANSI.
param(
    [string]$Version = '1.0.0',
    [switch]$NoBuild,
    [switch]$KeepFolder,
    [switch]$WithDocs
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot | Split-Path -Parent
$dist = Join-Path $root 'dist'
$tag = "Fudock-" + $Version + $(if ($WithDocs) { '-docs' } else { '' })
$stage = Join-Path $dist $tag
$exe = Join-Path $root 'build\Fudock.exe'

if (-not $NoBuild) {
    & (Join-Path $root 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
}
if (-not (Test-Path $exe)) { throw "missing $exe" }

if (Get-Process Fudock -ErrorAction SilentlyContinue) {
    throw 'Fudock.exe is running - quit it (tray -> Quit) before packaging'
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Copy-Item $exe (Join-Path $stage 'Fudock.exe')
if (Test-Path (Join-Path $root 'LICENSE')) { Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE') }

# One bilingual quick start for the end user (exe + LICENSE + readme.txt).
if (Test-Path (Join-Path $root 'readme.txt')) { Copy-Item (Join-Path $root 'readme.txt') (Join-Path $stage 'readme.txt') }
else { Write-Host 'WARN: missing readme.txt' -ForegroundColor Yellow }

$copied = 0

if ($WithDocs) {
    # ---- screenshots: only the ones README references, downscaled + JPEG ----
    Add-Type -AssemblyName System.Drawing
    $docsOut = Join-Path $stage 'docs'
    New-Item -ItemType Directory -Path $docsOut -Force | Out-Null

    $readmePath = Join-Path $root 'README.md'
    $readme = [System.IO.File]::ReadAllText($readmePath, [System.Text.Encoding]::UTF8)
    $refs = [regex]::Matches($readme, 'docs/([A-Za-z0-9_\-\.]+\.png)') |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

    $jpgEncoder = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
        Where-Object { $_.MimeType -eq 'image/jpeg' }
    $quality = New-Object System.Drawing.Imaging.EncoderParameters(1)
    $quality.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter(
        [System.Drawing.Imaging.Encoder]::Quality, 88L)

    $maxW = 960.0
    # Transparent PNGs must stay PNG (JPEG would turn the alpha into black).
    $keepPng = @('icon.png')
    foreach ($r in $refs) {
        $src = Join-Path $root ("docs\" + $r)
        if (-not (Test-Path $src)) { Write-Host ("WARN: missing docs/" + $r) -ForegroundColor Yellow; continue }
        if ($keepPng -contains $r) {
            Copy-Item $src (Join-Path $docsOut $r)
            $copied++
            continue
        }
        $outName = [System.IO.Path]::GetFileNameWithoutExtension($r) + '.jpg'
        $img = [System.Drawing.Image]::FromFile($src)
        try {
            $w = [int][Math]::Min($maxW, $img.Width)
            $h = [int][Math]::Round($img.Height * ($w / $img.Width))
            $bmp = New-Object System.Drawing.Bitmap($w, $h)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.DrawImage($img, 0, 0, $w, $h)
            $g.Dispose()
            $bmp.Save((Join-Path $docsOut $outName), $jpgEncoder, $quality)
            $bmp.Dispose()
        }
        finally { $img.Dispose() }
        $readme = $readme.Replace('docs/' + $r, 'docs/' + $outName)
        $copied++
    }

    [System.IO.File]::WriteAllText((Join-Path $stage 'README.md'), $readme, (New-Object System.Text.UTF8Encoding($false)))
}

$zip = Join-Path $dist ($tag + '.zip')
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
if (-not $KeepFolder) { Remove-Item $stage -Recurse -Force }

$size = (Get-Item $zip).Length / 1KB
Write-Host ("PACK OK: {0}  ({1:N0} KB, {2} screenshots)" -f $zip, $size, $copied) -ForegroundColor Green
