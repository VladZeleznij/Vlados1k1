# Build an original multi-resolution MARCH ROLE PLAY icon for the real Windows EXE.
# PNG-compressed frames remain crisp in Explorer at 16, 32, 48, 64, 128 and 256 px.
Add-Type -AssemblyName System.Drawing
$base=[System.Drawing.Bitmap]::new(256,256,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g=[System.Drawing.Graphics]::FromImage($base)
$g.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint=[System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.Clear([System.Drawing.Color]::Transparent)
$card=[System.Drawing.Drawing2D.GraphicsPath]::new()
$card.AddArc(14,14,228,228,34,180,90)
$card.AddArc(14,14,228,228,34,270,90)
$card.AddArc(14,14,228,228,34,0,90)
$card.AddArc(14,14,228,228,34,90,90)
$card.CloseFigure()
$background=[System.Drawing.Drawing2D.LinearGradientBrush]::new(([System.Drawing.Rectangle]::new(0,0,256,256)),[System.Drawing.Color]::FromArgb(9,34,46),[System.Drawing.Color]::FromArgb(3,12,22),90)
$g.FillPath($background,$card)
$background.Dispose()
foreach($width in @(18,12,7)){
 $a= if($width -eq 18){19}elseif($width -eq 12){32}else{105}
 $pen=[System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb($a,0,238,231),$width)
 $g.DrawPath($pen,$card)
 $pen.Dispose()
}
$edge=[System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255,0,225,215),3)
$g.DrawPath($edge,$card)
$edge.Dispose()
$shine=[System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(90,138,255,250),3)
$g.DrawLine($shine,43,37,213,37)
$shine.Dispose()
# Stylized geometric M as a vector polygon, not a Windows font glyph.
$points=[System.Drawing.Point[]]@(
 [System.Drawing.Point]::new(43,170),
 [System.Drawing.Point]::new(57,76),
 [System.Drawing.Point]::new(90,76),
 [System.Drawing.Point]::new(128,125),
 [System.Drawing.Point]::new(166,76),
 [System.Drawing.Point]::new(199,76),
 [System.Drawing.Point]::new(213,170),
 [System.Drawing.Point]::new(175,170),
 [System.Drawing.Point]::new(168,121),
 [System.Drawing.Point]::new(138,158),
 [System.Drawing.Point]::new(117,158),
 [System.Drawing.Point]::new(88,121),
 [System.Drawing.Point]::new(81,170)
)
$letter=[System.Drawing.Drawing2D.GraphicsPath]::new()
$letter.AddPolygon($points)
$cyan=[System.Drawing.Drawing2D.LinearGradientBrush]::new(([System.Drawing.Rectangle]::new(43,76,170,95)),[System.Drawing.Color]::FromArgb(48,255,247),[System.Drawing.Color]::FromArgb(0,192,194),90)
$g.FillPath($cyan,$letter)
$cyan.Dispose()
$outline=[System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(110,255,255),2)
$g.DrawPath($outline,$letter)
$outline.Dispose()
$letter.Dispose()
$bar=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(0,220,216))
$g.FillRectangle($bar,62,192,132,3)
$bar.Dispose()
$font=[System.Drawing.Font]::new('Segoe UI',19,[System.Drawing.FontStyle]::Bold,[System.Drawing.GraphicsUnit]::Pixel)
$txt=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(215,242,247))
$format=[System.Drawing.StringFormat]::new()
$format.Alignment=[System.Drawing.StringAlignment]::Center
$g.DrawString('ROLE PLAY',$font,$txt,([System.Drawing.RectangleF]::new(24,201,208,30)),$format)
$format.Dispose();$txt.Dispose();$font.Dispose()
$g.Dispose();$card.Dispose()

$sizes=@(16,24,32,48,64,128,256)
$frames=[System.Collections.Generic.List[byte[]]]::new()
foreach($size in $sizes){
 $b=[System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
 $draw=[System.Drawing.Graphics]::FromImage($b)
 $draw.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
 $draw.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::HighQuality
 $draw.DrawImage($base,0,0,$size,$size)
 $draw.Dispose()
 $ms=[System.IO.MemoryStream]::new()
 $b.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png)
 $frames.Add($ms.ToArray())
 $ms.Dispose();$b.Dispose()
}
$base.Dispose()
$target=Join-Path $PSScriptRoot 'march.ico'
$writer=[System.IO.BinaryWriter]::new([System.IO.File]::Create($target))
try{
 $writer.Write([UInt16]0)
 $writer.Write([UInt16]1)
 $writer.Write([UInt16]$sizes.Count)
 $offset=6+16*$sizes.Count
 for($i=0;$i -lt $sizes.Count;$i++){
  $size=$sizes[$i]
  $dim=if($size -eq 256){0}else{$size}
  $writer.Write([byte]$dim)
  $writer.Write([byte]$dim)
  $writer.Write([byte]0)
  $writer.Write([byte]0)
  $writer.Write([UInt16]1)
  $writer.Write([UInt16]32)
  $writer.Write([UInt32]$frames[$i].Length)
  $writer.Write([UInt32]$offset)
  $offset+=$frames[$i].Length
 }
 foreach($frame in $frames){$writer.Write([byte[]]$frame)}
} finally { $writer.Dispose() }
Write-Host "Created brand-new 7-size app icon at $target"
