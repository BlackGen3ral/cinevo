$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot '..\Cinevo\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256"><rect width="256" height="256" rx="56" fill="#171A22"/><path d="M180 66 A82 82 0 1 0 180 190 L157 157 A42 42 0 1 1 157 99 Z" fill="#B49AF8"/><path d="M137 99 L184 128 L137 157 Z" fill="#ECEEF4"/></svg>'
[IO.File]::WriteAllText((Join-Path $assets 'cinevo.svg'), $svg)
$images = @()
foreach ($size in @(16,32,48,64,128,256)) {
 $bmp = New-Object Drawing.Bitmap($size,$size)
 $g = [Drawing.Graphics]::FromImage($bmp)
 $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
 $g.ScaleTransform(($size/256.0),($size/256.0))
 $bg = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#171A22'))
 $lavender = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#B49AF8'))
 $white = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#ECEEF4'))
 $round = New-Object Drawing.Drawing2D.GraphicsPath
 $round.AddArc(0,0,112,112,180,90); $round.AddArc(144,0,112,112,270,90); $round.AddArc(144,144,112,112,0,90); $round.AddArc(0,144,112,112,90,90); $round.CloseFigure()
 $g.FillPath($bg,$round)
 $pen = New-Object Drawing.Pen($lavender,40)
 $g.DrawArc($pen,46,46,164,164,50,260)
 $points = [Drawing.PointF[]]@((New-Object Drawing.PointF(137,99)),(New-Object Drawing.PointF(184,128)),(New-Object Drawing.PointF(137,157)))
 $g.FillPolygon($white,$points)
 $stream = New-Object IO.MemoryStream
 $bmp.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
 $images += ,@{ Size=$size; Bytes=$stream.ToArray() }
 if ($size -eq 256) { $bmp.Save((Join-Path $assets 'cinevo.png'),[Drawing.Imaging.ImageFormat]::Png) }
 $stream.Dispose(); $g.Dispose(); $bmp.Dispose(); $pen.Dispose(); $bg.Dispose(); $lavender.Dispose(); $white.Dispose(); $round.Dispose()
}
$file = [IO.File]::Create((Join-Path $assets 'cinevo.ico'))
$writer = New-Object IO.BinaryWriter($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
$offset = 6 + 16*$images.Count
foreach ($im in $images) { $dimension = if ($im.Size -eq 256) {0} else {$im.Size}; $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$im.Bytes.Length); $writer.Write([uint32]$offset); $offset += $im.Bytes.Length }
foreach ($im in $images) { $writer.Write([byte[]]$im.Bytes) }
$writer.Dispose(); $file.Dispose()
