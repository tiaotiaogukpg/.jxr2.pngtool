# JXR 转 PNG（Windows WIC 解码）
# 用法: .\JXR2PNG.ps1 <file.jxr> [file2.jxr ...]

Param([Parameter(Mandatory=$false, ValueFromPipeline=$true)][string[]]$Files)

$ErrorActionPreference = 'Stop'

# 加载 WinRT 异步扩展
Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null
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

        Write-Output $outputFile.Path
    }
    catch { Write-Error "转换失败: $_" }
}
