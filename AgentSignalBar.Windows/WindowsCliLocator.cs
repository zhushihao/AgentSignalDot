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
            return allowed.Any(r => full == r || full.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
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
