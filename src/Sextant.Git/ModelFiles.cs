namespace Sextant.Git;

public static class ModelFiles
{
    public static bool IsFbxPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        return Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase);
    }
}
