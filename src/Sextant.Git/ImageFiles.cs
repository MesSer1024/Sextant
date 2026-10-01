using System.Globalization;

namespace Sextant.Git;

public static class PreviewLimit
{
    public static bool Allows(long size) => size >= 0 && size <= HistoryLimits.MaxPreviewBytes;
}

public static class ImageFiles
{
    public static bool IsImagePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        return Path.GetExtension(path).ToLowerInvariant() is
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico" or ".svg" or ".tif" or ".tiff";
    }

    public static string FormatBytes(long size)
    {
        if (size < 1024)
            return size.ToString(CultureInfo.InvariantCulture) + " B";
        if (size < 1024 * 1024)
            return (size / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        return (size / 1024d / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
    }
}
