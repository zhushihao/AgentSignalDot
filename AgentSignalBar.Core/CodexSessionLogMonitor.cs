using System.Text;
using System.Text.Json;

namespace AgentSignalBar.Core;

public sealed record CodexSessionLogActivity(
    AgentSignal Signal,
    string SessionId,
    string Agent,
    string Event,
    DateTimeOffset? Timestamp);

public static class CodexSessionLogParser
{
    public static CodexSessionLogActivity? ActivityFromLine(string line, string defaultSessionId, string defaultAgent = "codex-desktop")
    {
        using var document = ParseDocument(line);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        var timestamp = StringProperty(root, "timestamp") is { } timestampText
            && DateTimeOffset.TryParse(timestampText, out var parsedTimestamp)
                ? parsedTimestamp
                : (DateTimeOffset?)null;
        var topLevelType = StringProperty(root, "type");
        var payload = root.TryGetProperty("payload", out var payloadElement) && payloadElement.ValueKind == JsonValueKind.Object
            ? payloadElement
            : default;
        var agent = AgentName(payload) ?? defaultAgent;
        var sessionId = SessionId(payload, agent) ?? defaultSessionId;

        return topLevelType switch
        {
            "event_msg" => ActivityFromEventMessage(payload, sessionId, agent, timestamp),
            "response_item" => ActivityFromResponseItem(payload, sessionId, agent, timestamp),
            _ => null
        };
    }

    public static string? AgentNameFromSessionMetaLine(string line)
    {
        using var document = ParseDocument(line);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        if (StringProperty(root, "type") != "session_meta"
            || !root.TryGetProperty("payload", out var payload)
            || payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return AgentName(payload);
    }

    public static string SessionPrefix(string agent)
    {
        return agent.ToLowerInvariant() switch
        {
            "codex-cli" => "codex-cli",
            "codex-idea" or "codex-intellij" => "codex-idea",
            "codex-jetbrains" => "codex-jetbrains",
            "codex-vscode" => "codex-vscode",
            "codex-xcode" => "codex-xcode",
            "codex-ide" => "codex-ide",
            _ => "codex-desktop"
        };
    }

    private static CodexSessionLogActivity? ActivityFromEventMessage(
        JsonElement payload,
        string sessionId,
        string agent,
        DateTimeOffset? timestamp)
    {
        return StringProperty(payload, "type") switch
        {
            "token_count" => null,
            "task_started" or "user_message" => new CodexSessionLogActivity(AgentSignal.Thinking, sessionId, agent, "DesktopTaskStarted", timestamp),
            "task_complete" => new CodexSessionLogActivity(AgentSignal.Done, sessionId, agent, "DesktopTaskComplete", timestamp),
            "turn_aborted" => new CodexSessionLogActivity(AgentSignal.Done, sessionId, agent, "DesktopTurnAborted", timestamp),
            "agent_message" when StringProperty(payload, "phase") == "final_answer" =>
                new CodexSessionLogActivity(AgentSignal.Done, sessionId, agent, "DesktopTaskComplete", timestamp),
            "agent_message" => new CodexSessionLogActivity(AgentSignal.Working, sessionId, agent, "DesktopMessage", timestamp),
            _ => null
        };
    }

    private static CodexSessionLogActivity? ActivityFromResponseItem(
        JsonElement payload,
        string sessionId,
        string agent,
        DateTimeOffset? timestamp)
    {
        return StringProperty(payload, "type") switch
        {
            "reasoning" => new CodexSessionLogActivity(AgentSignal.Thinking, sessionId, agent, "DesktopThinking", timestamp),
            "function_call" or "custom_tool_call" => new CodexSessionLogActivity(
                ToolCallSignal(payload),
                sessionId,
                agent,
                "DesktopToolCall:" + ToolName(payload),
                timestamp),
            "function_call_output" => new CodexSessionLogActivity(AgentSignal.ToolDone, sessionId, agent, "DesktopToolDone", timestamp),
            "message" when StringProperty(payload, "role") == "user" => null,
            "message" when StringProperty(payload, "phase") == "final_answer" =>
                new CodexSessionLogActivity(AgentSignal.Done, sessionId, agent, "DesktopTaskComplete", timestamp),
            "message" => new CodexSessionLogActivity(AgentSignal.ToolDone, sessionId, agent, "DesktopMessage", timestamp),
            _ => null
        };
    }

    private static AgentSignal ToolCallSignal(JsonElement payload)
    {
        return ToolName(payload).Equals("request_user_input", StringComparison.OrdinalIgnoreCase)
            ? AgentSignal.Attention
            : AgentSignal.Working;
    }

    private static string ToolName(JsonElement payload)
    {
        var name = StringProperty(payload, "name");
        return string.IsNullOrWhiteSpace(name) ? "tool" : name.Trim();
    }

    private static string? SessionId(JsonElement payload, string agent)
    {
        foreach (var key in new[] { "threadId", "thread_id", "conversationId", "conversation_id" })
        {
            if (StringProperty(payload, key) is { } value && !string.IsNullOrWhiteSpace(value))
            {
                return SessionPrefix(agent) + ":" + value.Trim();
            }
        }

        return null;
    }

    private static string? AgentName(JsonElement payload)
    {
        var source = FirstString(payload, "source", "client", "app", "application", "entrypoint", "runner")?.ToLowerInvariant() ?? "";
        var originator = FirstString(payload, "originator")?.ToLowerInvariant() ?? "";
        var combined = string.Join(" ", new[] { source, originator }.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (ContainsAny(source, "exec", "cli", "terminal", "shell", "tui"))
        {
            return "codex-cli";
        }

        if (ContainsAny(originator, "xcode"))
        {
            return "codex-xcode";
        }

        if (ContainsAny(originator, "idea", "intellij"))
        {
            return "codex-idea";
        }

        if (ContainsAny(originator, "jetbrains"))
        {
            return "codex-jetbrains";
        }

        if (ContainsAny(originator, "codex desktop"))
        {
            return "codex-desktop";
        }

        if (ContainsAny(combined, "idea", "intellij"))
        {
            return "codex-idea";
        }

        if (ContainsAny(combined, "jetbrains"))
        {
            return "codex-jetbrains";
        }

        if (ContainsAny(combined, "visual studio code", "vscode", "vs-code", "codex_vscode"))
        {
            return "codex-vscode";
        }

        if (ContainsAny(combined, "xcode"))
        {
            return "codex-xcode";
        }

        if (ContainsAny(combined, "ide"))
        {
            return "codex-ide";
        }

        if (ContainsAny(combined, "desktop", "app"))
        {
            return "codex-desktop";
        }

        return null;
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.Contains(token, StringComparison.Ordinal));
    }

    private static string? FirstString(JsonElement payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (StringProperty(payload, key) is { } value && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? StringProperty(JsonElement element, string key)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static JsonDocument? ParseDocument(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class CodexSessionLogMonitor
{
    private const int RecentFileLimit = 8;
    private readonly IReadOnlyList<string> sessionRoots;
    private readonly Dictionary<string, long> offsetsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> agentsByPath = new(StringComparer.OrdinalIgnoreCase);
    private bool primed;

    public CodexSessionLogMonitor(IReadOnlyList<string> sessionRoots)
    {
        this.sessionRoots = sessionRoots;
    }

    public static CodexSessionLogMonitor ForCurrentUser()
    {
        return new CodexSessionLogMonitor([DefaultSessionRoot()]);
    }

    public static string DefaultSessionRoot()
    {
        if (Environment.GetEnvironmentVariable("AGENT_SIGNAL_CODEX_SESSIONS_DIR") is { } overridePath
            && !string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "sessions");
    }

    public IReadOnlyList<CodexSessionLogActivity> Poll(DateTimeOffset now)
    {
        var files = RecentSessionFiles();
        if (!primed)
        {
            Prime(files);
            primed = true;
            return [];
        }

        var activities = new List<CodexSessionLogActivity>();
        foreach (var file in files)
        {
            var lines = ReadNewLines(file);
            activities.AddRange(ParseLines(lines, file));
        }

        return activities
            .OrderBy(activity => activity.Timestamp ?? now)
            .ToArray();
    }

    private void Prime(IReadOnlyList<FileInfo> files)
    {
        foreach (var file in files)
        {
            var lines = ReadAllCompleteLines(file.FullName);
            offsetsByPath[file.FullName] = file.Length;
            foreach (var line in lines)
            {
                if (CodexSessionLogParser.AgentNameFromSessionMetaLine(line) is { } agent)
                {
                    agentsByPath[file.FullName] = agent;
                    break;
                }
            }
        }
    }

    private IReadOnlyList<CodexSessionLogActivity> ParseLines(IReadOnlyList<string> lines, FileInfo file)
    {
        var activities = new List<CodexSessionLogActivity>();
        foreach (var line in lines)
        {
            if (CodexSessionLogParser.AgentNameFromSessionMetaLine(line) is { } agent)
            {
                agentsByPath[file.FullName] = agent;
                continue;
            }

            var resolvedAgent = agentsByPath.TryGetValue(file.FullName, out var knownAgent)
                ? knownAgent
                : "codex-desktop";
            if (CodexSessionLogParser.ActivityFromLine(line, SessionId(file, resolvedAgent), resolvedAgent) is { } activity)
            {
                activities.Add(activity);
            }
        }

        return activities;
    }

    private IReadOnlyList<string> ReadNewLines(FileInfo file)
    {
        if (!offsetsByPath.TryGetValue(file.FullName, out var offset))
        {
            offsetsByPath[file.FullName] = file.Length;
            return [];
        }

        file.Refresh();
        if (file.Length <= offset)
        {
            if (file.Length < offset)
            {
                offsetsByPath[file.FullName] = 0;
            }
            return [];
        }

        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var text = reader.ReadToEnd();
        offsetsByPath[file.FullName] = stream.Position;
        return CompleteLines(text);
    }

    private static IReadOnlyList<string> ReadAllCompleteLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return CompleteLines(reader.ReadToEnd());
    }

    private static IReadOnlyList<string> CompleteLines(string text)
    {
        return text
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    private IReadOnlyList<FileInfo> RecentSessionFiles()
    {
        return sessionRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "rollout-*.jsonl", SearchOption.AllDirectories))
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(RecentFileLimit)
            .ToArray();
    }

    private static string SessionId(FileInfo file, string agent)
    {
        var name = Path.GetFileNameWithoutExtension(file.Name);
        var parts = name.Split('-');
        var suffix = parts.Length >= 5
            ? string.Join("-", parts.Skip(parts.Length - 5))
            : "";
        var session = suffix.Length == 36 ? suffix : "global";
        return CodexSessionLogParser.SessionPrefix(agent) + ":" + session;
    }
}
