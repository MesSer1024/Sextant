using System.Text;

namespace Sextant.Git.Parsing;

public static class ConfigParser
{
    public static Dictionary<string, string> Parse(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = entry.IndexOf('\n');
            if (split <= 0)
                continue;
            var key = entry[..split].Trim();
            if (key.Length == 0)
                continue;
            config[key] = entry[(split + 1)..];
        }

        return config;
    }

    public static Encoding LogEncoding(IReadOnlyDictionary<string, string> config)
    {
        if (!config.TryGetValue("i18n.logoutputencoding", out var name) || name.Length == 0)
            return Encoding.UTF8;
        try
        {
            return Encoding.GetEncoding(name);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}
