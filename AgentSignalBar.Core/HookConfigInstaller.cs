using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentSignalBar.Core;

public sealed record HookInstallPreview(
    string TargetName,
    string Path,
    bool Existed,
    bool Changed,
    string Json);

public static class HookConfigInstaller
{
    public static readonly string[] CodeBuddyEvents =
    [
        "SessionStart",
        "UserPromptSubmit",
        "PreToolUse",
        "PostToolUse",
        "PostToolUseFailure",
        "SubagentStart",
        "SubagentStop",
        "PermissionRequest",
        "PermissionDenied",
        "Notification",
        "Stop",
        "StopFailure",
        "SessionEnd"
    ];

    public static readonly string[] CodexEvents =
    [
        "SessionStart",
        "UserPromptSubmit",
        "PreToolUse",
        "PostToolUse",
        "PermissionRequest",
        "Stop"
    ];

    public static readonly string[] ClaudeEvents =
    [
        "ConfigChange",
        "CwdChanged",
        "Elicitation",
        "ElicitationResult",
        "FileChanged",
        "InstructionsLoaded",
        "SessionStart",
        "TaskCreated",
        "TaskCompleted",
        "TeammateIdle",
        "UserPromptExpansion",
        "UserPromptSubmit",
        "PreToolUse",
        "PostToolBatch",
        "PostToolUse",
        "PostToolUseFailure",
        "PreCompact",
        "PostCompact",
        "SubagentStart",
        "SubagentStop",
        "PermissionRequest",
        "PermissionDenied",
        "Notification",
        "Stop",
        "StopFailure",
        "WorktreeCreate",
        "WorktreeRemove",
        "SessionEnd"
    ];

    public static HookInstallPreview PreviewClaudeInstall(string homeDirectory, string agentSignalCliPath)
    {
        var path = Path.Combine(homeDirectory, ".claude", "settings.json");
        return PreviewInstall(
            "Claude Code",
            path,
            agentSignalCliPath,
            ClaudeEvents,
            passEventArgument: true,
            matcher: "");
    }

    public static HookInstallPreview PreviewCodexInstall(string configPath, string agentSignalCliPath)
    {
        return PreviewInstall(
            "Codex",
            configPath,
            agentSignalCliPath,
            CodexEvents,
            passEventArgument: true,
            matcher: "*");
    }

    public static HookInstallPreview InstallClaude(string homeDirectory, string agentSignalCliPath)
    {
        var preview = PreviewClaudeInstall(homeDirectory, agentSignalCliPath);
        WriteIfChanged(preview);
        return preview;
    }

    public static HookInstallPreview InstallCodex(string configPath, string agentSignalCliPath)
    {
        var preview = PreviewCodexInstall(configPath, agentSignalCliPath);
        WriteIfChanged(preview);
        return preview;
    }

    public static HookInstallPreview PreviewCodeBuddyInstall(string homeDirectory, string agentSignalCliPath)
    {
        var path = Path.Combine(homeDirectory, ".codebuddy", "settings.json");
        return PreviewInstall(
            "CodeBuddy",
            path,
            agentSignalCliPath,
            CodeBuddyEvents,
            passEventArgument: true,
            matcher: "");
    }

    public static HookInstallPreview InstallCodeBuddy(string homeDirectory, string agentSignalCliPath)
    {
        var preview = PreviewCodeBuddyInstall(homeDirectory, agentSignalCliPath);
        WriteIfChanged(preview);
        return preview;
    }

    private static HookInstallPreview PreviewInstall(
        string targetName,
        string path,
        string agentSignalCliPath,
        IEnumerable<string> events,
        bool passEventArgument,
        string matcher)
    {
        var (root, existed) = LoadRoot(path);
        var hooks = root["hooks"] as JsonObject;
        if (hooks is null)
        {
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }

        var changed = !existed || root["hooks"] is null;
        foreach (var eventName in events)
        {
            var blocks = hooks[eventName] as JsonArray;
            if (blocks is null)
            {
                blocks = new JsonArray();
                hooks[eventName] = blocks;
                changed = true;
            }

            var command = HookCommand(agentSignalCliPath, passEventArgument ? eventName : null, targetName);
            if (RemoveStaleAgentSignalHookCommands(blocks, command, targetName))
            {
                changed = true;
            }

            if (EventContainsCommand(blocks, command))
            {
                continue;
            }

            blocks.Add(HookBlock(command, eventName, matcher));
            changed = true;
        }

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        return new HookInstallPreview(targetName, path, existed, changed, json);
    }

    private static bool RemoveStaleAgentSignalHookCommands(JsonArray blocks, string currentCommand, string targetName)
    {
        var changed = false;
        for (var blockIndex = blocks.Count - 1; blockIndex >= 0; blockIndex--)
        {
            if (blocks[blockIndex] is not JsonObject block || block["hooks"] is not JsonArray hooks)
            {
                continue;
            }

            for (var hookIndex = hooks.Count - 1; hookIndex >= 0; hookIndex--)
            {
                if (hooks[hookIndex] is JsonObject hook
                    && hook["command"]?.GetValue<string>() is { } existingCommand
                    && existingCommand != currentCommand
                    && IsAgentSignalHookCommand(existingCommand, targetName))
                {
                    hooks.RemoveAt(hookIndex);
                    changed = true;
                }
            }

            if (hooks.Count == 0)
            {
                blocks.RemoveAt(blockIndex);
            }
        }

        return changed;
    }

    public static bool IsAgentSignalHookCommand(string command, string targetName)
    {
        var normalized = command.ToLowerInvariant();
        var verb = targetName switch
        {
            "Claude Code" => "claude-hook",
            "CodeBuddy" => "codebuddy-hook",
            _ => "codex-hook"
        };
        return normalized.Contains("agent-signal", StringComparison.Ordinal)
            && normalized.Contains(verb, StringComparison.Ordinal);
    }

    private static void WriteIfChanged(HookInstallPreview preview)
    {
        if (!preview.Changed)
        {
            return;
        }

        var directory = Path.GetDirectoryName(preview.Path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(preview.Path))
        {
            var backup = preview.Path + ".bak-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Copy(preview.Path, backup, overwrite: false);
        }

        File.WriteAllText(preview.Path, preview.Json);
    }

    private static (JsonObject Root, bool Existed) LoadRoot(string path)
    {
        if (!File.Exists(path))
        {
            return (new JsonObject(), false);
        }

        var node = JsonNode.Parse(File.ReadAllText(path));
        if (node is not JsonObject root)
        {
            throw new InvalidOperationException($"{path} must contain a JSON object.");
        }

        return (root, true);
    }

    private static JsonObject HookBlock(string command, string eventName, string matcher)
    {
        var timeout = eventName == "PermissionRequest" ? 10 : 5;
        return new JsonObject
        {
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = command,
                    ["timeout"] = timeout
                }
            },
            ["matcher"] = matcher
        };
    }

    public static string HookCommand(string agentSignalCliPath, string? eventName, string targetName)
    {
        var verb = targetName switch
        {
            "Claude Code" => "claude-hook",
            "CodeBuddy" => "codebuddy-hook",
            _ => "codex-hook"
        };
        var command = $"& {QuotePowerShellArgument(agentSignalCliPath)} {verb}";
        if (!string.IsNullOrWhiteSpace(eventName))
        {
            command += " " + eventName;
        }

        return "powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command " + QuoteWindowsArgument(command);
    }

    private static string QuotePowerShellArgument(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string QuoteWindowsArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static bool EventContainsCommand(JsonArray blocks, string command)
    {
        foreach (var blockNode in blocks)
        {
            if (blockNode is not JsonObject block || block["hooks"] is not JsonArray hooks)
            {
                continue;
            }

            foreach (var hookNode in hooks)
            {
                if (hookNode is JsonObject hook && hook["command"]?.GetValue<string>() == command)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
