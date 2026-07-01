namespace AgentSignalBar.Core;

public static class StatePath
{
    public static string DefaultStateFilePath(IReadOnlyDictionary<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString());

        if (NonEmpty(environment, "AGENT_SIGNAL_LIGHT_STATE_FILE") is { } explicitFile)
        {
            return ExpandHome(explicitFile);
        }

        var directory = NonEmpty(environment, "AGENT_SIGNAL_LIGHT_STATE_DIR")
            ?? NonEmpty(environment, "SIGNAL_LIGHT_STATE_DIR")
            ?? WindowsDefaultStateDirectory(environment);

        return Path.Combine(ExpandHome(directory), "status.json");
    }

    private static string WindowsDefaultStateDirectory(IReadOnlyDictionary<string, string?> environment)
    {
        var localAppData = NonEmpty(environment, "LOCALAPPDATA")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, "AgentSignalBar");
    }

    private static string? NonEmpty(IReadOnlyDictionary<string, string?> environment, string key)
    {
        return environment.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static string ExpandHome(string path)
    {
        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || path.StartsWith("~" + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[2..]);
        }

        return path;
    }
}
