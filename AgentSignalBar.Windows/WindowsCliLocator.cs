namespace AgentSignalBar.Windows;

internal static class WindowsCliLocator
{
    public static string FindAgentSignalCli()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var repositoryRoot = FindRepositoryRoot(baseDirectory) ?? baseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "agent-signal.exe"),
            Path.Combine(baseDirectory, "AgentSignalBar.Cli.exe"),
            Path.Combine(baseDirectory, "..", "AgentSignalBar.Cli", "agent-signal.exe"),
            Path.Combine(baseDirectory, "..", "AgentSignalBar.Cli", "AgentSignalBar.Cli.exe"),
            Path.Combine(repositoryRoot, "AgentSignalBar.Cli", "bin", "Release", "net8.0", "agent-signal.exe"),
            Path.Combine(repositoryRoot, "AgentSignalBar.Cli", "bin", "Debug", "net8.0", "agent-signal.exe")
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath) && IsWithinTrustedRoot(fullPath))
            {
                return fullPath;
            }
        }

        return Environment.ProcessPath ?? Application.ExecutablePath;
    }

    /// <summary>
    /// 仅允许从受信位置（程序自身目录、LOCALAPPDATA、Program Files、用户主目录、
    /// ProgramData/WorkBuddy/users）解析 agent-signal.exe，防止从可写目录种植恶意 exe。
    /// </summary>
    private static bool IsWithinTrustedRoot(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var allowed = new List<string> { AppContext.BaseDirectory };
            if (!string.IsNullOrWhiteSpace(localAppData)) allowed.Add(localAppData);
            if (!string.IsNullOrWhiteSpace(programFiles)) allowed.Add(programFiles);
            if (!string.IsNullOrWhiteSpace(userProfile)) allowed.Add(userProfile);
            if (!string.IsNullOrWhiteSpace(common)) allowed.Add(Path.Combine(common, "WorkBuddy", "users"));
            // FIX: AppContext.BaseDirectory 自带尾部分隔符，若再拼一个分隔符会变成
            // "root\\file.exe" vs "root\\\\" 导致 StartsWith 永远失败。改为统一去尾分隔符
            // 后用「文件所在目录」与允许根比较，既允许根目录本身也允许其子树。
            var normalizedAllowed = allowed
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => Path.TrimEndingDirectorySeparator(a))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var parentDir = Path.GetDirectoryName(full);
            if (parentDir is null)
            {
                return false;
            }
            var normParent = Path.TrimEndingDirectorySeparator(parentDir);
            foreach (var root in normalizedAllowed)
            {
                if (string.Equals(normParent, root, StringComparison.OrdinalIgnoreCase)
                    || normParent.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && (Directory.Exists(Path.Combine(directory.FullName, "AgentSignalBar.Cli"))
                    || File.Exists(Path.Combine(directory.FullName, "AgentSignalDot.sln"))))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        return null;
    }
}
