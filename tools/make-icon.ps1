# Generates app.ico (multi-size PNG-compressed ICO).
#
# Styles (pick with -Style, default "fbar"):
#   wave   : dock bar floating over water                                      (default)
#   dock   : glass dock bar + coloured dots + faint reflection (previous look)
#   letter : bold "F" standing on a thin glass dock bar
#   pier   : the dock bar IS a pier deck on two pilings, standing in water
#   hanzi  : the character for "wu" (dock) drawn as a glyph
#
# Small sizes (<=32) are simplified so the silhouette still reads in a 16px slot.
# Everything is rendered 4x supersampled and downscaled with HighQualityBicubic.
# ASCII-only on purpose: Windows PowerShell 5.1 treats BOM-less files as ANSI.
param(
    [ValidateSet('fbar', 'pier', 'boat', 'dock', 'letter', 'wave', 'hanzi')]
    [string]$Style = 'wave',
    [string]$Out = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($Out)) { $Out = Join-Path $root 'app.ico' }

# Hanzi U+575E ("wu" of Fu Wu) built from code points: this file stays ASCII.
$glyph = [string][char]0x575E

function Path-Round([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    if ($r -lt 0.75) { $r = 0.75 }
    if ($r * 2 -gt $w) { $r = $w / 2 }
    if ($r * 2 -gt $h) { $r = $h / 2 }
    $d = $r * 2
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc([float]$x, [float]$y, [float]$d, [float]$d, 180, 90)
    $p.AddArc([float]($x + $w - $d), [float]$y, [float]$d, [float]$d, 270, 90)
    $p.AddArc([float]($x + $w - $d), [float]($y + $h - $d), [float]$d, [float]$d, 0, 90)
    $p.AddArc([float]$x, [float]($y + $h - $d), [float]$d, [float]$d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-VBrush([System.Drawing.Color]$c1, [System.Drawing.Color]$c2,
                    [double]$x, [double]$y, [double]$w, [double]$h) {
    $rect = New-Object System.Drawing.RectangleF([float]$x, [float]$y, [float]$w, [float]$h)
    return New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect, $c1, $c2, [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
}

function Get-DotColors([bool]$small) {
    if ($small) {
        $c = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
        return @( $c, $c, $c )
    }
    return @(
        [System.Drawing.Color]::FromArgb(255, 0xFF, 0x7A, 0x59),
        [System.Drawing.Color]::FromArgb(255, 0xFF, 0xC6, 0x4D),
        [System.Drawing.Color]::FromArgb(255, 0x4F, 0xD6, 0xA6),
        [System.Drawing.Color]::FromArgb(255, 0x8F, 0xBC, 0xFF))
}

function Draw-Tile([System.Drawing.Graphics]$g, [double]$R) {
    $tile = Path-Round 0 0 $R $R ($R * 0.225)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF(0, 0, [float]$R, [float]$R)),
        [System.Drawing.Color]::FromArgb(255, 0x2F, 0x6B, 0xFF),
        [System.Drawing.Color]::FromArgb(255, 0x7C, 0x4D, 0xFF),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillPath($bg, $tile)
    $bg.Dispose()

    $clip = New-Object System.Drawing.Drawing2D.GraphicsPath
    $clip.AddPath($tile, $false)
    $g.SetClip($clip)
    $clip.Dispose()
    $hl = New-VBrush ([System.Drawing.Color]::FromArgb(46, 255, 255, 255)) `
                     ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)) `
                     0 0 $R ($R * 0.62)
    $g.FillRectangle($hl, 0, 0, [float]$R, [float]($R * 0.62))
    $hl.Dispose()

    $vg = New-VBrush ([System.Drawing.Color]::FromArgb(0, 0, 0, 0)) `
                     ([System.Drawing.Color]::FromArgb(34, 8, 0, 40)) `
                     0 ($R * 0.52) $R ($R * 0.48)
    $g.FillRectangle($vg, 0, [float]($R * 0.52), [float]$R, [float]($R * 0.48))
    $vg.Dispose()
    $g.ResetClip()

    $rim = Path-Round ($R * 0.008) ($R * 0.008) ($R * 0.984) ($R * 0.984) ($R * 0.218)
    $rp = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(52, 255, 255, 255), [float]([Math]::Max(1.0, $R * 0.005)))
    $g.DrawPath($rp, $rim)
    $rp.Dispose()
    $rim.Dispose()
    $tile.Dispose()
}

# ---- a glass dock bar, optionally with dots on it ----
function Draw-Bar([System.Drawing.Graphics]$g, [double]$S, [double]$x, [double]$y,
                  [double]$w, [double]$h, [bool]$withDots, [bool]$small) {
    $path = Path-Round $x $y $w $h ($h / 2.0)
    $fill = New-VBrush ([System.Drawing.Color]::FromArgb(120, 255, 255, 255)) `
                       ([System.Drawing.Color]::FromArgb(58, 255, 255, 255)) `
                       $x $y $w $h
    $g.FillPath($fill, $path)
    $fill.Dispose()
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(150, 255, 255, 255), [float]([Math]::Max(1.0, $S * 0.011)))
    $g.DrawPath($pen, $path)
    $pen.Dispose()
    $path.Dispose()

    if (-not $withDots) { return }

    $colors = Get-DotColors $small
    $n = $colors.Count
    $dotR = $h * 0.26
    $cy = $y + $h / 2.0
    for ($i = 0; $i -lt $n; $i++) {
        $f = ($i + 1.0) / ($n + 1.0)
        $cx = $x + $w * $f
        $b = New-Object System.Drawing.SolidBrush($colors[$i])
        $g.FillEllipse($b, [float]($cx - $dotR), [float]($cy - $dotR), [float]($dotR * 2), [float]($dotR * 2))
        $b.Dispose()
    }
}

# ---- style: fbar (an F whose crossbar IS the glass dock bar) ----
function Draw-FBar([System.Drawing.Graphics]$g, [double]$S, [bool]$small) {
    $w = $S * 0.150
    $x0 = $S * 0.290
    $top = $S * 0.190
    $bot = $S * 0.700
    $cap = ($w / 2.0)

    # 1) the dock bar sits UNDER the glyph, so the letter stays one unbroken shape
    $barH = $S * 0.170
    $barY = $S * 0.408
    $barX = $x0 + $w * 0.30
    $barW = ($S * 0.760) - $barX
    $barPath = Path-Round $barX $barY $barW $barH ($barH / 2.0)

    if ($small) {
        $c1 = [System.Drawing.Color]::White
        $c2 = [System.Drawing.Color]::White
    }
    else {
        $c1 = [System.Drawing.Color]::FromArgb(228, 255, 255, 255)
        $c2 = [System.Drawing.Color]::FromArgb(186, 255, 255, 255)
    }
    $fill = New-VBrush $c1 $c2 $barX $barY $barW $barH
    $g.FillPath($fill, $barPath)
    $fill.Dispose()
    if (-not $small) {
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(90, 255, 255, 255), [float]([Math]::Max(1.0, $S * 0.010)))
        $g.DrawPath($pen, $barPath)
        $pen.Dispose()
    }
    $barPath.Dispose()

    # 2) dots riding the bar
    if (-not $small) {
        $colors = Get-DotColors $small
        $dotR = $barH * 0.235
        $cy = $barY + $barH / 2.0
        for ($i = 0; $i -lt $colors.Count; $i++) {
            $f = ($i + 1.0) / ($colors.Count + 1.0)
            $cx = $barX + $barW * $f
            $b = New-Object System.Drawing.SolidBrush($colors[$i])
            $g.FillEllipse($b, [float]($cx - $dotR), [float]($cy - $dotR), [float]($dotR * 2), [float]($dotR * 2))
            $b.Dispose()
        }
    }

    # 3) stem + top arm on top: the crossbar visually grows out of the letter
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $stem = Path-Round $x0 $top $w ($bot - $top) $cap
    $g.FillPath($white, $stem)
    $stem.Dispose()

    $armW = ($S * 0.705) - $x0
    $topArm = Path-Round $x0 $top $armW $w $cap
    $g.FillPath($white, $topArm)
    $topArm.Dispose()
    $white.Dispose()
}
# ---- shared: water waves ----
function Draw-Waves([System.Drawing.Graphics]$g, [double]$S, [bool]$small,
                    [double]$yFirst, [double]$gap, [int]$count) {
    $amp = $S * 0.022
    $thick = $S * 0.026
    if ($small) { $amp = $S * 0.030; $thick = $S * 0.050 }
    $x1 = $S * 0.120
    $x2 = $S * 0.880
    $span = $x2 - $x1
    for ($k = 0; $k -lt $count; $k++) {
        $yc = $S * ($yFirst + $gap * $k)
        $pts = @()
        $steps = 44
        for ($i = 0; $i -le $steps; $i++) {
            $t = $i / [double]$steps
            $x = $x1 + $span * $t
            $y = $yc + $amp * [Math]::Sin(($t * 2.0 * [Math]::PI * 1.15) + ($k * 1.35))
            $pts += , (New-Object System.Drawing.PointF([float]$x, [float]$y))
        }
        $alpha = 150 - $k * 62
        $wp = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb($alpha, 255, 255, 255),
            [float]($thick - $k * ($thick * 0.22)))
        $wp.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $wp.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $wp.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawLines($wp, [System.Drawing.PointF[]]$pts)
        $wp.Dispose()
    }
}

# ---- style: pier (the dock bar IS the pier deck, standing in water) ----
function Draw-Pier([System.Drawing.Graphics]$g, [double]$S, [bool]$small) {
    # pilings first; the water band and the deck overlap them
    $postW = $S * 0.080
    $postTop = $S * 0.520
    $postBot = $S * 0.790
    $pb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(228, 255, 255, 255))
    foreach ($cx in @( ($S * 0.300), ($S * 0.700) )) {
        $p = Path-Round ($cx - $postW / 2.0) $postTop $postW ($postBot - $postTop) ($postW / 2.0)
        $g.FillPath($pb, $p)
        $p.Dispose()
    }
    $pb.Dispose()

    # water: a wavy-topped band filling the bottom of the tile
    $yTop = 0.800
    $amp = 0.020
    if ($small) { $yTop = 0.780; $amp = 0.030 }
    $steps = 40
    $top = @()
    for ($i = 0; $i -le $steps; $i++) {
        $tt = $i / [double]$steps
        $x = $S * $tt
        $y = $S * ($yTop + $amp * [Math]::Sin(($tt * 2.0 * [Math]::PI * 1.6)))
        $top += , (New-Object System.Drawing.PointF([float]$x, [float]$y))
    }

    $water = New-Object System.Drawing.Drawing2D.GraphicsPath
    $water.AddLines([System.Drawing.PointF[]]$top)
    $water.AddLine([float]$S, [float]$S, [float]0, [float]$S)
    $water.CloseFigure()

    # clip the water to the tile shape, otherwise its square corners poke out of
    # the rounded tile at the bottom
    $tileClip = Path-Round 0 0 $S $S ($S * 0.225)
    $g.SetClip($tileClip)

    $wf = New-VBrush ([System.Drawing.Color]::FromArgb(120, 138, 205, 255)) `
                     ([System.Drawing.Color]::FromArgb(38, 138, 205, 255)) `
                     0 ($S * $yTop) $S ($S - $S * $yTop)
    $g.FillPath($wf, $water)
    $wf.Dispose()
    $water.Dispose()

    # crest line so the surface reads as water, not as a shadow
    $cp = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(215, 255, 255, 255),
        [float]([Math]::Max(1.0, $S * 0.022)))
    $cp.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cp.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cp.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($cp, [System.Drawing.PointF[]]$top)
    $cp.Dispose()
    $g.ResetClip()
    $tileClip.Dispose()

    # deck = the glass dock bar with the app dots on top
    if ($small) {
        $bw = $S * 0.780
        $bh = $S * 0.200
        $by = $S * 0.320
    }
    else {
        $bw = $S * 0.745
        $bh = $S * 0.185
        $by = $S * 0.350
    }
    $bx = ($S - $bw) / 2.0
    Draw-Bar $g $S $bx $by $bw $bh $true $small
}
# ---- style: boat (a small hull on the water, moored at the bar) ----
function Draw-Boat([System.Drawing.Graphics]$g, [double]$S, [bool]$small) {
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)

    # hull: bottom half of an ellipse, closed along the deck line
    $hull = New-Object System.Drawing.Drawing2D.GraphicsPath
    $hx = $S * 0.165
    $hw = $S * 0.670
    $htop = $S * 0.470
    $hh = $S * 0.250
    $hull.AddArc([float]$hx, [float]$htop, [float]$hw, [float]($hh * 2.0), 0, 180)
    $hull.CloseFigure()
    $g.FillPath($white, $hull)
    $hull.Dispose()

    # mast + sail
    $mastW = $S * 0.062
    $mastX = $S * 0.500 - $mastW / 2.0
    $mast = Path-Round $mastX ($S * 0.230) $mastW ($S * 0.470 - $S * 0.230) ($mastW / 2.0)
    $g.FillPath($white, $mast)
    $mast.Dispose()

    $sail = New-Object System.Drawing.Drawing2D.GraphicsPath
    $sail.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([float]($S * 0.560), [float]($S * 0.265))),
        (New-Object System.Drawing.PointF([float]($S * 0.760), [float]($S * 0.470))),
        (New-Object System.Drawing.PointF([float]($S * 0.560), [float]($S * 0.470)))))
    $g.FillPath($white, $sail)
    $sail.Dispose()
    $white.Dispose()

    Draw-Waves $g $S $small 0.845 0.075 2
}
# ---- style: dock (bar + dots + light bleed under the dots) ----
function Draw-Dock([System.Drawing.Graphics]$g, [double]$S, [bool]$small) {
    if ($small) {
        $bw = $S * 0.700
        $bh = $S * 0.270
        $by = $S * 0.365
    }
    else {
        $bw = $S * 0.640
        $bh = $S * 0.200
        $by = $S * 0.345
    }
    $bx = ($S - $bw) / 2.0
    Draw-Bar $g $S $bx $by $bw $bh $true $small

    if (-not $small) {
        $colors = Get-DotColors $small
        $dotR = $bh * 0.26
        $y0 = $by + $bh - $S * 0.015
        $hh = $S * 0.095
        for ($i = 0; $i -lt $colors.Count; $i++) {
            $f = ($i + 1.0) / ($colors.Count + 1.0)
            $cx = $bx + $bw * $f
            $dw = $dotR * 2 * 1.55
            $c = $colors[$i]
            $c1 = [System.Drawing.Color]::FromArgb(125, $c.R, $c.G, $c.B)
            $c2 = [System.Drawing.Color]::FromArgb(0, $c.R, $c.G, $c.B)
            $path = Path-Round ($cx - $dw / 2.0) $y0 $dw $hh ($dw / 2.0)
            $br = New-VBrush $c1 $c2 ($cx - $dw / 2.0) $y0 $dw $hh
            $g.FillPath($br, $path)
            $br.Dispose()
            $path.Dispose()
        }
    }
}

# ---- style: letter (bold F standing on a thin dock bar) ----
function Draw-Letter([System.Drawing.Graphics]$g, [double]$S, [bool]$small) {
    $w = $S * 0.150
    $x0 = $S * 0.300
    $top = $S * 0.185
    $bot = $S * 0.675
    $cap = ($w / 2.0)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)

    $stem = Path-Round $x0 $top $w ($bot - $top) $cap
    $g.FillPath($white, $stem)
    $stem.Dispose()

    $armW = ($S * 0.700) - $x0
    $topArm = Path-Round $x0 $top $armW $w $cap
    $g.FillPath($white, $topArm)
    $topArm.Dispose()

    $midTop = $S * 0.415
    $midH = $w * 0.92
    $midArm = Path-Round $x0 $midTop (($S * 0.620) - $x0) $midH $cap
    $g.FillPath($white, $midArm)
    $midArm.Dispose()
    $white.Dispose()

    if ($small) {
        $bw = $S * 0.640
        $bh = $S * 0.150
    }
    else {
        $bw = $S * 0.600
        $bh = $S * 0.130
    }
    $bx = ($S - $bw) / 2.0
    $by = $S * 0.775
    Draw-Bar $g $S $bx $by $bw $bh $false $small
}

# ---- style: wave (dock bar floating over water) ----
function Draw-Wave([System.Drawing.Graphics]$g, [double]$S, [bool]$small) {
    if ($small) {
        $bw = $S * 0.710
        $bh = $S * 0.230
        $by = $S * 0.300
    }
    else {
        $bw = $S * 0.640
        $bh = $S * 0.185
        $by = $S * 0.305
    }
    $bx = ($S - $bw) / 2.0
    Draw-Bar $g $S $bx $by $bw $bh $true $small

    $x1 = $S * 0.100
    $x2 = $S * 0.900
    $span = $x2 - $x1
    $cycles = 1.55

    $levels = @(
        @{ y = 0.592; amp = 0.030; thick = 0.040; alpha = 190 },
        @{ y = 0.682; amp = 0.026; thick = 0.030; alpha = 112 })
    if ($small) {
        $levels = @(
            @{ y = 0.645; amp = 0.045; thick = 0.080; alpha = 212 })
    }

    foreach ($lv in $levels) {
        $pts = @()
        $steps = 56
        for ($i = 0; $i -le $steps; $i++) {
            $tt = $i / [double]$steps
            $x = $x1 + $span * $tt
            $y = $S * $lv.y + $S * $lv.amp * [Math]::Sin($tt * 2.0 * [Math]::PI * $cycles)
            $pts += , (New-Object System.Drawing.PointF([float]$x, [float]$y))
        }
        $wp = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb($lv.alpha, 255, 255, 255),
            [float]([Math]::Max(1.0, $S * $lv.thick)))
        $wp.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $wp.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $wp.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawLines($wp, [System.Drawing.PointF[]]$pts)
        $wp.Dispose()
    }
}
# ---- style: hanzi (the glyph itself) ----
function Draw-Hanzi([System.Drawing.Graphics]$g, [double]$S, [bool]$small) {
    $fam = New-Object System.Drawing.FontFamily('Microsoft YaHei')
    $em = $S * 0.560
    if ($small) { $em = $S * 0.620 }
    $font = New-Object System.Drawing.Font($fam, [float]$em, [System.Drawing.FontStyle]::Bold,
        [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $box = New-Object System.Drawing.RectangleF(0, [float]($S * -0.02), [float]$S, [float]$S)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.DrawString($glyph, $font, $white, $box, $sf)
    $white.Dispose()
    $font.Dispose()
    $sf.Dispose()
    $fam.Dispose()
}

function Scale-Into([System.Drawing.Image]$src, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle(0, 0, $size, $size)))
    $g.Dispose()
    return $bmp
}

function New-IconBitmap([int]$size, [string]$style) {
    $small = $size -le 32
    $ss = 4
    $R = $size * $ss
    if ($R -gt 1024) { $R = 1024; $ss = [double]$R / $size }

    $tileBig = New-Object System.Drawing.Bitmap($R, $R, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gt = [System.Drawing.Graphics]::FromImage($tileBig)
    $gt.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $gt.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gt.Clear([System.Drawing.Color]::Transparent)
    Draw-Tile $gt $R
    $gt.Dispose()

    $artBig = New-Object System.Drawing.Bitmap($R, $R, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $ga = [System.Drawing.Graphics]::FromImage($artBig)
    $ga.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $ga.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $ga.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $ga.Clear([System.Drawing.Color]::Transparent)
    switch ($style) {
        'pier' { Draw-Pier $ga $R $small }
        'boat' { Draw-Boat $ga $R $small }
        'dock' { Draw-Dock $ga $R $small }
        'letter' { Draw-Letter $ga $R $small }
        'wave' { Draw-Wave $ga $R $small }
        'hanzi' { Draw-Hanzi $ga $R $small }
        default { Draw-FBar $ga $R $small }
    }
    $ga.Dispose()

    $tile = Scale-Into $tileBig $size
    $art = Scale-Into $artBig $size
    $tileBig.Dispose(); $artBig.Dispose()

    $icon = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gi = [System.Drawing.Graphics]::FromImage($icon)
    $gi.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gi.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gi.Clear([System.Drawing.Color]::Transparent)
    $gi.DrawImage($tile, 0, 0)
    $gi.DrawImage($art, 0, 0)
    $gi.Dispose()
    $tile.Dispose(); $art.Dispose()
    return $icon
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$blobs = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s $Style
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $blobs += , $ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()
}

$fs = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    if ($s -ge 256) { $dim = 0 } else { $dim = $s }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$blobs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $blobs[$i].Length
}
foreach ($b in $blobs) { $bw.Write([byte[]]$b) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($Out, $fs.ToArray())
$bw.Dispose(); $fs.Dispose()

Write-Output ("icon written: " + $Out + "  style=" + $Style + "  (" + (Get-Item $Out).Length + " bytes)")
