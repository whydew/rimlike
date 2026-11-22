# Generates the world-map node icons (white glyph, dark outline, transparent background) into
# Textures/Roguelike/Nodes. Re-run after changing a glyph:
#   powershell -ExecutionPolicy Bypass -File Tools\make-node-icons.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot "..\Textures\Roguelike\Nodes"
New-Item -ItemType Directory -Force $out | Out-Null

# Node kind -> glyph (Segoe UI Symbol).
$glyphs = [ordered]@{
    Expedition    = [char]0x2691  # flag
    CombatAlerted = [char]0x2694  # crossed swords
    CombatUnaware = [char]0x25C9  # target
    Escort        = [char]0x26E8  # shield
    Event         = '?'
    Elite         = [char]0x2620  # skull
    Rest          = [char]0x2668  # hot springs
    Merchant      = '$'
    Treasure      = [char]0x2726  # star
    Boss          = [char]0x265B  # crown
    Hub           = [char]0x2302  # house
}

$size = 128
foreach ($name in $glyphs.Keys) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Round dark badge so the glyph reads on any terrain.
    $badge = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(200, 20, 20, 24))
    $g.FillEllipse($badge, 6, 6, $size - 12, $size - 12)
    $ring = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 235, 235, 235)), 6
    $g.DrawEllipse($ring, 6, 6, $size - 12, $size - 12)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $family = New-Object System.Drawing.FontFamily "Segoe UI Symbol"
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $path.AddString([string]$glyphs[$name], $family, 1, 68, (New-Object System.Drawing.RectangleF 0, 4, $size, $size), $fmt)
    $g.FillPath([System.Drawing.Brushes]::White, $path)

    $bmp.Save((Join-Path $out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}
Get-ChildItem $out | Select-Object Name, Length
