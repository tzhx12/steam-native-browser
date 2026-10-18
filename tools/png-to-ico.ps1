param(
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$AssetsDir = (Join-Path $PSScriptRoot "..\src\SteamNativeBrowser.WinUI\Assets"),
    [switch]$RoundedMask
)

# 把一张方图转换成多尺寸 app.ico（PNG 负载）与 app.png（256）。
Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Source))
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Resize-ToPng([System.Drawing.Bitmap]$image, [int]$size, [bool]$rounded) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    if ($rounded) {
        $radius = [float]($size * 0.22)
        $path = New-RoundedPath 0 0 $size $size $radius
        $g.SetClip($path)
    }

    $g.DrawImage($image, 0, 0, $size, $size)

    if ($rounded) {
        $g.ResetClip()
    }

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $g.Dispose(); $bmp.Dispose(); $ms.Dispose()
    return ,$bytes
}

$assets = if ([System.IO.Path]::IsPathRooted($AssetsDir)) {
    [System.IO.Path]::GetFullPath($AssetsDir)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $AssetsDir))
}
New-Item -ItemType Directory -Force -Path $assets | Out-Null

foreach ($size in $sizes) {
    $pngs += ,(Resize-ToPng $src $size $RoundedMask.IsPresent)
}

# 主图 app.png（256）
[System.IO.File]::WriteAllBytes((Join-Path $assets "app.png"), $pngs[$sizes.Count - 1])

# app.ico
$icoPath = Join-Path $assets "app.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $bw.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
    $bw.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $bw.Write($png) }
$bw.Flush(); $bw.Close(); $fs.Close()
$src.Dispose()

Write-Host "icon -> $icoPath"
Get-ChildItem $assets -Filter "app.*" | Select-Object Name, Length
