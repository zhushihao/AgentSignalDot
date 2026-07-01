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
            Path.Combine(repositoryRoot, "windows", "AgentSignalBar.Cli", "bin", "Release", "net8.0", "agent-signal.exe"),
            Path.Combine(repositoryRoot, "windows", "AgentSignalBar.Cli", "bin", "Debug", "net8.0", "agent-signal.exe")
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return Environment.ProcessPath ?? Application.ExecutablePath;
    }

    private static string? FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "windows")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        return null;
    }
}
