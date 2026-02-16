# JXR 转 PNG（Windows WIC 解码）+ 提升亮度
# 用法: .\JXR2PNG.ps1 <file.jxr> [file2.jxr ...]

Param([Parameter(Mandatory=$false, ValueFromPipeline=$true)][string[]]$Files)

$ErrorActionPreference = 'Stop'
$BrightnessFactor = 1.1  # 亮度系数，1.1 = 提升 10%

Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null
Add-Type -AssemblyName System.Drawing | Out-Null
$runtime = [System.WindowsRuntimeSystemExtensions].GetMethods()
$asTaskT = ($runtime | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
$asTaskV = ($runtime | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' })[0]
function Await-Result($task, $type) { $t = $asTaskT.MakeGenericMethod($type).Invoke($null, @($task)); $t.Wait() | Out-Null; $t.Result }
function Await-Void($task) { $asTaskV.Invoke($null, @($task)).Wait() | Out-Null }

# 引用 WIC 程序集（含 WMPhoto/JXR 解码器）
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

        # 提升亮度后覆盖保存
        $pngPath = $outputFile.Path
        $img = [System.Drawing.Bitmap]::FromFile($pngPath)
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
        $out.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $out.Dispose()
        $attr.Dispose()

        Write-Output $pngPath
    }
    catch { Write-Error "转换失败: $_" }
}
