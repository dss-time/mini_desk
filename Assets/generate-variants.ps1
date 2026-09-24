Add-Type -AssemblyName System.Drawing

$sourcePath = Join-Path $PSScriptRoot 'MiniDesk-256.png'
$source = [System.Drawing.Bitmap]::FromFile($sourcePath)

function New-Variant([string]$name, [scriptblock]$transform) {
    $bitmap = New-Object System.Drawing.Bitmap 256, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y = 0; $y -lt 256; $y++) {
        for ($x = 0; $x -lt 256; $x++) {
            $pixel = $source.GetPixel($x, $y)
            if ($pixel.A -eq 0) { continue }
            $rgb = & $transform $pixel.R $pixel.G $pixel.B
            $bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($pixel.A, $rgb[0], $rgb[1], $rgb[2]))
        }
    }
    $pngPath = Join-Path $PSScriptRoot "MiniDesk-$name.png"
    $bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()

    $bytes = [System.IO.File]::ReadAllBytes($pngPath)
    $stream = [System.IO.File]::Create((Join-Path $PSScriptRoot "MiniDesk-$name.ico"))
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]1)
    $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$bytes.Length); $writer.Write([UInt32]22)
    $writer.Write($bytes); $writer.Dispose(); $stream.Dispose()
}

New-Variant 'Light' { param($r,$g,$b) @([Math]::Min(255,[int]($r*0.72+70)), [Math]::Min(255,[int]($g*0.72+70)), [Math]::Min(255,[int]($b*0.72+70))) }
New-Variant 'Dark' { param($r,$g,$b) @([int]($r*0.32+24), [int]($g*0.34+28), [int]($b*0.38+34)) }
New-Variant 'Mono' { param($r,$g,$b) $v=[Math]::Min(255,[int](0.21*$r+0.72*$g+0.07*$b+35)); @($v,$v,$v) }
$source.Dispose()
