// JXR → PNG，纯 WIC，线性→sRGB 色彩管理，无托管像素处理
// 双击运行：扫描程序所在目录 *.jxr，逐个转换为同名 .png，成功后删除原 jxr

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace JXR2PNG;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        string scanDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        Console.WriteLine("扫描目录: " + scanDir);

        int hr = Ole32.CoInitializeEx(IntPtr.Zero, Ole32.COINIT_APARTMENTTHREADED);
        if (hr != 0 && hr != 1) // STA 失败则尝试 MTA (如 RPC_E_CHANGED_MODE)
        {
            hr = Ole32.CoInitializeEx(IntPtr.Zero, Ole32.COINIT_MULTITHREADED);
        }
        if (hr != 0 && hr != 1)
        {
            Console.WriteLine("错误：COM 初始化失败 (0x" + hr.ToString("X8") + ")");
            Console.WriteLine("按 Enter 键退出...");
            Console.ReadLine();
            return 1;
        }
        try
        {
            string[] jxrFiles = Directory.GetFiles(scanDir, "*.jxr")
                .Concat(Directory.GetFiles(scanDir, "*.JXR"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (jxrFiles.Length == 0)
            {
                Console.WriteLine("未找到 .jxr 文件");
                Console.WriteLine("按 Enter 键退出...");
                Console.ReadLine();
                return 0;
            }

            Console.WriteLine($"找到 {jxrFiles.Length} 个 .jxr 文件");
            Console.WriteLine();

            int successCount = 0;
            int failCount = 0;

            for (int i = 0; i < jxrFiles.Length; i++)
            {
                string jxr = jxrFiles[i];
                string png = Path.ChangeExtension(jxr, ".png");
                string name = Path.GetFileName(jxr);

                try
                {
                    bool ok = ConvertJxrToPng(jxr, png);
                    if (ok && File.Exists(png))
                    {
                        File.Delete(jxr);
                        Console.WriteLine($"[{i + 1}/{jxrFiles.Length}] {name} -> PNG 完成，已删除 .jxr");
                        successCount++;
                    }
                    else
                    {
                        Console.WriteLine($"[{i + 1}/{jxrFiles.Length}] {name} 转换失败");
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{i + 1}/{jxrFiles.Length}] {name} 转换失败: {ex.Message}");
                    failCount++;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"成功: {successCount}  失败: {failCount}");
            Console.WriteLine("按 Enter 键退出...");
            Console.ReadLine();
            return failCount == 0 ? 0 : 1;
        }
        finally
        {
            Ole32.CoUninitialize();
        }
    }

    static bool ConvertJxrToPng(string inputPath, string outputPath)
    {
        IWICImagingFactory? factory = null;
        IWICBitmapDecoder? decoder = null;
        IWICBitmapFrameDecode? frame = null;
        IWICBitmapEncoder? encoder = null;
        IWICBitmapFrameEncode? frameEncode = null;
        IStream? outputStream = null;
        object? bitmapSource = null;

        try
        {
            var clsid = WicGuids.CLSID_WICImagingFactory;
            var iid = WicGuids.IID_IWICImagingFactory;
            int hr = Ole32.CoCreateInstance(
                ref clsid,
                IntPtr.Zero,
                1, // CLSCTX_INPROC_SERVER
                ref iid,
                out object? factoryObj);
            if (hr != 0 || factoryObj == null)
                return false;

            factory = (IWICImagingFactory)factoryObj;

            hr = factory.CreateDecoderFromFilename(
                inputPath,
                IntPtr.Zero,
                0x80000000, // GENERIC_READ
                1, // WICDecodeMetadataCacheOnDemand
                out decoder);
            if (hr != 0 || decoder == null)
                return false;

            hr = decoder.GetFrame(0, out frame);
            if (hr != 0 || frame == null)
                return false;

            bitmapSource = (object?)TryCreateColorTransform(factory, frame)
                ?? CreateFormatConverterSource(factory, frame);

            if (bitmapSource == null)
                return false;

            outputStream = Shlwapi.SHCreateStreamOnFileEx(outputPath, 0x1011, 0, true, IntPtr.Zero); // STGM_CREATE|STGM_WRITE|STGM_SHARE_EXCLUSIVE
            if (outputStream == null)
                return false;

            var fmtPng = WicGuids.GUID_ContainerFormatPng;
            hr = factory.CreateEncoder(ref fmtPng, IntPtr.Zero, out encoder);
            if (hr != 0 || encoder == null)
                return false;

            hr = encoder.Initialize(outputStream, 0); // WICBitmapEncoderNoCache
            if (hr != 0)
                return false;

            hr = encoder.CreateNewFrame(out frameEncode, IntPtr.Zero);
            if (hr != 0 || frameEncode == null)
                return false;

            hr = frameEncode.Initialize(IntPtr.Zero);
            if (hr != 0)
                return false;

            uint width, height;
            if (bitmapSource is IWICColorTransform ct)
                ct.GetSize(out width, out height);
            else
                ((IWICFormatConverter)bitmapSource).GetSize(out width, out height);
            hr = frameEncode.SetSize(width, height);
            if (hr != 0)
                return false;

            Guid pf = WicGuids.GUID_WICPixelFormat32bppBGRA;
            hr = frameEncode.SetPixelFormat(ref pf);
            if (hr != 0)
                return false;

            try
            {
                hr = factory.CreateColorContext(out IWICColorContext? ctxPng);
                if (ctxPng != null)
                {
                    string sRgbIcm = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "spool", "drivers", "color", "sRGB Color Space Profile.icm");
                    if (File.Exists(sRgbIcm))
                    {
                        ctxPng.InitializeFromFilename(sRgbIcm);
                        var arr = new IWICColorContext[] { ctxPng };
                        frameEncode.SetColorContexts(1, arr);
                    }
                }
            }
            catch
            {
                /* 跳过 sRGB 元数据，PNG 仍可正常生成 */
            }

            hr = frameEncode.WriteSource((IWICBitmapSource)bitmapSource, IntPtr.Zero);
            if (hr != 0)
                return false;

            hr = frameEncode.Commit();
            if (hr != 0)
                return false;

            hr = encoder.Commit();
            return hr == 0;
        }
        finally
        {
            if (frameEncode != null) Marshal.ReleaseComObject(frameEncode);
            if (encoder != null) Marshal.ReleaseComObject(encoder);
            if (outputStream != null) Marshal.ReleaseComObject(outputStream);
            if (bitmapSource != null) Marshal.ReleaseComObject(bitmapSource);
            if (frame != null) Marshal.ReleaseComObject(frame);
            if (decoder != null) Marshal.ReleaseComObject(decoder);
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
    }

    static IWICColorTransform? TryCreateColorTransform(IWICImagingFactory factory, IWICBitmapFrameDecode frame)
    {
        try
        {
            IWICColorContext? ctxSrc = null;
            uint ctxCount = 0;
            frame.GetColorContexts(0, Array.Empty<IWICColorContext>(), out ctxCount);
            if (ctxCount > 0)
            {
                var contexts = new IWICColorContext[ctxCount];
                for (int i = 0; i < ctxCount; i++)
                    factory.CreateColorContext(out contexts[i]);
                frame.GetColorContexts(ctxCount, contexts, out ctxCount);
                if (ctxCount > 0 && contexts[0] != null)
                    ctxSrc = contexts[0];
            }
            if (ctxSrc == null)
            {
                factory.CreateColorContext(out ctxSrc);
                if (ctxSrc != null)
                {
                    string sRgbIcm = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "spool", "drivers", "color", "sRGB Color Space Profile.icm");
                    if (File.Exists(sRgbIcm))
                        ctxSrc.InitializeFromFilename(sRgbIcm);
                    else
                        ctxSrc.InitializeFromExifColorSpace(1);
                }
            }
            if (ctxSrc == null) return null;

            factory.CreateColorContext(out IWICColorContext ctxDst);
            ctxDst.InitializeFromExifColorSpace(1);

            factory.CreateColorTransform(out IWICColorTransform colorTransform);
            var pfDest = WicGuids.GUID_WICPixelFormat32bppBGRA;
            if (colorTransform.Initialize((IWICBitmapSource)frame, ctxSrc, ctxDst, ref pfDest) != 0)
                return null;
            return colorTransform;
        }
        catch
        {
            return null;
        }
    }

    static IWICFormatConverter? CreateFormatConverterSource(IWICImagingFactory factory, IWICBitmapFrameDecode frame)
    {
        if (factory.CreateFormatConverter(out IWICFormatConverter converter) != 0 || converter == null)
            return null;
        var pf = WicGuids.GUID_WICPixelFormat32bppBGRA;
        if (converter.Initialize((IWICBitmapSource)frame, ref pf, 0, IntPtr.Zero, 0.0, 0) != 0)
            return null;
        return converter;
    }
}

static class Shlwapi
{
    private const int STGM_CREATE = 0x1000;
    private const int STGM_WRITE = 0x1;
    private const int STGM_SHARE_EXCLUSIVE = 0x10;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateStreamOnFileEx(
        [MarshalAs(UnmanagedType.LPWStr)] string pszFile,
        uint grfMode,
        uint dwAttributes,
        bool fCreate,
        IntPtr pstmTemplate,
        out IStream ppstm);

    internal static IStream? SHCreateStreamOnFileEx(string path, uint grfMode, uint attrs, bool fCreate, IntPtr template)
    {
        if (SHCreateStreamOnFileEx(path, grfMode, attrs, fCreate, template, out IStream s) == 0)
            return s;
        return null;
    }
}

static class Ole32
{
    internal const int COINIT_APARTMENTTHREADED = 2;
    internal const int COINIT_MULTITHREADED = 4;

    [DllImport("ole32.dll")]
    internal static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

    [DllImport("ole32.dll")]
    internal static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    internal static extern int CoCreateInstance(
        ref Guid rclsid,
        IntPtr pUnkOuter,
        int dwClsContext,
        ref Guid riid,
        out object? ppv);
}

static class WicGuids
{
    internal static readonly Guid CLSID_WICImagingFactory = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    internal static readonly Guid IID_IWICImagingFactory = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
    internal static readonly Guid GUID_WICPixelFormat32bppBGRA = new("6fddc324-4e03-4bfe-b185-3d77768dc90f");
    internal static readonly Guid GUID_ContainerFormatPng = new("1b7cfaf4-713f-473c-bbcd-6137425faeaf");
}

[ComImport]
[Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IWICImagingFactory
{
    [PreserveSig]
    int CreateDecoderFromFilename(
        [MarshalAs(UnmanagedType.LPWStr)] string wzFilename,
        IntPtr pguidVendor,
        uint dwDesiredAccess,
        int metadataOptions,
        out IWICBitmapDecoder ppIDecoder);

    [PreserveSig]
    int CreateStream(out IWICStream ppIWICStream);
    [PreserveSig]
    int CreateColorContext(out IWICColorContext ppIWICColorContext);
    [PreserveSig]
    int CreateColorTransform(out IWICColorTransform ppIWICColorTransform);
    [PreserveSig]
    int CreateEncoder(ref Guid guidContainerFormat, IntPtr pguidVendor, out IWICBitmapEncoder ppIEncoder);
    [PreserveSig]
    int CreateFormatConverter(out IWICFormatConverter ppIFormatConverter);
}

[ComImport]
[Guid("9edde9e7-8dee-47ea-99df-e6faf2ed44bf")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IWICBitmapDecoder
{
    void GetContainerFormat(out Guid pguidContainerFormat);
    void GetDecoderInfo(out IntPtr ppIDecoderInfo);
    void CopyPalette(IntPtr pIPalette);
    void GetMetadataQueryReader(out IntPtr ppIMetadataQueryReader);
    void GetPreview(out IntPtr ppIPreview);
    void GetColorContexts(uint cCount, IWICColorContext[]? ppIColorContexts, out uint pcActualCount);
    void GetThumbnail(out IntPtr ppIThumbnail);
    void GetFrameCount(out uint pCount);
    [PreserveSig]
    int GetFrame(uint index, out IWICBitmapFrameDecode ppIBitmapFrame);
}

[ComImport]
[Guid("3b16811b-6a43-4ec9-a813-3d930c13b940")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IWICBitmapFrameDecode
{
    void GetSize(out uint pWidth, out uint pHeight);
    void GetPixelFormat(out Guid pPixelFormat);
    void GetResolution(out double pDpiX, out double pDpiY);
    void CopyPalette(IntPtr pIPalette);
    void CopyPixels(IntPtr prc, uint cbStride, uint cbBufferSize, IntPtr pbBuffer);
    void GetMetadataQueryReader(out IntPtr ppIMetadataQueryReader);
    void GetColorContexts(uint cCount, IWICColorContext[]? ppIColorContexts, out uint pcActualCount);
    void GetThumbnail(out IntPtr ppIThumbnail);
}

[ComImport]
[Guid("1c9c75b7-f88b-4a5a-9feb-0a95a4e17ad2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IWICColorContext
{
    [PreserveSig]
    int InitializeFromFilename([MarshalAs(UnmanagedType.LPWStr)] string wzFilename);
    void InitializeFromMemory(IntPtr pbBuffer, uint cbBufferSize);
    [PreserveSig]
    int InitializeFromExifColorSpace(uint value);
    void GetType(out int pType);
    void GetProfileBytes(uint cbBuffer, IntPtr pbBuffer, out uint pcbActual);
}

[ComImport]
[Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IWICFormatConverter
{
    void GetSize(out uint puiWidth, out uint puiHeight);
    void GetPixelFormat(out Guid pPixelFormat);
    void GetResolution(out double pDpiX, out double pDpiY);
    void CopyPalette(IntPtr pIPalette);
    void CopyPixels(IntPtr prc, uint cbStride, uint cbBufferSize, IntPtr pbBuffer);
    [PreserveSig]
    int Initialize(IWICBitmapSource pISource, ref Guid dstFormat, int dither, IntPtr pIPalette, double alphaThresholdPercent, int paletteTranslate);
}

[ComImport]
[Guid("b66f034f-d0e2-40df-b806-ff875d3963ea")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IWICColorTransform
{
    void GetSize(out uint puiWidth, out uint puiHeight);
    void GetPixelFormat(out Guid pPixelFormat);
    void GetResolution(out double pDpiX, out double pDpiY);
    void CopyPalette(IntPtr pIPalette);
    void CopyPixels(IntPtr prc, uint cbStride, uint cbBufferSize, IntPtr pbBuffer);
    int Initialize(
        IWICBitmapSource pIBitmapSource,
        IWICColorContext pIContextSource,
        IWICColorContext pIContextDest,
        ref Guid pixelFmtDest);
}

[ComImport]
[Guid("00000120-a8f2-4877-ba0a-fd2b6645fb94")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IWICBitmapSource
{
    void GetSize(out uint puiWidth, out uint puiHeight);
    void GetPixelFormat(out Guid pPixelFormat);
    void GetResolution(out double pDpiX, out double pDpiY);
    void CopyPalette(IntPtr pIPalette);
    void CopyPixels(IntPtr prc, uint cbStride, uint cbBufferSize, IntPtr pbBuffer);
}

[ComImport]
[Guid("b66f034f-d0e2-40df-b806-ff875d3963ea")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IWICBitmapSourceTransform { }

[ComImport]
[Guid("11bd1ea6-8d70-4ea4-9ca7-cdcf8a4de75f")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IWICStream
{
    [PreserveSig]
    int InitializeFromFilename([MarshalAs(UnmanagedType.LPWStr)] string wzFilename, uint dwDesiredAccess);
    void InitializeFromIStream(IStream pIStream);
    void InitializeFromMemory(IntPtr pbBuffer, uint cbBufferSize);
    void InitializeFromIStreamRegion(IStream pIStream, ulong ulOffset, ulong ulMaxSize);
    void CopyTo(IStream pIStream, ulong cbBufferSize, out ulong pcbRead, out ulong pcbWritten);
    void Seek(ulong dlibMove, int dwOrigin, out ulong plibNewPosition);
    void SetSize(ulong libNewSize);
    void CopyTo(IStream pIStream, out ulong pcbRead, out ulong pcbWritten);
}

[ComImport]
[Guid("9fbbc7ec-8c68-4cce-9e97-c9b5b3e0fc96")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IWICBitmapEncoder
{
    [PreserveSig]
    int Initialize(IStream pIStream, int options);
    void GetContainerFormat(out Guid pguidContainerFormat);
    void GetEncoderInfo(out IntPtr ppIEncoderInfo);
    void SetColorContexts(uint cCount, IntPtr ppIColorContexts);
    void SetPalette(IntPtr pIPalette);
    void SetThumbnail(IntPtr pIThumbnail);
    void SetPreview(IntPtr pIPreview);
    [PreserveSig]
    int CreateNewFrame(out IWICBitmapFrameEncode ppIFrameEncode, IntPtr ppIEncoderOptions);
    [PreserveSig]
    int Commit();
    void GetMetadataQueryWriter(out IntPtr ppIMetadataQueryWriter);
}

[ComImport]
[Guid("9fbbc7ec-8c68-4cce-9e97-c9b5b3e0fc96")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IWICBitmapEncoderStream { }

[ComImport]
[Guid("24fb34ed-9c20-41f4-80b3-7f6d17f56dc4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IWICBitmapFrameEncode
{
    [PreserveSig]
    int Initialize(IntPtr pIEncoderOptions);
    [PreserveSig]
    int SetSize(uint uiWidth, uint uiHeight);
    void SetResolution(double dpiX, double dpiY);
    [PreserveSig]
    int SetPixelFormat(ref Guid pPixelFormat);
    [PreserveSig]
    int SetColorContexts(uint cCount, IWICColorContext[] ppIColorContexts);
    void SetPalette(IntPtr pIPalette);
    void SetThumbnail(IntPtr pIThumbnail);
    void WritePixels(uint lineCount, uint cbStride, uint cbBufferSize, IntPtr pbPixels);
    [PreserveSig]
    int WriteSource(IWICBitmapSource pIBitmapSource, IntPtr prc);
    [PreserveSig]
    int Commit();
    void GetMetadataQueryWriter(out IntPtr ppIMetadataQueryWriter);
}
