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
$g.Dispose()
$b.Save((Join-Path $PSScriptRoot 'city-bg.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$b.Dispose()
