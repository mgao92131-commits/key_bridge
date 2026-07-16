Add-Type -AssemblyName System.Drawing
$outBmp = New-Object System.Drawing.Bitmap(16, 16)
$ms = New-Object System.IO.MemoryStream
$outBmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$bytes = $ms.ToArray()
Write-Host "Length: $($bytes.Length)"
