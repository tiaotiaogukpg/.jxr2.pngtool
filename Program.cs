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
    static int Main(string[] args)
    {
        string scanDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        Console.WriteLine("扫描目录: " + scanDir);

        int hr = Ole32.CoInitializeEx(IntPtr.Zero, Ole32.COINIT_APARTMENTTHREADED);
        if (hr != 0 && hr != 1)
        {
            Console.WriteLine("错误：COM 初始化失败");
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
        IWICColorContext? ctxSrc = null;
        IWICColorContext? ctxDst = null;
        IWICColorTransform? colorTransform = null;
        IWICBitmapEncoder? encoder = null;
        IWICBitmapFrameEncode? frameEncode = null;
        IWICStream? stream = null;

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

            uint ctxCount = 0;
            frame.GetColorContexts(0, null, out ctxCount);
            if (ctxCount > 0)
            {
                var contexts = new IWICColorContext[ctxCount];
                for (int i = 0; i < ctxCount; i++)
                    hr = factory.CreateColorContext(out contexts[i]);
                frame.GetColorContexts(ctxCount, contexts, out ctxCount);
                if (ctxCount > 0 && contexts[0] != null)
                    ctxSrc = contexts[0];
            }

            if (ctxSrc == null)
            {
                hr = factory.CreateColorContext(out ctxSrc);
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

            if (ctxSrc == null)
                return false;

            hr = factory.CreateColorContext(out ctxDst);
            if (ctxDst == null)
                return false;
            ctxDst.InitializeFromExifColorSpace(1); // sRGB

            hr = factory.CreateColorTransform(out colorTransform);
            if (colorTransform == null)
                return false;

            var pfDest = WicGuids.GUID_WICPixelFormat32bppBGRA;
            hr = colorTransform.Initialize((IWICBitmapSource)frame, ctxSrc, ctxDst, ref pfDest);
            if (hr != 0)
                return false;

            hr = factory.CreateStream(out stream);
            if (stream == null)
                return false;

            hr = stream.InitializeFromFilename(outputPath, 0x40000000); // GENERIC_WRITE
            if (hr != 0)
                return false;

            var fmtPng = WicGuids.GUID_ContainerFormatPng;
            hr = factory.CreateEncoder(ref fmtPng, IntPtr.Zero, out encoder);
            if (hr != 0 || encoder == null)
                return false;

            hr = encoder.Initialize((IStream)stream, 0); // WICBitmapEncoderNoCache, IWICStream inherits IStream
            if (hr != 0)
                return false;

            hr = encoder.CreateNewFrame(out frameEncode, IntPtr.Zero);
            if (hr != 0 || frameEncode == null)
                return false;

            hr = frameEncode.Initialize(IntPtr.Zero);
            if (hr != 0)
                return false;

            colorTransform.GetSize(out uint width, out uint height);
            hr = frameEncode.SetSize(width, height);
            if (hr != 0)
                return false;

            Guid pf = WicGuids.GUID_WICPixelFormat32bppBGRA;
            hr = frameEncode.SetPixelFormat(ref pf);
            if (hr != 0)
                return false;

            IWICColorContext? ctxPng = null;
            hr = factory.CreateColorContext(out ctxPng);
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

            hr = frameEncode.WriteSource((IWICBitmapSource)colorTransform, IntPtr.Zero);
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
            if (stream != null) Marshal.ReleaseComObject(stream);
            if (colorTransform != null) Marshal.ReleaseComObject(colorTransform);
            if (ctxDst != null) Marshal.ReleaseComObject(ctxDst);
            if (ctxSrc != null) Marshal.ReleaseComObject(ctxSrc);
            if (frame != null) Marshal.ReleaseComObject(frame);
            if (decoder != null) Marshal.ReleaseComObject(decoder);
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
    }
}

static class Ole32
{
    internal const int COINIT_APARTMENTTHREADED = 2;

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
