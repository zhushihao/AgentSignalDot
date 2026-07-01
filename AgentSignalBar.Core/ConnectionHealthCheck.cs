using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentSignalBar.Core;

public enum ConnectionHealthSeverity
{
    Ok,
    Warning,
    Missing,
    Error
}

public enum ConnectionHealthItemId
{
    AgentSignalCli,
    ClaudeHooks,
    CodexHooks,
    StateFile,
    CodexSessionLogs
}

public sealed record ConnectionHealthCheckOptions(
    string HomeDirectory,
    string AgentSignalCliPath,
    string StateFilePath,
    string CodexHooksPath,
    string CodexSessionsDirectory);

public sealed record ConnectionHealthItem(
    ConnectionHealthItemId Id,
    string Name,
    ConnectionHealthSeverity Severity,
    string Summary,
    string Detail);

public sealed record ConnectionHealthReport(
    DateTimeOffset CheckedAt,
    IReadOnlyList<ConnectionHealthItem> Items)
{
    public bool HasProblems => Items.Any(item => item.Severity != ConnectionHealthSeverity.Ok);
}

public static class ConnectionHealthCheck
{
    public static ConnectionHealthReport Run(ConnectionHealthCheckOptions options)
    {
        var items = new List<ConnectionHealthItem>
        {
            CliItem(options.AgentSignalCliPath),
            HookFileItem(
                ConnectionHealthItemId.ClaudeHooks,
                "Claude Code Hooks",
                Path.Combine(options.HomeDirectory, ".claude", "settings.json"),
                "Claude Code",
                HookConfigInstaller.ClaudeEvents,
                options.AgentSignalCliPath),
            HookFileItem(
                ConnectionHealthItemId.CodexHooks,
                "Codex Hooks",
                options.CodexHooksPath,
                "Codex",
                HookConfigInstaller.CodexEvents,
                options.AgentSignalCliPath),
            StateFileItem(options.StateFilePath),
            CodexSessionLogsItem(options.CodexSessionsDirectory)
        };

        return new ConnectionHealthReport(DateTimeOffset.UtcNow, items);
    }

    private static ConnectionHealthItem CliItem(string path)
    {
        return File.Exists(path)
            ? new ConnectionHealthItem(
                ConnectionHealthItemId.AgentSignalCli,
                "Agent Signal CLI",
                ConnectionHealthSeverity.Ok,
                "CLI exists",
                path)
            : new ConnectionHealthItem(
                ConnectionHealthItemId.AgentSignalCli,
                "Agent Signal CLI",
                ConnectionHealthSeverity.Missing,
                "CLI missing",
                path);
    }

    private static ConnectionHealthItem HookFileItem(
        ConnectionHealthItemId id,
        string name,
        string path,
        string targetName,
        IReadOnlyList<string> expectedEvents,
        string cliPath)
    {
        if (!File.Exists(path))
        {
            return new ConnectionHealthItem(id, name, ConnectionHealthSeverity.Missing, "Hook file missing", path);
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path));
            if (root is not JsonObject rootObject || rootObject["hooks"] is not JsonObject hooks)
            {
                return new ConnectionHealthItem(id, name, ConnectionHealthSeverity.Error, "Hook JSON is not recognized", path);
            }

            var missingEvents = new List<string>();
            var staleCount = 0;
            foreach (var eventName in expectedEvents)
            {
                var commands = CommandsForEvent(hooks, eventName);
                var expectedCommand = HookConfigInstaller.HookCommand(cliPath, eventName, targetName);
                if (!commands.Contains(expectedCommand, StringComparer.Ordinal))
                {
                    missingEvents.Add(eventName);
                }

                staleCount += commands.Count(command =>
                    !string.Equals(command, expectedCommand, StringComparison.Ordinal)
                    && HookConfigInstaller.IsAgentSignalHookCommand(command, targetName));
            }

            if (missingEvents.Count == 0 && staleCount == 0)
            {
                return new ConnectionHealthItem(
                    id,
                    name,
                    ConnectionHealthSeverity.Ok,
                    "Hooks installed",
                    $"{path} covers {expectedEvents.Count} event(s).");
            }

            var problems = new List<string>();
            if (missingEvents.Count > 0)
            {
                problems.Add("missing " + string.Join(", ", missingEvents.Take(4)) + (missingEvents.Count > 4 ? "..." : ""));
            }

            if (staleCount > 0)
            {
                problems.Add($"{staleCount} duplicate/stale Agent Signal Bar command(s)");
            }

            return new ConnectionHealthItem(
                id,
                name,
                ConnectionHealthSeverity.Warning,
                "Hooks need attention",
                $"{path}: {string.Join("; ", problems)}.");
        }
        catch (JsonException error)
        {
            return new ConnectionHealthItem(id, name, ConnectionHealthSeverity.Error, "Hook JSON cannot be parsed", $"{path}: {error.Message}");
        }
        catch (IOException error)
        {
            return new ConnectionHealthItem(id, name, ConnectionHealthSeverity.Error, "Hook file cannot be read", $"{path}: {error.Message}");
        }
    }

    private static List<string> CommandsForEvent(JsonObject hooks, string eventName)
    {
        var commands = new List<string>();
        if (hooks[eventName] is not JsonArray blocks)
        {
            return commands;
        }

        foreach (var blockNode in blocks)
        {
            if (blockNode is not JsonObject block || block["hooks"] is not JsonArray hookArray)
            {
                continue;
            }

            foreach (var hookNode in hookArray)
            {
                if (hookNode is JsonObject hook && hook["command"]?.GetValue<string>() is { } command)
                {
                    commands.Add(command);
                }
            }
        }

        return commands;
    }

    private static ConnectionHealthItem StateFileItem(string path)
    {
        if (!File.Exists(path))
        {
            return new ConnectionHealthItem(ConnectionHealthItemId.StateFile, "State File", ConnectionHealthSeverity.Missing, "State file missing", path);
        }

        try
        {
            var store = new SignalStateStore(path);
            var snapshot = store.ReadSnapshot();
            var updatedAt = snapshot.UpdatedAt?.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "unknown";
            return new ConnectionHealthItem(
                ConnectionHealthItemId.StateFile,
                "State File",
                snapshot.Aggregate == AgentSignal.Stale ? ConnectionHealthSeverity.Warning : ConnectionHealthSeverity.Ok,
                "State file readable",
                $"{path}: aggregate={AgentSignalParser.ToRawValue(snapshot.Aggregate)}, updated_at={updatedAt}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new ConnectionHealthItem(ConnectionHealthItemId.StateFile, "State File", ConnectionHealthSeverity.Error, "State file cannot be read", $"{path}: {error.Message}");
        }
    }

    private static ConnectionHealthItem CodexSessionLogsItem(string path)
    {
        if (!Directory.Exists(path))
        {
            return new ConnectionHealthItem(ConnectionHealthItemId.CodexSessionLogs, "Codex Session Logs", ConnectionHealthSeverity.Missing, "Session log directory missing", path);
        }

        try
        {
            var latest = Directory
                .EnumerateFiles(path, "rollout-*.jsonl", SearchOption.AllDirectories)
                .Select(file => new FileInfo(file))
                .Where(file => file.Exists)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latest is null)
            {
                return new ConnectionHealthItem(ConnectionHealthItemId.CodexSessionLogs, "Codex Session Logs", ConnectionHealthSeverity.Warning, "No Codex session logs found", path);
            }

            return new ConnectionHealthItem(
                ConnectionHealthItemId.CodexSessionLogs,
                "Codex Session Logs",
                ConnectionHealthSeverity.Ok,
                "Latest session log found",
                $"{latest.Name}: {latest.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new ConnectionHealthItem(ConnectionHealthItemId.CodexSessionLogs, "Codex Session Logs", ConnectionHealthSeverity.Error, "Session logs cannot be read", $"{path}: {error.Message}");
        }
    }
}
