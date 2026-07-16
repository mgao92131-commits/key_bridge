Add-Type -AssemblyName System.Drawing

$sourcePath = Join-Path $PWD "icon.png"
$img = [System.Drawing.Image]::FromFile($sourcePath)
$bmp = New-Object System.Drawing.Bitmap($img)

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
Write-Host "Original: $($bmp.Width)x$($bmp.Height)"
Write-Host "Bounds: minX=$minX, minY=$minY, maxX=$maxX, maxY=$maxY"
$bmp.Dispose()
$img.Dispose()
