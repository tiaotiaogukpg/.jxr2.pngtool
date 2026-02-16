# JXR 转 PNG（Windows WIC 解码）+ 提升亮度
# 用法: .\JXR2PNG.ps1 <file.jxr> [file2.jxr ...]

Param([Parameter(Mandatory=$false, ValueFromPipeline=$true)][string[]]$Files)

$ErrorActionPreference = 'Stop'
$BrightnessFactor = 1.1   # 高光/中间调提升约 10%
$ShadowPreserve = $true  # 暗部少提亮，避免发灰

Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null
Add-Type -AssemblyName System.Drawing | Out-Null

# 编译 C# 快速亮度处理（比 PowerShell 循环快约 10 倍）
$brightnessCs = @'
public static class BrightnessLut {
    public static void Apply(byte[] rgb, byte[] lut, int bpp) {
        int n = rgb.Length;
        if (bpp == 4) {
            for (int i = 0; i < n; i += 4) {
                rgb[i] = lut[rgb[i]];
                rgb[i+1] = lut[rgb[i+1]];
                rgb[i+2] = lut[rgb[i+2]];
            }
        } else {
            for (int i = 0; i < n; i += bpp) {
                int lim = bpp < 3 ? bpp : 3;
                for (int c = 0; c < lim; c++) rgb[i + c] = lut[rgb[i + c]];
            }
        }
    }
}
'@
try { Add-Type -TypeDefinition $brightnessCs -Language CSharp } catch {}
$runtime = [System.WindowsRuntimeSystemExtensions].GetMethods()
$asTaskT = ($runtime | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
$asTaskV = ($runtime | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' })[0]
function Await-Result($task, $type) { $t = $asTaskT.MakeGenericMethod($type).Invoke($null, @($task)); $t.Wait() | Out-Null; $t.Result }
function Await-Void($task) { $asTaskV.Invoke($null, @($task)).Wait() | Out-Null }

# 引用 WIC 程序集（含 WMPhoto/JXR 解码器）
[Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics, ContentType=WindowsRuntime] | Out-Null

# 预计算亮度 LUT，避免循环内浮点运算
$k = $BrightnessFactor - 1
$lut = [byte[]]::new(256)
for ($i = 0; $i -lt 256; $i++) {
    $v = $i / 255.0
    $lut[$i] = [Math]::Min(255, [int]($v * (1.0 + $k * $v) * 255 + 0.5))
}

foreach ($f in $Files) {
    try {
        $path = (Get-Item -LiteralPath $f.Trim('"') -ErrorAction Stop).FullName
        $inputFile = Await-Result ([Windows.Storage.StorageFile]::GetFileFromPathAsync($path)) ([Windows.Storage.StorageFile])
        $folder = Await-Result ($inputFile.GetParentAsync()) ([Windows.Storage.StorageFolder])
        $stream = Await-Result ($inputFile.OpenReadAsync()) ([Windows.Storage.Streams.IRandomAccessStreamWithContentType])
        $decoder = Await-Result ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
        $bitmap = Await-Result ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
        $stream.Dispose()

        $outName = $inputFile.Name -replace ($inputFile.FileType + '$'), '.png'
        $outputFile = Await-Result ($folder.CreateFileAsync($outName, [Windows.Storage.CreationCollisionOption]::ReplaceExisting)) ([Windows.Storage.StorageFile])
        $outputStream = Await-Result ($outputFile.OpenAsync([Windows.Storage.FileAccessMode]::ReadWrite)) ([Windows.Storage.Streams.IRandomAccessStream])
        $encoder = Await-Result ([Windows.Graphics.Imaging.BitmapEncoder]::CreateAsync([Windows.Graphics.Imaging.BitmapEncoder]::PngEncoderId, $outputStream)) ([Windows.Graphics.Imaging.BitmapEncoder])
        $encoder.SetSoftwareBitmap($bitmap)
        $encoder.IsThumbnailGenerated = $false
        Await-Void ($encoder.FlushAsync())
        $outputStream.Dispose()

        # 提升亮度（暗部少提、中间调和高光多提，避免暗部发灰）
        $pngPath = $outputFile.Path
        $img = [System.Drawing.Bitmap]::FromFile($pngPath)
        if ($ShadowPreserve) {
            $bmpData = $img.LockBits([System.Drawing.Rectangle]::new(0, 0, $img.Width, $img.Height), [System.Drawing.Imaging.ImageLockMode]::ReadWrite, $img.PixelFormat)
            $ptr = $bmpData.Scan0
            $bytes = [Math]::Abs($bmpData.Stride) * $img.Height
            $rgb = [byte[]]::new($bytes)
            [System.Runtime.InteropServices.Marshal]::Copy($ptr, $rgb, 0, $bytes)
            $bpp = [System.Drawing.Image]::GetPixelFormatSize($img.PixelFormat) / 8
            try {
                [BrightnessLut]::Apply($rgb, $lut, $bpp)
            } catch {
                $n = $rgb.Length
                if ($bpp -eq 4) {
                    for ($i = 0; $i -lt $n; $i += 4) {
                        $rgb[$i] = $lut[$rgb[$i]]; $rgb[$i+1] = $lut[$rgb[$i+1]]; $rgb[$i+2] = $lut[$rgb[$i+2]]
                    }
                } else {
                    for ($i = 0; $i -lt $n; $i += $bpp) {
                        for ($c = 0; $c -lt [Math]::Min(3, $bpp); $c++) { $rgb[$i+$c] = $lut[$rgb[$i+$c]] }
                    }
                }
            }
            [System.Runtime.InteropServices.Marshal]::Copy($rgb, 0, $ptr, $bytes)
            $img.UnlockBits($bmpData)
        } else {
            $rect = [System.Drawing.Rectangle]::new(0, 0, $img.Width, $img.Height)
            $cm = [System.Drawing.Imaging.ColorMatrix]::new()
            $cm.Matrix00 = $cm.Matrix11 = $cm.Matrix22 = $BrightnessFactor
            $cm.Matrix33 = $cm.Matrix44 = 1
            $attr = [System.Drawing.Imaging.ImageAttributes]::new()
            $attr.SetColorMatrix($cm)
            $out = [System.Drawing.Bitmap]::new($img.Width, $img.Height)
            $g = [System.Drawing.Graphics]::FromImage($out)
            $g.DrawImage($img, $rect, 0, 0, $img.Width, $img.Height, [System.Drawing.GraphicsUnit]::Pixel, $attr)
            $g.Dispose()
            $img.Dispose()
            $img = $out
            $attr.Dispose()
        }
        $img.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $img.Dispose()

        Write-Output $pngPath
    }
    catch { Write-Error "转换失败: $_" }
}
