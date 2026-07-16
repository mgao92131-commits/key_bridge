Add-Type -AssemblyName System.Drawing

$sourcePath = Join-Path $PWD "icon.png"
if (-Not (Test-Path $sourcePath)) {
    Write-Host "Source icon.png not found!"
    exit 1
}

$img = [System.Drawing.Image]::FromFile($sourcePath)
$bmp = New-Object System.Drawing.Bitmap($img)

# Find bounds
$minX = $bmp.Width; $minY = $bmp.Height; $maxX = 0; $maxY = 0
for ($y=0; $y -lt $bmp.Height; $y++) {
    for ($x=0; $x -lt $bmp.Width; $x++) {
        if ($bmp.GetPixel($x, $y).A -gt 0) {
            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}

if ($maxX -lt $minX) {
    Write-Host "Image is completely transparent."
    exit 1
}

$contentW = $maxX - $minX + 1
$contentH = $maxY - $minY + 1
$centerX = $minX + $contentW / 2
$centerY = $minY + $contentH / 2

$size = [math]::Max($contentW, $contentH)
# Add 10% padding
$paddedSize = [int][math]::Round($size * 1.1)

# Create squared and cropped image
$squaredBmp = New-Object System.Drawing.Bitmap($paddedSize, $paddedSize)
$g = [System.Drawing.Graphics]::FromImage($squaredBmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear([System.Drawing.Color]::Transparent)

# Draw original image shifted
$destX = [int][math]::Round(($paddedSize - $contentW) / 2)
$destY = [int][math]::Round(($paddedSize - $contentH) / 2)
$destRect = New-Object System.Drawing.Rectangle($destX, $destY, $contentW, $contentH)
$srcRect = New-Object System.Drawing.Rectangle($minX, $minY, $contentW, $contentH)
$g.DrawImage($bmp, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()

function Resize-Image($img, $size, $outPath) {
    $dir = Split-Path $outPath
    if (-Not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir | Out-Null
    }
    
    $outBmp = New-Object System.Drawing.Bitmap([int]$size, [int]$size)
    $g = [System.Drawing.Graphics]::FromImage($outBmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($img, 0, 0, [int]$size, [int]$size)
    $g.Dispose()
    
    $outBmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $outBmp.Dispose()
}

function Get-ResizedBytes($img, $size) {
    $outBmp = New-Object System.Drawing.Bitmap([int]$size, [int]$size)
    $g = [System.Drawing.Graphics]::FromImage($outBmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($img, 0, 0, [int]$size, [int]$size)
    $g.Dispose()
    
    $ms = New-Object System.IO.MemoryStream
    $outBmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $outBmp.Dispose()
    $ms.Dispose()
    return ,$bytes
}

# Android mipmaps
$androidResBase = Join-Path $PWD "BlueType.Android/app/src/main/res"

$sizes = @{
    "mipmap-mdpi" = 48
    "mipmap-hdpi" = 72
    "mipmap-xhdpi" = 96
    "mipmap-xxhdpi" = 144
    "mipmap-xxxhdpi" = 192
}

foreach ($key in $sizes.Keys) {
    $outPath = Join-Path $androidResBase "$key/ic_launcher.png"
    Resize-Image $squaredBmp $sizes[$key] $outPath
    Write-Host "Created Android $key ($($sizes[$key])px)"
}

# Adaptive Icons for Android (v26)
$v26Dir = Join-Path $androidResBase "mipmap-anydpi-v26"
if (-Not (Test-Path $v26Dir)) { New-Item -ItemType Directory -Path $v26Dir | Out-Null }
$adaptiveXml = @"
<?xml version="1.0" encoding="utf-8"?>
<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
    <background android:drawable="@color/ic_launcher_background"/>
    <foreground android:drawable="@mipmap/ic_launcher_foreground"/>
</adaptive-icon>
"@
$adaptiveXml | Out-File (Join-Path $v26Dir "ic_launcher.xml") -Encoding UTF8
$adaptiveXml | Out-File (Join-Path $v26Dir "ic_launcher_round.xml") -Encoding UTF8

# Provide ic_launcher_foreground (using the padded icon)
foreach ($key in $sizes.Keys) {
    $outPath = Join-Path $androidResBase "$key/ic_launcher_foreground.png"
    # Adaptive icon foregrounds need to be 108dp. Normal launcher is 48dp.
    $adaptiveFgSize = [int][math]::Round($sizes[$key] * 108 / 48)
    $fgBmp = New-Object System.Drawing.Bitmap($adaptiveFgSize, $adaptiveFgSize)
    $gFg = [System.Drawing.Graphics]::FromImage($fgBmp)
    $gFg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    
    # Safe zone is 72dp. We will draw our squared graphic to occupy the safe zone.
    $drawSize = [int][math]::Round($sizes[$key] * 72 / 48)
    $offset = [int][math]::Round(($adaptiveFgSize - $drawSize) / 2)
    $gFg.DrawImage($squaredBmp, $offset, $offset, $drawSize, $drawSize)
    $gFg.Dispose()
    $fgBmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $fgBmp.Dispose()
}

$valuesDir = Join-Path $androidResBase "values"
if (-Not (Test-Path $valuesDir)) { New-Item -ItemType Directory -Path $valuesDir | Out-Null }
$colorsXmlPath = Join-Path $valuesDir "ic_launcher_colors.xml"
$colorsXml = @"
<?xml version="1.0" encoding="utf-8"?>
<resources>
    <color name="ic_launcher_background">#1B1B1F</color>
</resources>
"@
$colorsXml | Out-File $colorsXmlPath -Encoding UTF8
Write-Host "Created Android Adaptive Icons"


# Windows ICO (Multi-resolution: 16, 32, 48, 64, 256)
$icoSizes = @(16, 32, 48, 64, 256)
$icoImages = @()
foreach ($s in $icoSizes) {
    $pngBytes = Get-ResizedBytes $squaredBmp $s
    $icoImages += @{ Size = $s; Bytes = $pngBytes }
}

$icoPath = Join-Path $PWD "BlueType.Agent/icon.ico"
$icoStream = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($icoStream)

# Header
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$icoSizes.Length)

# Directory
$offset = 6 + (16 * $icoSizes.Length)
foreach ($imgData in $icoImages) {
    $w = $imgData.Size; if ($w -eq 256) { $w = 0 }
    $h = $imgData.Size; if ($h -eq 256) { $h = 0 }
    $bw.Write([byte]$w)
    $bw.Write([byte]$h)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$imgData.Bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $imgData.Bytes.Length
}

# Image Data
foreach ($imgData in $icoImages) {
    $bw.Write($imgData.Bytes)
}

$bw.Close()
$icoStream.Close()
Write-Host "Created Windows BlueType.Agent/icon.ico (multi-res)"

$squaredBmp.Dispose()
$bmp.Dispose()
$img.Dispose()
