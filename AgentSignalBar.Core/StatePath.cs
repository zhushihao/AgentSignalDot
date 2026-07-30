namespace AgentSignalBar.Core;

public static class StatePath
{
    public static string DefaultStateFilePath(IReadOnlyDictionary<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString());

        if (NonEmpty(environment, "AGENT_SIGNAL_LIGHT_STATE_FILE") is { } explicitFile
            && IsWithinTrustedStateRoot(ExpandHome(explicitFile), environment))
        {
            return ExpandHome(explicitFile);
        }

        var directory = NonEmpty(environment, "AGENT_SIGNAL_LIGHT_STATE_DIR")
            ?? NonEmpty(environment, "SIGNAL_LIGHT_STATE_DIR")
            ?? WindowsDefaultStateDirectory(environment);

        var expanded = ExpandHome(directory);
        if (!IsWithinTrustedStateRoot(expanded, environment))
        {
            // 重定向到受信根之外（如系统目录、其他用户目录）一律忽略，回退默认位置。
            expanded = WindowsDefaultStateDirectory(environment);
        }

        return Path.Combine(expanded, "status.json");
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

    /// <summary>
    /// 仅允许状态文件落在用户可写位置（LOCALAPPDATA、用户主目录、Temp、程序自身目录），
    /// 拒绝系统目录或其他用户目录，防止通过环境变量把信号灯文件重定向到受攻击者控制的路径。
    /// </summary>
    private static bool IsWithinTrustedStateRoot(string path, IReadOnlyDictionary<string, string?> environment)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var localAppData = NonEmpty(environment, "LOCALAPPDATA")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var temp = Path.GetTempPath();
            var roots = new[] { localAppData, userProfile, temp, AppContext.BaseDirectory }
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => Path.GetFullPath(r!))
                .ToArray();
            return roots.Any(r =>
                full.Equals(r, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(
                    r.EndsWith(Path.DirectorySeparatorChar) ? r : r + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
