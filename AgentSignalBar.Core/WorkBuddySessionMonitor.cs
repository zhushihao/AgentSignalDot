using System.Text.Json;

namespace AgentSignalBar.Core;

public sealed record WorkBuddySessionActivity(
    AgentSignal Signal,
    string SessionId,
    string Agent,
    string Event,
    DateTimeOffset Timestamp);

public sealed class WorkBuddySessionMonitor
{
    private const string SessionFilePattern = "*.json";
    private static readonly TimeSpan DefaultHeartbeatThreshold = TimeSpan.FromSeconds(30);
    private readonly string? sessionsDirectory;
    private readonly TimeSpan heartbeatThreshold;
    private readonly Dictionary<string, bool> wasActiveBySession = new(StringComparer.OrdinalIgnoreCase);

    public WorkBuddySessionMonitor(string? sessionsDirectory = null, TimeSpan? heartbeatThreshold = null)
    {
        this.sessionsDirectory = sessionsDirectory;
        this.heartbeatThreshold = heartbeatThreshold ?? DefaultHeartbeatThreshold;
    }

    /// <summary>
    /// Candidate session directories to scan. Resolution order:
    ///   1. explicit constructor arg, or
    ///   2. AGENT_SIGNAL_WORKBUDDY_SESSIONS_DIR env var (each a single directory),
    ///   3. user-profile .workbuddy/sessions PLUS every WorkBuddy sandboxed
    ///      data root's .workbuddy/sessions. LOCALAPPDATA redirection can push
    ///      the live session files into the ProgramData root, so we must look in
    ///      both places or the heartbeat monitor silently finds nothing and can
    ///      never backstop the log monitor.
    /// </summary>
    private IReadOnlyList<string> GetSessionsDirectories()
    {
        if (!string.IsNullOrWhiteSpace(sessionsDirectory))
        {
            return [sessionsDirectory];
        }

        var env = Environment.GetEnvironmentVariable("AGENT_SIGNAL_WORKBUDDY_SESSIONS_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return [env];
        }

        var dirs = new List<string>
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".workbuddy",
                "sessions")
        };

        foreach (var root in EnumerateWorkBuddyDataRoots())
        {
            dirs.Add(Path.Combine(root, ".workbuddy", "sessions"));
        }

        return dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> EnumerateWorkBuddyDataRoots()
    {
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var wbUsers = Path.Combine(common, "WorkBuddy", "users");
        if (Directory.Exists(wbUsers))
        {
            foreach (var userDir in Directory.GetDirectories(wbUsers))
            {
                yield return userDir;
            }
        }
    }

    public static string DefaultSessionsDirectory()
    {
        if (Environment.GetEnvironmentVariable("AGENT_SIGNAL_WORKBUDDY_SESSIONS_DIR") is { } overridePath
            && !string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".workbuddy",
            "sessions");
    }

    public IReadOnlyList<WorkBuddySessionActivity> Poll(DateTimeOffset now, IReadOnlySet<string>? knownBusySessionIds = null)
    {
        var activities = new List<WorkBuddySessionActivity>();
        var currentActive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sessionsDirectory in GetSessionsDirectories())
        {
            if (!Directory.Exists(sessionsDirectory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(sessionsDirectory, SessionFilePattern, SearchOption.TopDirectoryOnly))
            {
            var fileInfo = new FileInfo(file);
            fileInfo.Refresh();
            if (!fileInfo.Exists)
            {
                continue;
            }

            var sessionId = ReadSessionId(fileInfo) ?? Path.GetFileNameWithoutExtension(fileInfo.Name);
            var sessionKey = $"workbuddy:{sessionId}";
            var isActive = now - fileInfo.LastWriteTimeUtc < heartbeatThreshold;

            if (!isActive)
            {
                continue;
            }

            // Track background process sessions in active set (so they don't
            // trigger spurious SessionEnd signals) but never emit thinking for them.
            // These sessions (sessionId starting with "interactive-") represent
            // WorkBuddy's own app process, not real user conversations.
            var isBackgroundProcess = sessionId.StartsWith("interactive-", StringComparison.OrdinalIgnoreCase);

            currentActive.Add(sessionKey);

            if (isBackgroundProcess)
            {
                continue;
            }

            // Only emit thinking if the log monitor has confirmed this session is
            // actually busy. Sessions without log data are treated as idle.
            if (knownBusySessionIds is not null && !knownBusySessionIds.Contains(sessionId))
            {
                continue;
            }

            if (!wasActiveBySession.TryGetValue(sessionKey, out var previouslyActive) || !previouslyActive)
            {
                activities.Add(new WorkBuddySessionActivity(
                    AgentSignal.Thinking,
                    sessionKey,
                    "workbuddy",
                    "SessionHeartbeat",
                    now));
            }
        }
        }

        // Emit SessionEnd for sessions that were active but no longer are.
        foreach (var pair in wasActiveBySession.ToArray())
        {
            if (pair.Value && !currentActive.Contains(pair.Key))
            {
                activities.Add(new WorkBuddySessionActivity(
                    AgentSignal.SessionEnd,
                    pair.Key,
                    "workbuddy",
                    "SessionHeartbeatExpired",
                    now));
            }
        }

        // Update tracked state and prune stale entries.
        foreach (var key in currentActive)
        {
            wasActiveBySession[key] = true;
        }

        var keysToRemove = wasActiveBySession
            .Where(pair => !currentActive.Contains(pair.Key))
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in keysToRemove)
        {
            wasActiveBySession.Remove(key);
        }

        return activities;
    }

    private static string? ReadSessionId(FileInfo fileInfo)
    {
        try
        {
            using var stream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("sessionId", out var sessionIdElement)
                && sessionIdElement.ValueKind == JsonValueKind.String)
            {
                var value = sessionIdElement.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // Ignore parse errors; fall back to file name.
        }

        return null;
    }
}
