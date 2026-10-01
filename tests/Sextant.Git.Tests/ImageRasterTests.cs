using System.Text;
using BitMiracle.LibTiff.Classic;
using Sextant;
using Sextant.Git;
using SkiaSharp;

namespace Sextant.Git.Tests;

public class ImageRasterTests
{
    [Fact]
    public void Image_paths_include_svg_tiff_and_the_common_rasters()
    {
        Assert.True(ImageFiles.IsImagePath("a.svg"));
        Assert.True(ImageFiles.IsImagePath("a.SVG"));
        Assert.True(ImageFiles.IsImagePath("a.png"));
        Assert.True(ImageFiles.IsImagePath("a.jpg"));
        Assert.True(ImageFiles.IsImagePath("a.jpeg"));
        Assert.True(ImageFiles.IsImagePath("a.tif"));
        Assert.True(ImageFiles.IsImagePath("a.tiff"));
        Assert.False(ImageFiles.IsImagePath("a.txt"));
    }

    [Fact]
    public void Svg_preview_is_a_png_and_does_not_fetch_a_file_it_points_at()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sextant-svg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var secret = Path.Combine(dir, "secret.png");
            File.WriteAllBytes(secret, BluePng());
            var href = secret.Replace('\\', '/');
            var svg = """
                <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="8" height="8">
                  <rect width="8" height="8" fill="#ff0000"/>
                  <image href="file:///HREF" xlink:href="file:///HREF" width="8" height="8"/>
                </svg>
                """.Replace("HREF", href, StringComparison.Ordinal);
            var png = ImageRaster.Prepare("mark.svg", Encoding.UTF8.GetBytes(svg));
            var pixel = Pixel(png);
            Assert.True(pixel.Red > 200 && pixel.Green < 40 && pixel.Blue < 40, pixel.ToString());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Tiff_preview_is_a_png_of_the_first_page()
    {
        var png = ImageRaster.Prepare("scan.tiff", RedBlueTiff());
        using var bitmap = Decode(png);
        Assert.Equal(2, bitmap.Width);
        Assert.Equal(1, bitmap.Height);
        var left = bitmap.GetPixel(0, 0);
        var right = bitmap.GetPixel(1, 0);
        Assert.True(left.Red > 200 && left.Green < 40 && left.Blue < 40, left.ToString());
        Assert.True(right.Blue > 200 && right.Red < 40 && right.Green < 40, right.ToString());
    }

    [Fact]
    public void Png_bytes_are_left_unchanged()
    {
        var png = BluePng();
        Assert.Same(png, ImageRaster.Prepare("a.png", png));
        Assert.Same(png, ImageRaster.Prepare("a.jpg", png));
        Assert.Same(png, ImageRaster.Prepare("a.jpeg", png));
    }

    private static SKColor Pixel(byte[]? png)
    {
        using var bitmap = Decode(png);
        return bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
    }

    private static SKBitmap Decode(byte[]? png)
    {
        Assert.NotNull(png);
        Assert.True(png.Length > 8);
        Assert.Equal(0x89, png[0]);
        var bitmap = SKBitmap.Decode(png);
        Assert.NotNull(bitmap);
        return bitmap;
    }

    private static byte[] BluePng()
    {
        using var bitmap = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(0, 0, 255));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] RedBlueTiff()
    {
        using var stream = new MemoryStream();
        using (var tif = Tiff.ClientOpen("preview", "w", stream, new Writer()))
        {
            Assert.NotNull(tif);
            tif.SetField(TiffTag.IMAGEWIDTH, 2);
            tif.SetField(TiffTag.IMAGELENGTH, 1);
            tif.SetField(TiffTag.SAMPLESPERPIXEL, 3);
            tif.SetField(TiffTag.BITSPERSAMPLE, 8);
            tif.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
            tif.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
            tif.SetField(TiffTag.COMPRESSION, Compression.NONE);
            tif.SetField(TiffTag.ROWSPERSTRIP, 1);
            Assert.True(tif.WriteScanline(new byte[] { 255, 0, 0, 0, 0, 255 }, 0));
        }

        return stream.ToArray();
    }

    private sealed class Writer : TiffStream
    {
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
