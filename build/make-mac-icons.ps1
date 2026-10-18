# Draws the macOS app icon (asset catalog) and the menu bar template icon from the design of LocalTake.ico.
# Usage: powershell -File build/make-mac-icons.ps1 -Root src/LocalTake.Mac
param([string]$Root)
Add-Type -AssemblyName System.Drawing

# Design of LocalTake.ico, in a 240-unit square: corner brackets 24 thick from 36 to 204, a dot of radius 32 at 120.
function Draw-Mark($g, [double]$ox, [double]$oy, [double]$s, $brush, $dotBrush) {
    $pen = New-Object Drawing.Pen ($brush, [single](24 * $s))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Miter'
    $a = 48; $b = 95; $c = 145; $d = 192   # stroke centres: 36+12, 107-12, 133+12, 204-12
    $corners = @(
        @(@($a,$b),@($a,$a),@($b,$a)), @(@($c,$a),@($d,$a),@($d,$b)),
        @(@($d,$c),@($d,$d),@($c,$d)), @(@($b,$d),@($a,$d),@($a,$c)))
    foreach ($pts in $corners) {
        $p = [Drawing.PointF[]]($pts | ForEach-Object { New-Object Drawing.PointF ([single]($ox + $_[0]*$s)), ([single]($oy + $_[1]*$s)) })
        $g.DrawLines($pen, $p)
    }
    $r = 32 * $s
    $g.FillEllipse($dotBrush, [single]($ox + 120*$s - $r), [single]($oy + 120*$s - $r), [single](2*$r), [single](2*$r))
    $pen.Dispose()
}

function New-Canvas([int]$size) {
    $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    return $bmp, $g
}

# App icon: Apple's grid puts the rounded square at 824/1024 of the canvas, corner radius about 22.5 %.
function Save-AppIcon([int]$size, [string]$path) {
    $bmp, $g = New-Canvas $size
    $u = $size / 1024.0
    $x = 100 * $u; $w = 824 * $u; $r = 185 * $u
    $gp = New-Object Drawing.Drawing2D.GraphicsPath
    $gp.AddArc($x, $x, 2*$r, 2*$r, 180, 90); $gp.AddArc($x+$w-2*$r, $x, 2*$r, 2*$r, 270, 90)
    $gp.AddArc($x+$w-2*$r, $x+$w-2*$r, 2*$r, 2*$r, 0, 90); $gp.AddArc($x, $x+$w-2*$r, 2*$r, 2*$r, 90, 90); $gp.CloseFigure()
    $blue = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 10, 100, 173))
    $g.FillPath($blue, $gp)
    Draw-Mark $g $x $x ($w / 240.0) ([Drawing.Brushes]::White) (New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 255, 87, 34)))
    $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}

# Menu bar icon: a template image (black and alpha only, macOS tints it), the mark alone on 18 points.
function Save-StatusIcon([int]$size, [string]$path) {
    $bmp, $g = New-Canvas $size
    $s = ($size * 16.0 / 18.0) / 168.0
    $o = $size / 18.0 - 36 * $s
    Draw-Mark $g $o $o $s ([Drawing.Brushes]::Black) ([Drawing.Brushes]::Black)
    $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}

$set = Join-Path $Root 'Assets.xcassets\AppIcon.appiconset'
New-Item -ItemType Directory -Force $set | Out-Null
New-Item -ItemType Directory -Force (Join-Path $Root 'Resources') | Out-Null
$images = @()
foreach ($pt in 16, 32, 128, 256, 512) {
    foreach ($scale in 1, 2) {
        $px = $pt * $scale
        $name = "icon_${pt}x${pt}" + $(if ($scale -eq 2) { '@2x' } else { '' }) + '.png'
        Save-AppIcon $px (Join-Path $set $name)
        $images += "    { `"idiom`" : `"mac`", `"size`" : `"${pt}x${pt}`", `"scale`" : `"${scale}x`", `"filename`" : `"$name`" }"
    }
}
$json = "{`n  `"images`" : [`n" + ($images -join ",`n") + "`n  ],`n  `"info`" : { `"version`" : 1, `"author`" : `"xcode`" }`n}`n"
[IO.File]::WriteAllText((Join-Path $set 'Contents.json'), $json)
[IO.File]::WriteAllText((Join-Path $Root 'Assets.xcassets\Contents.json'), "{`n  `"info`" : { `"version`" : 1, `"author`" : `"xcode`" }`n}`n")
Save-StatusIcon 18 (Join-Path $Root 'Resources\StatusIcon.png')
Save-StatusIcon 36 (Join-Path $Root 'Resources\StatusIcon@2x.png')
