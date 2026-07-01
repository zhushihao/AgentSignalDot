namespace AgentSignalBar.Windows;

internal static class WindowsUserPaths
{
    public static string HomeDirectory()
    {
        return Environment.GetEnvironmentVariable("USERPROFILE")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
