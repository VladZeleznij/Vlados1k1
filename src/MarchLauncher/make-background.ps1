Add-Type -AssemblyName System.Drawing
$w=1400; $h=800
$b=New-Object System.Drawing.Bitmap($w,$h)
$g=[System.Drawing.Graphics]::FromImage($b)
$g.SmoothingMode='AntiAlias'
$rect=New-Object System.Drawing.Rectangle(0,0,$w,$h)
$bg=[System.Drawing.Drawing2D.LinearGradientBrush]::new($rect,[System.Drawing.Color]::FromArgb(10,30,45),[System.Drawing.Color]::FromArgb(150,66,72),90)
$g.FillRectangle($bg,$rect)
$random=New-Object System.Random(42)
$horizon=530
for($i=0;$i -lt 85;$i++){
 $x=$random.Next(0,$w);$bw=$random.Next(20,78);$bh=$random.Next(100,450)
 $dark=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(235,$random.Next(6,19),$random.Next(16,34),$random.Next(28,45)))
 $g.FillRectangle($dark,$x,$horizon-$bh,$bw,$bh+280)
 $dark.Dispose()
 for($yy=$horizon-$bh+12;$yy -lt $horizon-8;$yy+=18){
  for($xx=$x+7;$xx -lt $x+$bw-4;$xx+=13){
   if($random.NextDouble() -lt .36){
    $light=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb($random.Next(75,200),255,$random.Next(120,205),$random.Next(65,135)))
    $g.FillRectangle($light,$xx,$yy,4,7);$light.Dispose()
   }
  }
 }
}
$road=[System.Drawing.Drawing2D.LinearGradientBrush]::new(([System.Drawing.Rectangle]::new(0,530,$w,270)),[System.Drawing.Color]::FromArgb(8,24,34),[System.Drawing.Color]::FromArgb(1,9,17),90)
$g.FillRectangle($road,0,530,$w,270)
for($i=0;$i -lt 45;$i++){
 $x=$random.Next(0,$w);$y=$random.Next(550,800);$c=[System.Drawing.Color]::FromArgb($random.Next(20,95),$random.Next(15,55),$random.Next(130,240),$random.Next(160,230))
 $pen=[System.Drawing.Pen]::new($c,$random.Next(1,4))
 $g.DrawLine($pen,$x,$y,$x+$random.Next(20,180),$y);$pen.Dispose()
}
# Original stylized sports car, placed in the lower right of the hero artwork.
$shadow=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(105,0,0,0))
$g.FillEllipse($shadow,615,654,680,72)
$shadow.Dispose()
$body=[System.Drawing.Drawing2D.GraphicsPath]::new()
$pts=[System.Drawing.Point[]]@(
 [System.Drawing.Point]::new(595,632),[System.Drawing.Point]::new(650,600),
 [System.Drawing.Point]::new(790,579),[System.Drawing.Point]::new(870,520),
 [System.Drawing.Point]::new(1050,511),[System.Drawing.Point]::new(1152,574),
 [System.Drawing.Point]::new(1240,594),[System.Drawing.Point]::new(1290,639),
 [System.Drawing.Point]::new(1274,678),[System.Drawing.Point]::new(606,678))
$body.AddPolygon($pts)
$carPaint=[System.Drawing.Drawing2D.LinearGradientBrush]::new(([System.Drawing.Rectangle]::new(595,510,700,180)),[System.Drawing.Color]::FromArgb(26,205,200),[System.Drawing.Color]::FromArgb(3,53,70),90)
$g.FillPath($carPaint,$body)
$carOutline=[System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(110,238,234),4)
$g.DrawPath($carOutline,$body)
$carOutline.Dispose();$carPaint.Dispose();$body.Dispose()
$glass=[System.Drawing.Drawing2D.GraphicsPath]::new()
$glass.AddPolygon([System.Drawing.Point[]]@(
 [System.Drawing.Point]::new(840,575),[System.Drawing.Point]::new(891,533),
 [System.Drawing.Point]::new(1040,528),[System.Drawing.Point]::new(1117,579)))
$glassBrush=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(10,29,45))
$g.FillPath($glassBrush,$glass)
$glassPen=[System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(89,205,215),3)
$g.DrawPath($glassPen,$glass)
$glassPen.Dispose();$glassBrush.Dispose();$glass.Dispose()
foreach($wheelX in @(745,1157)){
 $tire=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(4,9,16))
 $rim=[System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(110,184,192),7)
 $g.FillEllipse($tire,$wheelX-47,636,94,94)
 $g.DrawEllipse($rim,$wheelX-30,653,60,60)
 $tire.Dispose();$rim.Dispose()
}
$headlight=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255,238,194))
$g.FillEllipse($headlight,1264,617,23,11)
$headlight.Dispose()
$g.Dispose()
$b.Save((Join-Path $PSScriptRoot 'city-bg.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$b.Dispose()
