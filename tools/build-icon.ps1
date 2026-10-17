$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Drawing
$source=[Drawing.Bitmap]::new((Join-Path $workspace 'assets\mascot.png'))
$frames=[Collections.Generic.List[byte[]]]::new()
$sizes=@(16,32,48,64,128,256)
try {
 foreach($size in $sizes) {
  $bitmap=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $graphics=[Drawing.Graphics]::FromImage($bitmap)
  $stream=[IO.MemoryStream]::new()
  try {
   $graphics.Clear([Drawing.Color]::Transparent)
   $graphics.CompositingMode=[Drawing.Drawing2D.CompositingMode]::SourceCopy
   $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
   $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
   $graphics.DrawImage($source,[Drawing.Rectangle]::new(0,0,$size,$size))
   $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
   $frames.Add($stream.ToArray())
  } finally {$stream.Dispose();$graphics.Dispose();$bitmap.Dispose()}
 }
} finally {$source.Dispose()}
$output=[IO.BinaryWriter]::new([IO.File]::Create((Join-Path $workspace 'assets\app.ico')))
try {
 $output.Write([uint16]0);$output.Write([uint16]1);$output.Write([uint16]$sizes.Count)
 $offset=6+16*$sizes.Count
 for($i=0;$i -lt $sizes.Count;$i++) {
  $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
  $output.Write([byte]$dimension);$output.Write([byte]$dimension);$output.Write([byte]0);$output.Write([byte]0)
  $output.Write([uint16]1);$output.Write([uint16]32)
  $output.Write([uint32]$frames[$i].Length);$output.Write([uint32]$offset)
  $offset+=$frames[$i].Length
 }
 foreach($frame in $frames){$output.Write($frame)}
} finally {$output.Dispose()}
Write-Output 'Built assets/app.ico: transparent 16/32/48/64/128/256 pixel frames from mascot.png'
