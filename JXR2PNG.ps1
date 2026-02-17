# JXR → PNG，WIC 解码+编码，线性→sRGB gamma 校正
# 用法: .\JXR2PNG.ps1 <file.jxr> [file2.jxr ...]

Param([Parameter(Mandatory=$false, ValueFromPipeline=$true)][string[]]$Files)

[Console]::OutputEncoding = [System.Text.Encoding]::GetEncoding(936)
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null
Add-Type -AssemblyName System.Drawing | Out-Null

# 线性→sRGB gamma 校正：输入为线性光(0-1)，输出 sRGB 编码
$lin2sLut = [byte[]]::new(256)
for ($i = 0; $i -lt 256; $i++) {
    $lin = $i / 255.0
    $s = if ($lin -le 0.0031308) { $lin*12.92 } else { 1.055*[Math]::Pow($lin, 1/2.4)-0.055 }
    $lin2sLut[$i] = [byte][Math]::Min(255, [Math]::Max(0, [int]($s*255+0.5)))
}

$gammaCs = @'
public static class Gamma {
    static byte[] lut;
    static Gamma() {
        lut = new byte[256];
        for (int i = 0; i < 256; i++) {
            double lin = i / 255.0;
            double s = lin <= 0.0031308 ? lin*12.92 : 1.055*System.Math.Pow(lin, 1.0/2.4) - 0.055;
            lut[i] = (byte)System.Math.Max(0, System.Math.Min(255, (int)(s*255+0.5)));
        }
    }
    public static void LinearToSrgb(byte[] rgb, int bpp) {
        for (int i = 0; i < rgb.Length; i += bpp)
            for (int c = 0; c < 3 && c < bpp; c++) rgb[i+c] = lut[rgb[i+c]];
    }
}
'@
try { Add-Type -TypeDefinition $gammaCs -Language CSharp } catch {}

$runtime = [System.WindowsRuntimeSystemExtensions].GetMethods()
$asTaskT = ($runtime | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
$asTaskV = ($runtime | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' })[0]
function Await-Result($task, $type) { $t = $asTaskT.MakeGenericMethod($type).Invoke($null, @($task)); $t.Wait() | Out-Null; $t.Result }
function Await-Void($task) { $asTaskV.Invoke($null, @($task)).Wait() | Out-Null }

[Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics, ContentType=WindowsRuntime] | Out-Null

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

        # 线性→sRGB gamma 校正（WIC 可能输出线性）
        $pngPath = $outputFile.Path
        $img = [System.Drawing.Bitmap]::FromFile($pngPath)
        $bmpData = $img.LockBits([System.Drawing.Rectangle]::new(0, 0, $img.Width, $img.Height), [System.Drawing.Imaging.ImageLockMode]::ReadWrite, $img.PixelFormat)
        $ptr = $bmpData.Scan0
        $bytes = [Math]::Abs($bmpData.Stride) * $img.Height
        $rgb = [byte[]]::new($bytes)
        [System.Runtime.InteropServices.Marshal]::Copy($ptr, $rgb, 0, $bytes)
        $bpp = [System.Drawing.Image]::GetPixelFormatSize($img.PixelFormat) / 8
        try { [Gamma]::LinearToSrgb($rgb, $bpp) } catch {
            for ($i = 0; $i -lt $rgb.Length; $i += $bpp) { for ($c = 0; $c -lt [Math]::Min(3, $bpp); $c++) { $rgb[$i+$c] = $lin2sLut[$rgb[$i+$c]] } }
        }
        [System.Runtime.InteropServices.Marshal]::Copy($rgb, 0, $ptr, $bytes)
        $img.UnlockBits($bmpData)
        $img.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $img.Dispose()

        Write-Output $pngPath
    }
    catch { Write-Error "转换失败: $_" }
}
