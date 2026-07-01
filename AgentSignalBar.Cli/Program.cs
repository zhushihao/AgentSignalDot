using System.Text.Json;
using System.Text.Json.Serialization;
using AgentSignalBar.Core;

return AgentSignalCli.Run(args, Console.In, Console.Out, Console.Error);

internal static class AgentSignalCli
{
    public static int Run(string[] args, TextReader input, TextWriter output, TextWriter error)
    {
        if (args.Length == 0)
        {
            PrintUsage(output);
            return 0;
        }

        try
        {
            var command = args[0];
            var rest = args.Skip(1).ToArray();
            var store = new SignalStateStore();

            switch (command)
            {
                case "help":
                case "--help":
                case "-h":
                    PrintUsage(output);
                    return 0;
                case "list":
                    PrintSignals(output);
                    return 0;
                case "status":
                    {
                        var parsed = ParsedArguments.Parse(rest);
                        parsed.RequireNoPositionals();
                        PrintStatus(output, store.ReadSnapshot(), parsed.PrintJson);
                        return 0;
                    }
                case "clear":
                case "clear-warning":
                case "clear-warnings":
                    {
                        var parsed = ParsedArguments.Parse(rest);
                        parsed.RequireNoPositionals();
                        PrintStatus(output, store.ClearWarnings(), parsed.PrintJson);
                        return 0;
                    }
                case "reset":
                case "clear-all":
                    {
                        var parsed = ParsedArguments.Parse(rest);
                        parsed.RequireNoPositionals();
                        PrintStatus(output, store.ClearSessions(), parsed.PrintJson);
                        return 0;
                    }
                case "set":
                case "play":
                    {
                        var parsed = ParsedArguments.Parse(rest);
                        parsed.RequirePositionals(1);
                        var signal = AgentSignalParser.Normalize(parsed.Positionals[0])
                            ?? throw new CliException("Missing or unknown signal.");
                        var snapshot = parsed.SessionId is null
                            ? store.SetManualSignal(signal)
                            : store.ApplySessionSignal(signal, parsed.SessionId, parsed.Agent, parsed.Event);
                        PrintStatus(output, snapshot, parsed.PrintJson);
                        return 0;
                    }
                case "session":
                    {
                        var parsed = ParsedArguments.Parse(rest);
                        parsed.RequirePositionals(1);
                        var signal = AgentSignalParser.Normalize(parsed.Positionals[0])
                            ?? throw new CliException("Missing or unknown session signal.");
                        PrintStatus(output, store.ApplySessionSignal(signal, parsed.SessionId ?? "global", parsed.Agent, parsed.Event), parsed.PrintJson);
                        return 0;
                    }
                case "codex-hook":
                    {
                        var parsed = ParsedArguments.Parse(rest);
                        parsed.RequireAtMostOnePositional();
                        var payload = ReadJsonObject(input);
                        var eventName = parsed.Positionals.FirstOrDefault()
                            ?? HookPayload.FirstString(payload, "hook_event_name", "event_name", "event", "hook", "type");
                        var signal = CodexHookAdapter.ChooseSignal(eventName, payload);
                        var agent = parsed.Agent ?? "codex-cli";
                        var sessionId = parsed.SessionId
                            ?? HookPayload.FirstString(payload, "session_id", "conversation_id", "thread_id", "chat_id", "codex_session_id")
                            ?? "global";
                        store.ApplySessionSignal(signal, SourceScopedSessionKey(sessionId, agent), agent, parsed.Event ?? eventName);
                        return 0;
                    }
                case "claude-hook":
                    {
                        var parsed = ParsedArguments.Parse(rest);
                        parsed.RequireAtMostOnePositional();
                        var payload = ReadJsonObject(input);
                        var eventName = parsed.Positionals.FirstOrDefault() ?? ClaudeHookAdapter.EventName(payload);
                        var signal = ClaudeHookAdapter.ChooseSignal(eventName, payload);
                        var sessionId = parsed.SessionId
                            ?? HookPayload.FirstString(payload, "session_id", "conversation_id", "thread_id", "chat_id", "claude_session_id")
                            ?? "claude-global";
                        store.ApplySessionSignal(signal, sessionId, parsed.Agent ?? "claude-code", parsed.Event ?? eventName);
                        return 0;
                    }
                case "agent-hook":
                case "generic-hook":
                case "hook":
                    {
                        var parsed = ParsedArguments.Parse(rest);
                        parsed.RequireAtMostOnePositional();
                        var payload = ReadJsonObject(input);
                        var eventName = parsed.Positionals.FirstOrDefault()
                            ?? HookPayload.FirstString(payload, "hook_event_name", "event_name", "event", "hook", "type", "action", "name");
                        var signal = GenericHookAdapter.ChooseSignal(eventName, payload);
                        var agent = parsed.Agent
                            ?? HookPayload.FirstString(payload, "agent", "agent_name", "source", "source_name", "app", "application", "client", "tool", "runner", "provider")
                            ?? "agent";
                        var sessionId = parsed.SessionId
                            ?? HookPayload.FirstString(payload, "session_id", "session", "conversation_id", "thread_id", "chat_id", "run_id", "job_id", "task_id")
                            ?? $"{agent}:global";
                        store.ApplySessionSignal(signal, sessionId, agent, parsed.Event ?? eventName);
                        return 0;
                    }
                case "install-hooks":
                    {
                        var parsed = ParsedArguments.Parse(rest);
                        parsed.RequireNoPositionals();
                        var cliPath = parsed.CliPath
                            ?? Environment.ProcessPath
                            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                            ?? "agent-signal.exe";
                        var home = parsed.HomeDirectory
                            ?? Environment.GetEnvironmentVariable("USERPROFILE")
                            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        var previews = InstallHooks(parsed, home, cliPath);
                        PrintHookInstallPreviews(output, previews, parsed.PrintJson, parsed.DryRun);
                        return 0;
                    }
                case "desktop-switch":
                    {
                        var parsed = ParsedArguments.Parse(rest);
                        parsed.RequireNoPositionals();
                        var windowsExe = ResolveWindowsExe(parsed.WindowsExePath);
                        var result = RunDesktopSwitch(windowsExe, parsed.DryRun);
                        PrintDesktopSwitchResult(output, result, parsed.PrintJson);
                        return 0;
                    }
                case "install-desktop-switch":
                    {
                        var parsed = ParsedArguments.Parse(rest);
                        parsed.RequireNoPositionals();
                        var cliPath = parsed.CliPath
                            ?? Environment.ProcessPath
                            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                            ?? "agent-signal.exe";
                        var windowsExe = ResolveWindowsExe(parsed.WindowsExePath);
                        var desktop = parsed.DesktopDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        var result = InstallDesktopSwitch(desktop, cliPath, windowsExe, parsed.DryRun);
                        PrintDesktopSwitchInstallResult(output, result, parsed.PrintJson);
                        return 0;
                    }
                default:
                    {
                        var signal = AgentSignalParser.Normalize(command);
                        if (signal is null)
                        {
                            throw new CliException($"Unknown command: {command}");
                        }

                        var parsed = ParsedArguments.Parse(rest);
                        parsed.RequireNoPositionals();
                        var snapshot = parsed.SessionId is null
                            ? store.SetManualSignal(signal.Value)
                            : store.ApplySessionSignal(signal.Value, parsed.SessionId, parsed.Agent, parsed.Event);
                        PrintStatus(output, snapshot, parsed.PrintJson);
                        return 0;
                    }
            }
        }
        catch (Exception ex) when (ex is CliException or JsonException or InvalidOperationException)
        {
            error.WriteLine($"agent-signal: {ex.Message}");
            return 1;
        }
    }

    private static DesktopSwitchResult RunDesktopSwitch(string windowsExePath, bool dryRun)
    {
        var processes = RunningWindowsProcesses().ToArray();
        if (processes.Length > 0)
        {
            if (!dryRun)
            {
                foreach (var process in processes)
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(2000))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
            }

            return new DesktopSwitchResult("stop", windowsExePath, processes.Length, dryRun);
        }

        if (!File.Exists(windowsExePath))
        {
            throw new CliException($"Windows app not found: {windowsExePath}");
        }

        if (!dryRun)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = windowsExePath,
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            });
        }

        return new DesktopSwitchResult("start", windowsExePath, 0, dryRun);
    }

    private static DesktopSwitchInstallResult InstallDesktopSwitch(string desktopDirectory, string cliPath, string windowsExePath, bool dryRun)
    {
        var shortcutPath = Path.Combine(desktopDirectory, "红绿灯开关.lnk");
        var arguments = $"desktop-switch --windows-exe {QuoteWindowsArgument(windowsExePath)}";

        if (!dryRun)
        {
            Directory.CreateDirectory(desktopDirectory);
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new CliException("WScript.Shell is not available.");
            dynamic shell = Activator.CreateInstance(shellType)
                ?? throw new CliException("Cannot create WScript.Shell.");
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = cliPath;
            shortcut.Arguments = arguments;
            shortcut.WorkingDirectory = Path.GetDirectoryName(windowsExePath) ?? desktopDirectory;
            shortcut.Description = "启动或关闭 Agent Signal Bar 红绿灯";
            if (File.Exists(windowsExePath))
            {
                shortcut.IconLocation = windowsExePath + ",0";
            }
            shortcut.Save();
        }

        return new DesktopSwitchInstallResult(shortcutPath, cliPath, arguments, windowsExePath, dryRun);
    }

    private static IEnumerable<System.Diagnostics.Process> RunningWindowsProcesses()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("AgentSignalBar.Windows"))
        {
            try
            {
                if (!process.HasExited)
                {
                    yield return process;
                }
            }
            finally
            {
                if (process.HasExited)
                {
                    process.Dispose();
                }
            }
        }
    }

    private static string ResolveWindowsExe(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var baseDirectory = AppContext.BaseDirectory;
        var repositoryRoot = FindRepositoryRoot(baseDirectory);
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "AgentSignalBar.Windows.exe"),
            Path.Combine(baseDirectory, "..", "AgentSignalBar.Windows", "AgentSignalBar.Windows.exe"),
            repositoryRoot is null
                ? ""
                : Path.Combine(repositoryRoot, "windows", "AgentSignalBar.Windows", "bin", "Debug", "net8.0-windows", "AgentSignalBar.Windows.exe"),
            repositoryRoot is null
                ? ""
                : Path.Combine(repositoryRoot, "windows", "AgentSignalBar.Windows", "bin", "Release", "net8.0-windows", "AgentSignalBar.Windows.exe")
        };

        foreach (var candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return Path.GetFullPath(candidates.First(candidate => !string.IsNullOrWhiteSpace(candidate)));
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

    private static void PrintDesktopSwitchResult(TextWriter output, DesktopSwitchResult result, bool asJson)
    {
        if (asJson)
        {
            output.WriteLine(JsonSerializer.Serialize(result, JsonOptions.CreateIndented()));
            return;
        }

        output.WriteLine($"{(result.DryRun ? "preview" : "desktop switch")}: {result.Action} {result.WindowsExePath}");
    }

    private static void PrintDesktopSwitchInstallResult(TextWriter output, DesktopSwitchInstallResult result, bool asJson)
    {
        if (asJson)
        {
            output.WriteLine(JsonSerializer.Serialize(result, JsonOptions.CreateIndented()));
            return;
        }

        output.WriteLine($"{(result.DryRun ? "preview" : "installed")} desktop switch: {result.ShortcutPath}");
    }

    private static string QuoteWindowsArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static IReadOnlyList<HookInstallPreview> InstallHooks(ParsedArguments parsed, string home, string cliPath)
    {
        var previews = new List<HookInstallPreview>();
        if (parsed.HookTarget is "all" or "claude")
        {
            previews.Add(parsed.DryRun
                ? HookConfigInstaller.PreviewClaudeInstall(home, cliPath)
                : HookConfigInstaller.InstallClaude(home, cliPath));
        }

        if (parsed.HookTarget is "all" or "codex")
        {
            foreach (var codexPath in CodexHookConfigPaths(parsed, home))
            {
                previews.Add(parsed.DryRun
                    ? HookConfigInstaller.PreviewCodexInstall(codexPath, cliPath)
                    : HookConfigInstaller.InstallCodex(codexPath, cliPath));
            }
        }

        return previews;
    }

    private static IEnumerable<string> CodexHookConfigPaths(ParsedArguments parsed, string home)
    {
        var scope = parsed.CodexScope;
        if (scope is "user" or "both")
        {
            yield return Path.Combine(home, ".codex", "hooks.json");
        }

        if (scope is "project" or "both")
        {
            var projectRoot = parsed.ProjectRoot ?? Directory.GetCurrentDirectory();
            yield return Path.Combine(projectRoot, ".codex", "hooks.json");
        }
    }

    private static void PrintHookInstallPreviews(TextWriter output, IReadOnlyList<HookInstallPreview> previews, bool asJson, bool dryRun)
    {
        if (asJson)
        {
            output.WriteLine(JsonSerializer.Serialize(
                previews.Select(preview => new HookInstallOutput(preview, dryRun)).ToArray(),
                JsonOptions.CreateIndented()));
            return;
        }

        foreach (var preview in previews)
        {
            var verb = dryRun ? "preview" : "installed";
            var status = preview.Changed ? "changed" : "already configured";
            output.WriteLine($"{verb} {preview.TargetName}: {preview.Path} ({status})");
        }
    }

    private static Dictionary<string, object?> ReadJsonObject(TextReader input)
    {
        var text = input.ReadToEnd();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Dictionary<string, object?>();
        }

        // 修复：先清理整个输入中的非法转义序列（如 \.），再提取 JSON 对象。
        // Claude Code 发送的 JSON 可能包含非法转义，Utf8JsonReader 在 FirstJsonObject
        // 中就会抛出异常，因此必须在提取前清理。
        var cleanedText = CleanIllegalEscapes(text);

        try
        {
            var jsonText = FirstJsonObject(cleanedText);
            using var document = JsonDocument.Parse(jsonText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Hook JSON payload must be an object.");
            }

            return HookPayload.FromJsonElement(document.RootElement);
        }
        catch (JsonException ex)
        {
            // 记录失败时的原始输入和清理后输入，便于诊断复杂 JSON 问题
            // 作为本地工具日志，不纳入版本控制
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AgentSignalBar",
                    "logs");
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, $"hook-parse-failure-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.txt");
                File.WriteAllText(logPath,
                    $"Exception: {ex}\n\n" +
                    $"--- Original text ({text.Length} chars) ---\n{text}\n\n" +
                    $"--- Cleaned text ({cleanedText.Length} chars) ---\n{cleanedText}",
                    System.Text.Encoding.UTF8);
            }
            catch
            {
                // 日志记录失败不应阻塞 fallback
            }

            // 如果解析仍然失败，尝试从原始文本中提取关键字段
            // 作为 fallback，不阻塞 Claude Code 运行
            return ExtractFallbackPayload(text, ex);
        }
    }

    /// <summary>
    /// 清理 JSON 中的非法转义序列。
    /// 某些工具（如 Claude Code）可能在 JSON 字符串中包含非法转义（如 \.），
    /// 这会触发 JsonException。此方法将这些非法转义替换为合法形式。
    /// </summary>
    private static string CleanIllegalEscapes(string json)
    {
        // 匹配反斜杠后面跟了非标准 JSON 转义字符的情况
        // 标准转义：\" \\ \/ \b \f \n \r \t \uXXXX
        // 非法转义：\. \, \: 等
        var result = new System.Text.StringBuilder(json.Length);
        for (var i = 0; i < json.Length; i++)
        {
            var ch = json[i];
            if (ch == '\\' && i + 1 < json.Length)
            {
                var next = json[i + 1];
                // 检查是否为标准 JSON 转义
                if (next is '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't' or 'u')
                {
                    // 标准转义，保留
                    result.Append(ch);
                    result.Append(next);
                    i++;
                }
                else
                {
                    // 非法转义：去掉反斜杠，只保留下一个字符
                    // 例如：\. → .    \, → ,
                    result.Append(next);
                    i++;
                }
            }
            else
            {
                result.Append(ch);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// 当 JSON 解析失败时，尝试从原始文本中提取关键字段作为 fallback。
    /// 确保 Claude Code 不会因为 Hook 失败而被阻塞。
    /// </summary>
    private static Dictionary<string, object?> ExtractFallbackPayload(string text, JsonException originalException)
    {
        var fallback = new Dictionary<string, object?>(StringComparer.Ordinal);
        
        try
        {
            // 尝试用正则提取 event 和 type 字段
            var eventMatch = System.Text.RegularExpressions.Regex.Match(text, @"""(hook_event_name|event_name|event|type|name)""\s*:\s*""([^""]+)""");
            if (eventMatch.Success)
            {
                fallback["event"] = eventMatch.Groups[2].Value;
            }

            var sessionMatch = System.Text.RegularExpressions.Regex.Match(text, @"""(session_id|conversation_id)""\s*:\s*""([^""]+)""");
            if (sessionMatch.Success)
            {
                fallback["session_id"] = sessionMatch.Groups[2].Value;
            }
        }
        catch
        {
            // 如果正则也失败，返回空字典
        }

        return fallback;
    }

    private static string FirstJsonObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0)
        {
            throw new JsonException("Hook JSON payload must contain an object.");
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(text[start..]);
        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Hook JSON payload must contain an object.");
        }

        var depth = 0;
        do
        {
            if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
            {
                depth++;
            }
            else if (reader.TokenType == JsonTokenType.EndObject || reader.TokenType == JsonTokenType.EndArray)
            {
                depth--;
                if (depth == 0)
                {
                    return System.Text.Encoding.UTF8.GetString(bytes.AsSpan(0, (int)reader.BytesConsumed));
                }
            }
        } while (reader.Read());

        throw new JsonException("Hook JSON payload object is incomplete.");
    }

    private static string SourceScopedSessionKey(string sessionId, string agent)
    {
        var normalizedAgent = agent.Trim().ToLowerInvariant().Replace("_", "-", StringComparison.Ordinal).Replace(" ", "-", StringComparison.Ordinal);
        if (normalizedAgent == "codex-desktop" || sessionId.StartsWith(normalizedAgent + ":", StringComparison.Ordinal))
        {
            return sessionId;
        }

        return $"{normalizedAgent}:{sessionId}";
    }

    private static void PrintStatus(TextWriter output, SignalSnapshot snapshot, bool asJson)
    {
        if (asJson)
        {
            var dto = new StatusOutput(snapshot);
            output.WriteLine(JsonSerializer.Serialize(dto, JsonOptions.CreateIndented()));
            return;
        }

        output.WriteLine($"aggregate: {AgentSignalParser.ToRawValue(snapshot.Aggregate)}");
        output.WriteLine($"state_file: {snapshot.StateFilePath}");
        output.WriteLine(snapshot.Sessions.Count == 0 ? "sessions: none" : "sessions:");
        foreach (var session in snapshot.Sessions)
        {
            output.WriteLine($"- {session.SessionId}: {AgentSignalParser.ToRawValue(session.Signal)} agent={session.Agent} event={session.LastEvent}");
        }
    }

    private static void PrintSignals(TextWriter output)
    {
        foreach (var signal in Enum.GetValues<AgentSignal>())
        {
            output.WriteLine($"{AgentSignalParser.ToRawValue(signal)}\t{AgentSignalParser.ToRawValue(signal.DisplayState())}");
        }
    }

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine("""
        agent-signal

        Usage:
          agent-signal list
          agent-signal status [--json]
          agent-signal <signal> [--session <id>] [--agent <name>] [--event <event>] [--json]
          agent-signal codex-hook [event]
          agent-signal claude-hook [event]
          agent-signal agent-hook [event]
          agent-signal install-hooks [--target all|claude|codex] [--codex-scope user|project|both] [--dry-run] [--json]
          agent-signal clear-warning [--json]
          agent-signal reset [--json]
        """);
    }
}

internal sealed class StatusOutput
{
    public StatusOutput(SignalSnapshot snapshot)
    {
        Aggregate = snapshot.Aggregate;
        DisplayState = snapshot.Aggregate.DisplayState();
        LampState = DisplayState;
        Priority = DisplayState.Priority();
        DisplayName = snapshot.Aggregate.DisplayName();
        Summary = snapshot.Aggregate.Summary();
        Action = DisplayState.HumanAction();
        StateFile = snapshot.StateFilePath;
        UpdatedAt = snapshot.UpdatedAt;
        Sessions = snapshot.Sessions.Select(SessionOutput.From).ToArray();
        RecentEvents = snapshot.RecentEvents.Select(RecentEventOutput.From).ToArray();
    }

    [JsonPropertyName("schema_version")]
    public int SchemaVersion => 1;

    [JsonPropertyName("aggregate")]
    public AgentSignal Aggregate { get; }

    [JsonPropertyName("display_state")]
    public DisplayState DisplayState { get; }

    [JsonPropertyName("lamp_state")]
    public DisplayState LampState { get; }

    [JsonPropertyName("priority")]
    public int Priority { get; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; }

    [JsonPropertyName("summary")]
    public string Summary { get; }

    [JsonPropertyName("action")]
    public string Action { get; }

    [JsonPropertyName("state_file")]
    public string StateFile { get; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; }

    [JsonPropertyName("sessions")]
    public IReadOnlyList<SessionOutput> Sessions { get; }

    [JsonPropertyName("recent_events")]
    public IReadOnlyList<RecentEventOutput> RecentEvents { get; }
}

internal sealed class SessionOutput
{
    private SessionOutput(SessionStatus session)
    {
        SessionId = session.SessionId;
        Agent = session.Agent;
        Signal = session.Signal;
        LastEvent = session.LastEvent;
        UpdatedAt = session.UpdatedAt;
    }

    public static SessionOutput From(SessionStatus session) => new(session);

    [JsonPropertyName("session_id")]
    public string SessionId { get; }

    [JsonPropertyName("agent")]
    public string? Agent { get; }

    [JsonPropertyName("signal")]
    public AgentSignal Signal { get; }

    [JsonPropertyName("last_event")]
    public string? LastEvent { get; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; }
}

internal sealed class RecentEventOutput
{
    private RecentEventOutput(RecentSignalEvent signalEvent)
    {
        Id = signalEvent.Id;
        SessionId = signalEvent.SessionId;
        Agent = signalEvent.Agent;
        Signal = signalEvent.Signal;
        Event = signalEvent.Event;
        UpdatedAt = signalEvent.UpdatedAt;
    }

    public static RecentEventOutput From(RecentSignalEvent signalEvent) => new(signalEvent);

    [JsonPropertyName("id")]
    public string Id { get; }

    [JsonPropertyName("session_id")]
    public string SessionId { get; }

    [JsonPropertyName("agent")]
    public string? Agent { get; }

    [JsonPropertyName("signal")]
    public AgentSignal Signal { get; }

    [JsonPropertyName("event")]
    public string? Event { get; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; }
}

internal sealed class HookInstallOutput
{
    public HookInstallOutput(HookInstallPreview preview, bool dryRun)
    {
        TargetName = preview.TargetName;
        Path = preview.Path;
        Existed = preview.Existed;
        Changed = preview.Changed;
        DryRun = dryRun;
        ConfigJson = preview.Json;
    }

    [JsonPropertyName("target_name")]
    public string TargetName { get; }

    [JsonPropertyName("path")]
    public string Path { get; }

    [JsonPropertyName("existed")]
    public bool Existed { get; }

    [JsonPropertyName("changed")]
    public bool Changed { get; }

    [JsonPropertyName("dry_run")]
    public bool DryRun { get; }

    [JsonPropertyName("config_json")]
    public string ConfigJson { get; }
}

internal sealed class DesktopSwitchResult(string action, string windowsExePath, int matchedProcesses, bool dryRun)
{
    [JsonPropertyName("action")]
    public string Action { get; } = action;

    [JsonPropertyName("windows_exe_path")]
    public string WindowsExePath { get; } = windowsExePath;

    [JsonPropertyName("matched_processes")]
    public int MatchedProcesses { get; } = matchedProcesses;

    [JsonPropertyName("dry_run")]
    public bool DryRun { get; } = dryRun;
}

internal sealed class DesktopSwitchInstallResult(string shortcutPath, string targetPath, string arguments, string windowsExePath, bool dryRun)
{
    [JsonPropertyName("shortcut_path")]
    public string ShortcutPath { get; } = shortcutPath;

    [JsonPropertyName("target_path")]
    public string TargetPath { get; } = targetPath;

    [JsonPropertyName("arguments")]
    public string Arguments { get; } = arguments;

    [JsonPropertyName("windows_exe_path")]
    public string WindowsExePath { get; } = windowsExePath;

    [JsonPropertyName("dry_run")]
    public bool DryRun { get; } = dryRun;
}

internal sealed class ParsedArguments
{
    public List<string> Positionals { get; } = [];
    public string? SessionId { get; private set; }
    public string? Agent { get; private set; }
    public string? Event { get; private set; }
    public bool PrintJson { get; private set; }
    public bool DryRun { get; private set; }
    public string HookTarget { get; private set; } = "all";
    public string CodexScope { get; private set; } = "user";
    public string? HomeDirectory { get; private set; }
    public string? ProjectRoot { get; private set; }
    public string? CliPath { get; private set; }
    public string? WindowsExePath { get; private set; }
    public string? DesktopDirectory { get; private set; }

    public static ParsedArguments Parse(string[] args)
    {
        var parsed = new ParsedArguments();
        for (var i = 0; i < args.Length;)
        {
            var value = args[i];
            switch (value)
            {
                case "--session":
                case "-s":
                    parsed.SessionId = OptionValue(args, i, value);
                    i += 2;
                    break;
                case "--agent":
                case "--source":
                    parsed.Agent = OptionValue(args, i, value);
                    i += 2;
                    break;
                case "--event":
                case "--label":
                    parsed.Event = OptionValue(args, i, value);
                    i += 2;
                    break;
                case "--json":
                case "-j":
                    parsed.PrintJson = true;
                    i += 1;
                    break;
                case "--dry-run":
                case "--check":
                    parsed.DryRun = true;
                    i += 1;
                    break;
                case "--install":
                    parsed.DryRun = false;
                    i += 1;
                    break;
                case "--target":
                    parsed.HookTarget = OneOf(OptionValue(args, i, value), value, ["all", "claude", "codex"]);
                    i += 2;
                    break;
                case "--codex-scope":
                    parsed.CodexScope = OneOf(OptionValue(args, i, value), value, ["user", "project", "both"]);
                    i += 2;
                    break;
                case "--home":
                    parsed.HomeDirectory = OptionValue(args, i, value);
                    i += 2;
                    break;
                case "--project-root":
                    parsed.ProjectRoot = OptionValue(args, i, value);
                    i += 2;
                    break;
                case "--cli":
                case "--cli-path":
                    parsed.CliPath = OptionValue(args, i, value);
                    i += 2;
                    break;
                case "--windows-exe":
                case "--windows-exe-path":
                    parsed.WindowsExePath = OptionValue(args, i, value);
                    i += 2;
                    break;
                case "--desktop":
                case "--desktop-directory":
                    parsed.DesktopDirectory = OptionValue(args, i, value);
                    i += 2;
                    break;
                default:
                    if (value.StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new CliException($"Unknown option: {value}");
                    }
                    parsed.Positionals.Add(value);
                    i += 1;
                    break;
            }
        }

        return parsed;
    }

    public void RequireNoPositionals()
    {
        if (Positionals.Count > 0)
        {
            throw new CliException($"Unexpected argument: {Positionals[0]}");
        }
    }

    public void RequireAtMostOnePositional()
    {
        if (Positionals.Count > 1)
        {
            throw new CliException($"Unexpected argument: {Positionals[1]}");
        }
    }

    public void RequirePositionals(int count)
    {
        if (Positionals.Count < count)
        {
            throw new CliException("Missing argument.");
        }
        if (Positionals.Count > count)
        {
            throw new CliException($"Unexpected argument: {Positionals[count]}");
        }
    }

    private static string OptionValue(string[] args, int index, string option)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
        {
            throw new CliException($"Missing value for {option}.");
        }

        return args[index + 1];
    }

    private static string OneOf(string value, string option, string[] allowed)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (allowed.Contains(normalized))
        {
            return normalized;
        }

        throw new CliException($"Invalid value for {option}: {value}.");
    }
}

internal static class HookPayload
{
    public static Dictionary<string, object?> FromJsonElement(JsonElement element)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = ScalarValue(property.Value);
        }
        return result;
    }

    public static string? FirstString(IReadOnlyDictionary<string, object?> payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (payload.TryGetValue(key, out var value) && ScalarString(value) is { } scalar && !string.IsNullOrWhiteSpace(scalar))
            {
                return scalar;
            }
        }

        var normalized = keys.Select(Normalize).ToHashSet();
        foreach (var pair in payload)
        {
            if (normalized.Contains(Normalize(pair.Key)) && ScalarString(pair.Value) is { } scalar && !string.IsNullOrWhiteSpace(scalar))
            {
                return scalar;
            }
        }

        return null;
    }

    private static object? ScalarValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when element.TryGetDouble(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static string? ScalarString(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            bool boolean => boolean ? "true" : "false",
            long integer => integer.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static string Normalize(string value)
    {
        return new string(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}

public static class GenericHookAdapter
{
    private static readonly Dictionary<string, AgentSignal> Events = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AgentStarted"] = AgentSignal.Working,
        ["TaskStarted"] = AgentSignal.Working,
        ["UserPromptSubmit"] = AgentSignal.Thinking,
        ["PreToolUse"] = AgentSignal.Working,
        ["PostToolUse"] = AgentSignal.ToolDone,
        ["ApprovalRequired"] = AgentSignal.PermissionRequest,
        ["PermissionRequest"] = AgentSignal.PermissionRequest,
        ["Failed"] = AgentSignal.Blocked,
        ["Error"] = AgentSignal.Error,
        ["AgentFinished"] = AgentSignal.Done,
        ["Done"] = AgentSignal.Done,
        ["Stop"] = AgentSignal.Done
    };

    public static AgentSignal ChooseSignal(string? eventName, IReadOnlyDictionary<string, object?> payload)
    {
        if (HookPayload.FirstString(payload, "signal", "signal_name", "lamp_signal", "agent_signal") is { } explicitSignal
            && AgentSignalParser.Normalize(explicitSignal) is { } signal)
        {
            return signal;
        }

        if (HookPayload.FirstString(payload, "status", "state", "phase", "result", "outcome") is { } status
            && AgentSignalParser.Normalize(status) is { } statusSignal)
        {
            return statusSignal;
        }

        if (eventName is not null)
        {
            foreach (var pair in Events)
            {
                if (NormalizeEvent(pair.Key) == NormalizeEvent(eventName))
                {
                    return pair.Value;
                }
            }
        }

        return AgentSignal.Attention;
    }

    private static string NormalizeEvent(string value)
    {
        return new string(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}

internal sealed class CliException(string message) : Exception(message);
