param([Parameter(Mandatory = $true)][string]$SourcePath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourcePath = (Resolve-Path -LiteralPath $SourcePath -ErrorAction Stop).Path
$assetDir = $PSScriptRoot
$source = [System.Drawing.Bitmap]::FromFile($sourcePath)
$cropRect = [System.Drawing.Rectangle]::new(80, 80, 282, 282)
$icon = [System.Drawing.Bitmap]::new($cropRect.Width, $cropRect.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($icon)
$graphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, $icon.Width, $icon.Height), $cropRect, [System.Drawing.GraphicsUnit]::Pixel)
$graphics.Dispose()
$source.Dispose()

# Remove only the connected pale art-board background. White cards inside the
# blue icon are enclosed by saturated pixels and therefore remain intact.
$width = $icon.Width
$height = $icon.Height
$visited = [byte[]]::new($width * $height)
$queue = [System.Collections.Generic.Queue[int]]::new()
for ($x = 0; $x -lt $width; $x++) { $queue.Enqueue($x); $queue.Enqueue(($height - 1) * $width + $x) }
for ($y = 0; $y -lt $height; $y++) { $queue.Enqueue($y * $width); $queue.Enqueue($y * $width + $width - 1) }

while ($queue.Count -gt 0) {
    $index = $queue.Dequeue()
    if ($index -lt 0 -or $index -ge $visited.Length -or $visited[$index] -ne 0) { continue }
    $visited[$index] = 1
    $x = $index % $width
    $y = [int][Math]::Floor($index / $width)
    $pixel = $icon.GetPixel($x, $y)
    $max = [Math]::Max($pixel.R, [Math]::Max($pixel.G, $pixel.B))
    $min = [Math]::Min($pixel.R, [Math]::Min($pixel.G, $pixel.B))
    $average = ($pixel.R + $pixel.G + $pixel.B) / 3
    if (($max - $min) -gt 52 -or $average -lt 145) { continue }
    $icon.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, $pixel.R, $pixel.G, $pixel.B))
    if ($x -gt 0) { $queue.Enqueue($index - 1) }
    if ($x + 1 -lt $width) { $queue.Enqueue($index + 1) }
    if ($y -gt 0) { $queue.Enqueue($index - $width) }
    if ($y + 1 -lt $height) { $queue.Enqueue($index + $width) }
}

function New-IconPng([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $padding = [Math]::Max(1, [int]($size * 0.025))
    $g.DrawImage($icon, [System.Drawing.Rectangle]::new($padding, $padding, $size - 2 * $padding, $size - 2 * $padding))
    $g.Dispose()
    $path = Join-Path $assetDir "MiniDesk-$size.png"
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $path
}

$sizes = @(256, 128, 64, 48, 32, 16)
$pngPaths = foreach ($size in $sizes) { New-IconPng $size }
$icon.Dispose()

$pngData = @()
foreach ($pngPath in $pngPaths) { $pngData += ,([System.IO.File]::ReadAllBytes($pngPath)) }
$headerSize = 6 + 16 * $pngData.Count
$totalSize = $headerSize + (($pngData | ForEach-Object Length | Measure-Object -Sum).Sum)
$ico = [byte[]]::new($totalSize)
[BitConverter]::GetBytes([uint16]0).CopyTo($ico, 0)
[BitConverter]::GetBytes([uint16]1).CopyTo($ico, 2)
[BitConverter]::GetBytes([uint16]$pngData.Count).CopyTo($ico, 4)
$offset = $headerSize
for ($i = 0; $i -lt $pngData.Count; $i++) {
    $size = $sizes[$i]
    $entry = 6 + 16 * $i
    $ico[$entry] = if ($size -eq 256) { 0 } else { [byte]$size }
    $ico[$entry + 1] = if ($size -eq 256) { 0 } else { [byte]$size }
    $ico[$entry + 2] = 0
    $ico[$entry + 3] = 0
    [BitConverter]::GetBytes([uint16]1).CopyTo($ico, $entry + 4)
    [BitConverter]::GetBytes([uint16]32).CopyTo($ico, $entry + 6)
    [BitConverter]::GetBytes([uint32]$pngData[$i].Length).CopyTo($ico, $entry + 8)
    [BitConverter]::GetBytes([uint32]$offset).CopyTo($ico, $entry + 12)
    $pngData[$i].CopyTo($ico, $offset)
    $offset += $pngData[$i].Length
}
[System.IO.File]::WriteAllBytes((Join-Path $assetDir 'MiniDesk.ico'), $ico)
Write-Output (Join-Path $assetDir 'MiniDesk.ico')
