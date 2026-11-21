# Generates one badge icon per unlock into Textures/Roguelike/Unlocks/<name>.png, where <name> is the UnlockDef's
# defName without "Roguelike_Unlock_". Each badge is tinted by kind (starter green, pool blue, bonus gold) and carries
# its own glyph. Re-run after adding an unlock:
#   powershell -ExecutionPolicy Bypass -File Tools\make-unlock-icons.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot "..\Textures\Roguelike\Unlocks"
New-Item -ItemType Directory -Force $out | Out-Null

function C([int]$r, [int]$g, [int]$b, [int]$a = 255) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
$kinds = @{
    Starter = @{ Light = (C 120 214 110); Dark = (C 18 46 20) }
    Variety = @{ Light = (C 118 160 235); Dark = (C 16 28 52) }
    Power   = @{ Light = (C 240 200 110); Dark = (C 52 38 12) }
}

# name -> kind, glyph (Segoe UI Symbol code point)[, font size for thin glyphs]
$icons = [ordered]@{
    VampireCoven    = 'Starter', 0x1F377  # wine glass: the hemogen chalice
    Burrowers       = 'Starter', 0x26CF   # pick
    ThinkTank       = 'Starter', 0x1F4A1  # light bulb
    Envoys          = 'Starter', 0x1F4AC  # speech balloon
    ShockTroopers   = 'Starter', 0x26A1, 84   # lightning
    Firebrands      = 'Starter', 0x1F525  # fire
    OldBlood        = 'Starter', 0x1F44A  # fist
    HogRiders       = 'Starter', 0x1F417  # boar
    RustClan        = 'Starter', 0x2622, 84   # radiation
    FrostRaiders    = 'Starter', 0x2744, 84   # snowflake
    MechTender      = 'Starter', 0x2699, 84   # gear
    PsycasterExile  = 'Starter', 0x1F441  # eye
    ImperialNoble   = 'Starter', 0x269C, 84   # fleur-de-lis
    VoidCultists    = 'Starter', 0x1F4D5  # closed book: the codex
    TribalHunters   = 'Starter', 0x1F43E  # paw prints
    IceNomads       = 'Starter', 0x26FA   # tent
    MedKit          = 'Variety', 0x271A   # cross
    SteelLungs      = 'Variety', 0x1F4A8  # gust
    PsychicDampener = 'Variety', 0x1F507  # muted speaker
    BP_Power        = 'Variety', 0x1F50C  # plug
    BP_Medical      = 'Variety', 0x1F48A  # pill
    ShieldBelt      = 'Variety', 0x26CA   # shield
    Sniper          = 'Variety', 0x2316, 84   # crosshair
    HeadStart       = 'Power',   0x1F3C3  # runner
    LuckyDraw       = 'Power',   0x1F340  # clover
    FieldRations    = 'Power',   0x1F356  # meat on the bone
    VeteranKit      = 'Power',   0x1F392  # backpack
}

$size = 128
$family = New-Object System.Drawing.FontFamily "Segoe UI Symbol"
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, 2 * $r, 2 * $r, 180, 90)
    $p.AddArc($x + $w - 2 * $r, $y, 2 * $r, 2 * $r, 270, 90)
    $p.AddArc($x + $w - 2 * $r, $y + $h - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $p.AddArc($x, $y + $h - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $p.CloseFigure()
    return $p
}

foreach ($name in $icons.Keys) {
    $kind, $code, $em = $icons[$name]
    if (-not $em) { $em = 62 }
    $k = $kinds[$kind]
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Badge: radial glow from the centre, dark rim, light inner ring.
    $badge = RoundRect 6 6 ($size - 12) ($size - 12) 22
    $grad = New-Object System.Drawing.Drawing2D.PathGradientBrush $badge
    $grad.CenterColor = [System.Drawing.Color]::FromArgb(255, [int](($k.Light.R + $k.Dark.R * 2) / 3), [int](($k.Light.G + $k.Dark.G * 2) / 3), [int](($k.Light.B + $k.Dark.B * 2) / 3))
    $grad.SurroundColors = @($k.Dark)
    $g.FillPath($grad, $badge)
    $g.DrawPath((New-Object System.Drawing.Pen (C 8 8 10 230), 6), $badge)
    $inner = RoundRect 13 13 ($size - 26) ($size - 26) 16
    $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, $k.Light)), 2.5), $inner)
    # Top sheen.
    $sheen = RoundRect 16 16 ($size - 32) 34 12
    $g.FillPath((New-Object System.Drawing.SolidBrush (C 255 255 255 28)), $sheen)

    # Glyph: drop shadow, dark outline, white fill with a hint of the kind colour.
    $glyph = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glyph.AddString([char]::ConvertFromUtf32($code), $family, 0, $em, (New-Object System.Drawing.RectangleF 0, 4, $size, $size), $fmt)
    if ($glyph.PointCount -eq 0) { Write-Warning "$name has an empty glyph" }
    $shadow = $glyph.Clone()
    $m = New-Object System.Drawing.Drawing2D.Matrix; $m.Translate(2, 4); $shadow.Transform($m)
    $g.FillPath((New-Object System.Drawing.SolidBrush (C 0 0 0 150)), $shadow)
    $outline = New-Object System.Drawing.Pen (C 10 10 12 255), 6
    $outline.LineJoin = 'Round'
    $g.DrawPath($outline, $glyph)
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Rectangle 0, 24, $size, 84), (C 255 255 255), ([System.Drawing.Color]::FromArgb(255, [int](($k.Light.R + 255) / 2), [int](($k.Light.G + 255) / 2), [int](($k.Light.B + 255) / 2))), 90
    $g.FillPath($fill, $glyph)

    $bmp.Save((Join-Path $out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}
"{0} icons in {1}" -f $icons.Count, (Resolve-Path $out)
