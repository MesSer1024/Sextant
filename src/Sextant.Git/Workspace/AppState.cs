using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sextant.Git;

public sealed class WorkspaceState
{
    public List<string> OpenTabs { get; set; } = [];

    public string? ActiveTab { get; set; }

    public double LocationsWidth { get; set; } = 220;

    public double GraphWidth { get; set; } = 520;

    public double FilesHeight { get; set; } = 180;
}

public sealed class AppSettings
{
    public string? GitExecutable { get; set; }

    public bool ReopenTabs { get; set; } = true;
}

public static class AppPaths
{
    public static string ConfigDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sextant");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sextant");

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var root = string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            : xdg;
        return Path.Combine(root, "sextant");
    }
}

public sealed class WorkspaceStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _directory;

    public WorkspaceStore(string directory) => _directory = directory;

    public WorkspaceState LoadWorkspace() => Load("workspace.json", new WorkspaceState());

    public AppSettings LoadSettings() => Load("settings.json", new AppSettings());

    public void SaveWorkspace(WorkspaceState state) => Save("workspace.json", state);

    public void SaveSettings(AppSettings settings) => Save("settings.json", settings);

    private T Load<T>(string fileName, T fallback)
    {
        var path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
            return fallback;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
        catch (IOException)
        {
            return fallback;
        }
    }

    private void Save<T>(string fileName, T value)
    {
        Directory.CreateDirectory(_directory);
        var destination = Path.Combine(_directory, fileName);
        var temporary = destination + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
        File.Move(temporary, destination, overwrite: true);
    }
}
