using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using SkiaSharp;
using Svg;
using Svg.Skia;

namespace Sextant;

/// <summary>
/// Turns SVG and TIFF into PNG for the image preview. Other formats are left as they are.
/// SVG scripts stay off, and the preview does not fetch images, entities, or files the document points at.
/// TIFF uses the first page.
/// </summary>
public static class ImageRaster
{
    private const int MaxSide = 1024;
    private const int MaxPixels = 16_000_000;
    private const float IconSide = 256f;
    private const float MaxScale = 32f;

    static ImageRaster()
    {
        // These are process-wide. The preview never turns them back on.
        SvgDocument.ResolveExternalImages = ExternalType.None;
        SvgDocument.ResolveExternalXmlEntites = ExternalType.None;
        SvgDocument.ResolveExternalElements = ExternalType.None;
        Tiff.SetErrorHandler(new QuietTiff());
    }

    public static byte[]? Prepare(string? path, byte[]? data)
    {
        if (data is null || data.Length == 0)
            return null;
        var ext = string.IsNullOrEmpty(path) ? "" : Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (ext is ".svg")
                return SvgToPng(data);
            if (ext is ".tif" or ".tiff")
                return TiffToPng(data);
        }
        catch (Exception)
        {
            return null;
        }

        return data;
    }

    private static byte[]? SvgToPng(byte[] data)
    {
        using var svg = new SKSvg();
        svg.Settings.EnableJavaScript = false;
        svg.Settings.EnableExternalJavaScript = false;
        using var input = new MemoryStream(data, writable: false);
        var picture = svg.Load(input);
        if (picture is null)
            return null;
        var cull = picture.CullRect;
        if (!(cull.Width > 0) || !(cull.Height > 0) || float.IsInfinity(cull.Width) || float.IsInfinity(cull.Height))
            return null;
        var scale = Scale(cull.Width, cull.Height);
        var width = (int)Math.Ceiling(cull.Width * scale);
        var height = (int)Math.Ceiling(cull.Height * scale);
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
            return null;
        using var output = new MemoryStream();
        if (!svg.Save(output, SKColors.Transparent, SKEncodedImageFormat.Png, 100, scale, scale))
            return null;
        return output.ToArray();
    }

    private static float Scale(float width, float height)
    {
        var longSide = Math.Max(width, height);
        if (longSide > MaxSide)
            return MaxSide / longSide;
        if (longSide < IconSide)
            return Math.Min(MaxScale, IconSide / longSide);
        return 1f;
    }

    private static byte[]? TiffToPng(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var tif = Tiff.ClientOpen("preview", "r", stream, StreamTiff.Instance);
        if (tif is null)
            return null;
        var width = FieldInt(tif, TiffTag.IMAGEWIDTH);
        var height = FieldInt(tif, TiffTag.IMAGELENGTH);
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || (long)width * height > MaxPixels)
            return null;
        var raster = new int[width * height];
        if (!tif.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT, false))
            return null;

        var pixels = new byte[raster.Length * 4];
        for (var i = 0; i < raster.Length; i++)
        {
            var pixel = raster[i];
            var offset = i * 4;
            pixels[offset] = (byte)(pixel & 0xff);
            pixels[offset + 1] = (byte)((pixel >> 8) & 0xff);
            pixels[offset + 2] = (byte)((pixel >> 16) & 0xff);
            pixels[offset + 3] = (byte)((pixel >> 24) & 0xff);
        }

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var ptr = bitmap.GetPixels();
        if (ptr == IntPtr.Zero)
            return null;
        Marshal.Copy(pixels, 0, ptr, pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray();
    }

    private static int FieldInt(Tiff tif, TiffTag tag)
    {
        var field = tif.GetField(tag);
        if (field is null || field.Length == 0)
            return 0;
        return field[0].ToInt();
    }

    private sealed class QuietTiff : TiffErrorHandler
    {
        public override void ErrorHandler(Tiff tif, string method, string format, params object[] args)
        {
        }

        public override void ErrorHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
        {
        }

        public override void WarningHandler(Tiff tif, string method, string format, params object[] args)
        {
        }

        public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
        {
        }
    }

    private sealed class StreamTiff : TiffStream
    {
        public static readonly StreamTiff Instance = new();

        public override int Read(object clientData, byte[] buffer, int offset, int count) =>
            ((Stream)clientData).Read(buffer, offset, count);

        public override void Write(object clientData, byte[] buffer, int offset, int count) =>
            ((Stream)clientData).Write(buffer, offset, count);

        public override long Seek(object clientData, long offset, SeekOrigin origin) =>
            ((Stream)clientData).Seek(offset, origin);

        public override void Close(object clientData)
        {
        }

        public override long Size(object clientData) => ((Stream)clientData).Length;
    }
}
