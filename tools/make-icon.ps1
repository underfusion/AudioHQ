# Generates src/AudioHQ.App/app.ico - a minimalist AudioHQ mark:
# three rounded fader/EQ bars on a dark rounded tile (matches the app theme).
# PNG-compressed multi-size ICO (16/32/48/64/128/256), Vista+ format.
Add-Type -AssemblyName System.Drawing

$sizes  = @(16, 32, 48, 64, 128, 256)
$outDir = Join-Path $PSScriptRoot '..\src\AudioHQ.App'
$out    = Join-Path $outDir 'app.ico'

# Theme colors (ARGB) - same palette as App.xaml.
$bg    = [System.Drawing.Color]::FromArgb(255, 0x22, 0x28, 0x34)  # CardBrush
$green = [System.Drawing.Color]::FromArgb(255, 0x22, 0xC5, 0x5E)
$blue  = [System.Drawing.Color]::FromArgb(255, 0x3B, 0x82, 0xF6)
$red   = [System.Drawing.Color]::FromArgb(255, 0xEF, 0x44, 0x44)
$bars  = @($green, $blue, $red)
$frac  = @(0.55, 1.0, 0.72)   # relative bar heights (bottom-anchored)

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    if ($d -gt $w) { $d = $w }
    if ($d -gt $h) { $d = $h }
    $p.AddArc($x,            $y,            $d, $d, 180, 90)
    $p.AddArc($x + $w - $d,  $y,            $d, $d, 270, 90)
    $p.AddArc($x + $w - $d,  $y + $h - $d,  $d, $d,   0, 90)
    $p.AddArc($x,            $y + $h - $d,  $d, $d,  90, 90)
    $p.CloseFigure()
    return $p
}

$pngStreams = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded tile background.
    $tileR = [single]($s * 0.22)
    $tile  = New-RoundRect 0 0 $s $s $tileR
    $brush = New-Object System.Drawing.SolidBrush($bg)
    $g.FillPath($brush, $tile)
    $brush.Dispose(); $tile.Dispose()

    # Three fader bars, bottom-anchored inside an inner padding box.
    $pad     = [single]($s * 0.24)
    $inner   = [single]($s - 2 * $pad)
    $gap     = [single]($inner * 0.16)
    $barW    = [single](($inner - 2 * $gap) / 3.0)
    $baseY   = [single]($s - $pad)
    $barR    = [single]($barW * 0.5)
    for ($i = 0; $i -lt 3; $i++) {
        $bx = [single]($pad + $i * ($barW + $gap))
        $bh = [single]($inner * $frac[$i])
        $by = [single]($baseY - $bh)
        $rect = New-RoundRect $bx $by $barW $bh $barR
        $bb   = New-Object System.Drawing.SolidBrush($bars[$i])
        $g.FillPath($bb, $rect)
        $bb.Dispose(); $rect.Dispose()
    }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngStreams += ,($ms.ToArray())
}

# Assemble ICO container (PNG-embedded entries).
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)            # reserved
$bw.Write([uint16]1)            # type = icon
$bw.Write([uint16]$sizes.Count) # image count

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s   = $sizes[$i]
    $len = $pngStreams[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim)       # width  (0 => 256)
    $bw.Write([byte]$dim)       # height (0 => 256)
    $bw.Write([byte]0)          # palette
    $bw.Write([byte]0)          # reserved
    $bw.Write([uint16]1)        # color planes
    $bw.Write([uint16]32)       # bits per pixel
    $bw.Write([uint32]$len)     # bytes in resource
    $bw.Write([uint32]$offset)  # data offset
    $offset += $len
}
foreach ($png in $pngStreams) { $bw.Write($png) }
$bw.Flush(); $bw.Close(); $fs.Close()

Write-Host "Wrote $out ($((Get-Item $out).Length) bytes, $($sizes.Count) sizes)"
