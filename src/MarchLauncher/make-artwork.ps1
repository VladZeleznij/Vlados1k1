# Download photographic artwork at build time, with local fallback.
# The generated fallback is always available even when the image host is unreachable.
Add-Type -AssemblyName System.Drawing
$root=$PSScriptRoot
$background=Join-Path $root 'city-bg.png'
$assets=@(
 @{name='hero.jpg'; url='https://images.unsplash.com/photo-1519501025264-65ba15a82390?w=1500&q=85'; x=0; y=0; w=1400; h=800},
 @{name='news1.jpg'; url='https://images.unsplash.com/photo-1519501025264-65ba15a82390?w=500&q=80'; x=100; y=30; w=500; h=330},
 @{name='news2.jpg'; url='https://images.unsplash.com/photo-1503376780353-7e6692767b70?w=500&q=80'; x=600; y=340; w=550; h=300},
 @{name='news3.jpg'; url='https://images.unsplash.com/photo-1519608487953-e999c86e7455?w=500&q=80'; x=50; y=60; w=500; h=300},
 @{name='news4.jpg'; url='https://images.unsplash.com/photo-1518837695005-2083093ee35b?w=500&q=80'; x=250; y=100; w=500; h=300}
)
foreach($asset in $assets) {
 $target=Join-Path $root $asset.name
 try {
  $client=[System.Net.Http.HttpClient]::new()
  $client.Timeout=[TimeSpan]::FromSeconds(18)
  $response=$client.GetAsync($asset.url).GetAwaiter().GetResult()
  $response.EnsureSuccessStatusCode()
  $bytes=$response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
  $memory=[System.IO.MemoryStream]::new($bytes)
  $source=[System.Drawing.Image]::FromStream($memory)
  $bitmap=[System.Drawing.Bitmap]::new($source)
  $bitmap.Save($target,[System.Drawing.Imaging.ImageFormat]::Jpeg)
  $bitmap.Dispose();$source.Dispose();$memory.Dispose();$client.Dispose()
  Write-Host "Downloaded $($asset.name)"
 } catch {
  Write-Warning "Artwork $($asset.name) unavailable: $($_.Exception.Message). Using built-in fallback."
  $fallback=[System.Drawing.Bitmap]::new($background)
  $crop=[System.Drawing.Rectangle]::new($asset.x,$asset.y,$asset.w,$asset.h)
  $out=[System.Drawing.Bitmap]::new($asset.w,$asset.h)
  $g=[System.Drawing.Graphics]::FromImage($out)
  $g.DrawImage($fallback,([System.Drawing.Rectangle]::new(0,0,$asset.w,$asset.h)),$crop,[System.Drawing.GraphicsUnit]::Pixel)
  $g.Dispose()
  $out.Save($target,[System.Drawing.Imaging.ImageFormat]::Jpeg)
  $out.Dispose();$fallback.Dispose()
 }
}

# Blend the city and a real automotive photograph into a cinematic hero image.
# This produces an actual JPG resource embedded into the compiled WPF application.
try {
 $city=[System.Drawing.Bitmap]::new((Join-Path $root 'hero.jpg'))
 $car=[System.Drawing.Bitmap]::new((Join-Path $root 'news2.jpg'))
 $width=1400; $height=700
 $canvas=[System.Drawing.Bitmap]::new($width,$height)
 $gfx=[System.Drawing.Graphics]::FromImage($canvas)
 $gfx.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
 $gfx.DrawImage($city,([System.Drawing.Rectangle]::new(0,0,$width,$height)))
 $carX=550; $carY=225; $carW=850; $carH=475
 # Feather the car photo into the city with a gradual alpha mask.
 $carScaled=[System.Drawing.Bitmap]::new($car,$carW,$carH)
 for($xx=0;$xx -lt $carW;$xx++){
  $edge=[Math]::Min(1.0, [Math]::Min($xx/240.0,($carW-1-$xx)/80.0))
  for($yy=0;$yy -lt $carH;$yy++){
   $v=[Math]::Min(1.0,[Math]::Min($yy/115.0,($carH-1-$yy)/90.0))
   $p=$carScaled.GetPixel($xx,$yy)
   $a=[int](230*[Math]::Max(0,$edge)*[Math]::Max(0,$v))
   $carScaled.SetPixel($xx,$yy,[System.Drawing.Color]::FromArgb($a,$p.R,$p.G,$p.B))
  }
 }
 $gfx.DrawImage($carScaled,$carX,$carY,$carW,$carH)
 $shade=[System.Drawing.Drawing2D.LinearGradientBrush]::new(([System.Drawing.Rectangle]::new(0,0,$width,$height)),[System.Drawing.Color]::FromArgb(190,1,9,17),[System.Drawing.Color]::FromArgb(20,2,11,20),0)
 $gfx.FillRectangle($shade,0,0,$width,$height)
 $shade.Dispose();$gfx.Dispose();$carScaled.Dispose();$car.Dispose();$city.Dispose()
 $canvas.Save((Join-Path $root 'hero.jpg'),[System.Drawing.Imaging.ImageFormat]::Jpeg)
 $canvas.Dispose()
 Write-Host 'Composed city and sports-car hero image.'
} catch { Write-Warning "Hero composite skipped: $($_.Exception.Message)" }
