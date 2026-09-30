using System.Diagnostics;
using System.Text;

namespace Sextant.Git.Tests;

public sealed class TempRepo : IDisposable
{
    private readonly string _git;

    public TempRepo()
    {
        _git = GitLocator.FindOnPath() ?? throw new InvalidOperationException("git was not found on PATH.");
        Directory = Path.Combine(Path.GetTempPath(), "sextant-test-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        Run("init");
        Run("config", "user.email", "test@example.com");
        Run("config", "user.name", "Test");
        Run("config", "commit.gpgsign", "false");
        Run("config", "tag.gpgSign", "false");
        var hooks = Path.Combine(Directory, ".empty-hooks");
        System.IO.Directory.CreateDirectory(hooks);
        Run("config", "core.hooksPath", hooks);
    }

    public string Directory { get; }

    public string Git => _git;

    public void WriteFile(string relative, string contents)
    {
        var path = Path.Combine(Directory, relative.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
            System.IO.Directory.CreateDirectory(parent);
        File.WriteAllText(path, contents.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    public void CommitAll(string message)
    {
        Run("add", "-A");
        Run("commit", "-m", message);
    }

    public string CurrentBranch() => RunCapture("branch", "--show-current").Trim();

    public void Run(params string[] args)
    {
        var output = Capture(args);
        if (output.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} exited {output.ExitCode}{Environment.NewLine}{output.Error}");
    }

    public string RunCapture(params string[] args)
    {
        var output = Capture(args);
        if (output.ExitCode != 0)
            throw new InvalidOperationException(output.Error);
        return output.Text;
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(40);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(40);
            }
        }
    }

    private (int ExitCode, string Text, string Error) Capture(string[] args)
    {
        var info = new ProcessStartInfo
        {
            FileName = _git,
            WorkingDirectory = Directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(Directory);
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}
