# Generates Assets\app.ico (multi-size, PNG-compressed entries).
Add-Type -AssemblyName System.Drawing

$outFile = Join-Path $PSScriptRoot "..\src\SteamNativeBrowser\Assets\app.ico"
$outFile = [System.IO.Path]::GetFullPath($outFile)
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [float]($s * 0.03)
    $w = [float]($s - 2 * $pad)
    $r = [float]($s * 0.235)
    $path = New-RoundedPath $pad $pad $w $w $r
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF($s, $s)),
        [System.Drawing.Color]::FromArgb(255, 108, 210, 255),
        [System.Drawing.Color]::FromArgb(255, 0, 95, 184))
    $g.FillPath($brush, $path)

    # subtle top highlight
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF(0, $s * 0.55)),
        [System.Drawing.Color]::FromArgb(70, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.SetClip($path)
    $g.FillRectangle($hl, 0, 0, $s, $s * 0.55)
    $g.ResetClip()

    # globe glyph
    $font = New-Object System.Drawing.Font("Segoe MDL2 Assets", [float]($s * 0.56), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $rect = New-Object System.Drawing.RectangleF(0, [float]($s * 0.015), $s, $s)
    $g.DrawString([string][char]0xE774, $font, $white, $rect, $fmt)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,($ms.ToArray())

    $g.Dispose(); $bmp.Dispose(); $ms.Dispose(); $font.Dispose(); $white.Dispose()
    $brush.Dispose(); $hl.Dispose(); $path.Dispose()
}

$fs = [System.IO.File]::Create($outFile)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush(); $bw.Close(); $fs.Close()
Write-Host "icon written: $outFile ($([math]::Round((Get-Item $outFile).Length/1kb,1)) KB)"
