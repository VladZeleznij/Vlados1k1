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

# Hero image blending is handled by WPF opacity masks in MainWindow.xaml.\n