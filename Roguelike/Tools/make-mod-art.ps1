# Generates the mod's icon (About/ModIcon.png) and banner (About/Preview.png) from the node and unlock badges.
# Run make-node-icons.ps1 and make-unlock-icons.ps1 first. Re-run after changing either:
#   powershell -ExecutionPolicy Bypass -File Tools\make-mod-art.ps1
Add-Type -AssemblyName System.Drawing
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$about = Join-Path $root "About"
$nodes = Join-Path $root "Textures\Roguelike\Nodes"
$unlocks = Join-Path $root "Textures\Roguelike\Unlocks"

function C([int]$r, [int]$g, [int]$b, [int]$a = 255) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
$gold = C 237 199 107
$parchment = C 235 227 209

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, 2 * $r, 2 * $r, 180, 90)
    $p.AddArc($x + $w - 2 * $r, $y, 2 * $r, 2 * $r, 270, 90)
    $p.AddArc($x + $w - 2 * $r, $y + $h - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $p.AddArc($x, $y + $h - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $p.CloseFigure()
    return $p
}

function Glow($g, [float]$cx, [float]$cy, [float]$radius, $color, [int]$alpha) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddEllipse($cx - $radius, $cy - $radius, 2 * $radius, 2 * $radius)
    $b = New-Object System.Drawing.Drawing2D.PathGradientBrush $p
    $b.CenterColor = [System.Drawing.Color]::FromArgb($alpha, $color)
    $b.SurroundColors = @([System.Drawing.Color]::FromArgb(0, $color))
    $g.FillPath($b, $p)
}

function Text($g, [string]$s, [string]$font, [float]$size, [int]$style, $color, [float]$x, [float]$y, [float]$w, [float]$h, [string]$align = 'Center', [float]$outline = 0) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = $align; $fmt.LineAlignment = 'Center'
    $path.AddString($s, (New-Object System.Drawing.FontFamily $font), $style, $size, (New-Object System.Drawing.RectangleF $x, $y, $w, $h), $fmt)
    $shadow = $path.Clone(); $m = New-Object System.Drawing.Drawing2D.Matrix; $m.Translate([math]::Max(1, $size / 40), [math]::Max(1, $size / 25)); $shadow.Transform($m)
    $g.FillPath((New-Object System.Drawing.SolidBrush (C 0 0 0 170)), $shadow)
    if ($outline -gt 0) { $pen = New-Object System.Drawing.Pen (C 12 10 8 255), $outline; $pen.LineJoin = 'Round'; $g.DrawPath($pen, $path) }
    $g.FillPath((New-Object System.Drawing.SolidBrush $color), $path)
}

function Card($g, [float]$cx, [float]$cy, [float]$w, [float]$h, [float]$angle, $kind, [string]$banner, [string]$badge) {
    $state = $g.Save()
    $g.TranslateTransform($cx, $cy); $g.RotateTransform($angle)
    Glow $g 0 0 ($h * 0.75) $kind 70
    $body = RoundRect (-$w / 2) (-$h / 2) $w $h 10
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF (-$w / 2), (-$h / 2), $w, $h), (C 44 44 49), (C 18 18 21), 90
    $g.FillPath($grad, $body)
    $g.DrawPath((New-Object System.Drawing.Pen $kind, 4), $body)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, [int]($kind.R * 0.5), [int]($kind.G * 0.5), [int]($kind.B * 0.5)))), (-$w / 2 + 4), (-$h / 2 + 4), ($w - 8), 30)
    Text $g $banner "Segoe UI" 15 1 $parchment (-$w / 2) (-$h / 2 + 4) $w 30
    $img = [System.Drawing.Image]::FromFile($badge)
    $s = $w * 0.62
    Glow $g 0 (-$h * 0.08) ($s * 0.9) $kind 120
    $g.DrawImage($img, (-$s / 2), (-$h * 0.08 - $s / 2), $s, $s)
    $img.Dispose()
    for ($i = 0; $i -lt 4; $i++) {
        $lw = $w * (0.72 - 0.12 * ($i % 2))
        $g.FillRectangle((New-Object System.Drawing.SolidBrush (C 220 214 200 70)), (-$lw / 2), ($h * 0.24 + $i * 14), $lw, 5)
    }
    $g.Restore($state)
}

function Background($g, [int]$w, [int]$h) {
    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $g.FillRectangle((New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (C 28 26 30), (C 10 10 12), 90), $rect)
    Glow $g ($w * 0.5) ($h * 0.55) ($w * 0.55) (C 120 80 40) 70
}

# ---------- banner ----------
$W = 1280; $H = 720
$bmp = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
Background $g $W $H

# The run's node graph, faint, behind everything.
$rand = New-Object System.Random 7
$graph = @()
for ($row = 0; $row -lt 6; $row++) {
    $count = @(3, 3, 3, 3, 2, 1)[$row]
    for ($i = 0; $i -lt $count; $i++) {
        $x = 160 + $row * 192 + $rand.Next(-20, 20)
        $y = $H / 2 + ($i - ($count - 1) / 2) * 190 + $rand.Next(-25, 25)
        $graph += , @($row, $x, $y)
    }
}
$linePen = New-Object System.Drawing.Pen (C 237 199 107 60), 3
foreach ($a in $graph) { foreach ($b in $graph) { if ($b[0] -eq $a[0] + 1 -and [math]::Abs($b[2] - $a[2]) -lt 260) { $g.DrawLine($linePen, $a[1], $a[2], $b[1], $b[2]) } } }
$kinds = @("Expedition", "CombatAlerted", "Event", "Rest", "Merchant", "CombatUnaware", "Treasure", "Escort", "Elite", "Expedition", "Event", "Rest", "Elite", "Merchant", "Boss")
$k = 0
foreach ($n in $graph) {
    $img = [System.Drawing.Image]::FromFile((Join-Path $nodes ($kinds[$k % $kinds.Count] + ".png")))
    $attr = New-Object System.Drawing.Imaging.ImageAttributes
    $cm = New-Object System.Drawing.Imaging.ColorMatrix; $cm.Matrix33 = 0.35; $attr.SetColorMatrix($cm)
    $g.DrawImage($img, (New-Object System.Drawing.Rectangle ([int]$n[1] - 28), ([int]$n[2] - 28), 56, 56), 0, 0, $img.Width, $img.Height, 'Pixel', $attr)
    $img.Dispose(); $k++
}
$vig = New-Object System.Drawing.Drawing2D.GraphicsPath
$vig.AddEllipse(-$W * 0.25, -$H * 0.45, $W * 1.5, $H * 1.9)
$vb = New-Object System.Drawing.Drawing2D.PathGradientBrush $vig
$vb.CenterColor = C 0 0 0 0; $vb.SurroundColors = @((C 0 0 0 230)); $vb.FocusScales = New-Object System.Drawing.PointF 0.55, 0.55
$g.FillPath($vb, $vig)

# A fanned hand of cards.
$green = C 120 214 110; $blue = C 118 160 235; $amber = C 242 140 40
Card $g 470 470 190 270 -14 $blue "RELIC" (Join-Path $unlocks "MedKit.png")
Card $g 810 470 190 270 14 $amber "BLUEPRINT" (Join-Path $unlocks "BP_Power.png")
Card $g 640 445 210 300 0 $green "STARTER" (Join-Path $unlocks "Starter_crashlanded.png")

# Title.
Glow $g ($W / 2) 140 330 $gold 60
Text $g "RIMLIKE" "Segoe UI" 128 1 $gold 0 50 $W 170 'Center' 10
Text $g "Roguelike runs for RimWorld" "Segoe UI" 34 0 $parchment 0 190 $W 60 'Center' 0
$g.FillRectangle((New-Object System.Drawing.SolidBrush (C 237 199 107 140)), ($W / 2 - 220), 252, 440, 2)
$bmp.Save((Join-Path $about "Preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

# ---------- icon ----------
$S = 256
$bmp = New-Object System.Drawing.Bitmap $S, $S
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
$g.Clear([System.Drawing.Color]::Transparent)
$badge = RoundRect 10 10 ($S - 20) ($S - 20) 44
$grad = New-Object System.Drawing.Drawing2D.PathGradientBrush $badge
$grad.CenterColor = C 120 88 40; $grad.SurroundColors = @((C 30 22 10))
$g.FillPath($grad, $badge)
$g.DrawPath((New-Object System.Drawing.Pen (C 8 8 10 235), 10), $badge)
$g.DrawPath((New-Object System.Drawing.Pen (C 237 199 107 230), 5), (RoundRect 24 24 ($S - 48) ($S - 48) 32))
# Three cards fanned in the middle.
foreach ($c in @(@(-18, -40, (C 118 160 235)), @(18, 40, (C 242 140 40)), @(0, 0, (C 120 214 110)))) {
    $state = $g.Save(); $g.TranslateTransform($S / 2 + $c[1], $S / 2 + 6); $g.RotateTransform($c[0])
    $card = RoundRect -46 -66 92 132 10
    $g.FillPath((New-Object System.Drawing.SolidBrush (C 26 26 30)), $card)
    $g.DrawPath((New-Object System.Drawing.Pen $c[2], 6), $card)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $c[2]), -40, -60, 80, 16)
    $g.Restore($state)
}
Text $g ([char]0x2694) "Segoe UI Symbol" 100 0 (C 255 248 230) 0 14 $S $S 'Center' 10
$bmp.Save((Join-Path $about "ModIcon.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Get-ChildItem $about *.png | Select-Object Name, Length
