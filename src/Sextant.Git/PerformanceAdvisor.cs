namespace Sextant.Git;

public static class PerformanceAdvisor
{
    public static readonly TimeSpan SlowStatus = TimeSpan.FromMilliseconds(1500);

    public static PerformanceSuggestion? Evaluate(TimeSpan statusDuration, IReadOnlyDictionary<string, string> config)
    {
        if (statusDuration < SlowStatus)
            return null;

        var manyFiles = IsEnabled(config, "feature.manyfiles");
        var monitor = IsEnabled(config, "core.fsmonitor");
        if (manyFiles && monitor)
            return null;
        return new PerformanceSuggestion(!manyFiles, !monitor);
    }

    private static bool IsEnabled(IReadOnlyDictionary<string, string> config, string key)
    {
        if (!config.TryGetValue(key, out var value))
            return false;
        if (value.Length == 0 || value.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}
