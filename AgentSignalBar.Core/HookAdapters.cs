namespace AgentSignalBar.Core;

public static class CodeBuddyHookAdapter
{
    // WorkBuddy / CodeBuddy Code hooks
    private static readonly Dictionary<string, AgentSignal> Events = HookAdapterHelpers.NormalizedEventMap(new()
    {
        ["SessionStart"] = AgentSignal.SessionStart,
        ["UserPromptSubmit"] = AgentSignal.Thinking,
        ["PreToolUse"] = AgentSignal.Working,
        ["PostToolUse"] = AgentSignal.ToolDone,
        ["PostToolUseFailure"] = AgentSignal.Blocked,
        ["SubagentStart"] = AgentSignal.SubagentStart,
        ["SubagentStop"] = AgentSignal.SubagentStop,
        ["PermissionRequest"] = AgentSignal.PermissionRequest,
        ["PermissionDenied"] = AgentSignal.Blocked,
        ["Notification"] = AgentSignal.Notification,
        ["Stop"] = AgentSignal.Done,
        ["StopFailure"] = AgentSignal.Blocked,
        ["SessionEnd"] = AgentSignal.SessionEnd
    });

    public static AgentSignal ChooseSignal(string? eventName, IReadOnlyDictionary<string, object?> payload)
    {
        if (HookAdapterHelpers.FirstString(payload, "signal", "signal_name", "lamp_signal") is { } explicitSignal
            && AgentSignalParser.Normalize(explicitSignal) is { } normalizedSignal)
        {
            return normalizedSignal;
        }

        if (HookAdapterHelpers.FirstString(payload, "status", "state") is { } status)
        {
            if (AgentSignalParser.Normalize(status) is { } statusSignal)
            {
                return statusSignal;
            }

            if (HookAdapterHelpers.IsFailureWord(status))
            {
                return AgentSignal.Blocked;
            }
        }

        if (HookAdapterHelpers.ContainsFailureMarker(payload))
        {
            return AgentSignal.Blocked;
        }

        var resolvedEvent = eventName
            ?? HookAdapterHelpers.FirstString(payload, "hook_event_name", "event_name", "event", "hook", "type")
            ?? "Stop";

        return HookAdapterHelpers.SignalFor(resolvedEvent, Events) ?? AgentSignal.Attention;
    }
}

public static class CodexHookAdapter
{
    private static readonly Dictionary<string, AgentSignal> Events = HookAdapterHelpers.NormalizedEventMap(new()
    {
        ["SessionStart"] = AgentSignal.SessionStart,
        ["UserPromptSubmit"] = AgentSignal.Thinking,
        ["PreToolUse"] = AgentSignal.Working,
        ["PostToolUse"] = AgentSignal.ToolDone,
        ["PermissionRequest"] = AgentSignal.PermissionRequest,
        ["Stop"] = AgentSignal.Done,
        ["SessionEnd"] = AgentSignal.SessionEnd
    });

    public static AgentSignal ChooseSignal(string? eventName, IReadOnlyDictionary<string, object?> payload)
    {
        if (HookAdapterHelpers.FirstString(payload, "signal", "signal_name", "lamp_signal") is { } explicitSignal
            && AgentSignalParser.Normalize(explicitSignal) is { } normalizedSignal)
        {
            return normalizedSignal;
        }

        if (HookAdapterHelpers.FirstString(payload, "status", "state") is { } status)
        {
            if (AgentSignalParser.Normalize(status) is { } statusSignal)
            {
                return statusSignal;
            }

            if (HookAdapterHelpers.IsFailureWord(status))
            {
                return AgentSignal.Blocked;
            }
        }

        if (HookAdapterHelpers.ContainsFailureMarker(payload))
        {
            return AgentSignal.Blocked;
        }

        var resolvedEvent = eventName
            ?? HookAdapterHelpers.FirstString(payload, "hook_event_name", "event_name", "event", "hook", "type")
            ?? "Stop";

        return HookAdapterHelpers.SignalFor(resolvedEvent, Events) ?? AgentSignal.Attention;
    }
}

public static class ClaudeHookAdapter
{
    private static readonly Dictionary<string, AgentSignal> Events = HookAdapterHelpers.NormalizedEventMap(new()
    {
        ["ConfigChange"] = AgentSignal.Attention,
        ["CwdChanged"] = AgentSignal.Attention,
        ["Elicitation"] = AgentSignal.Attention,
        ["ElicitationResult"] = AgentSignal.Working,
        ["FileChanged"] = AgentSignal.Attention,
        ["InstructionsLoaded"] = AgentSignal.Attention,
        ["SessionStart"] = AgentSignal.SessionStart,
        ["TaskCreated"] = AgentSignal.SubagentStart,
        ["TaskCompleted"] = AgentSignal.SubagentStop,
        ["TeammateIdle"] = AgentSignal.Idle,
        ["UserPromptExpansion"] = AgentSignal.Thinking,
        ["UserPromptSubmit"] = AgentSignal.Thinking,
        ["PreToolUse"] = AgentSignal.Working,
        ["PostToolBatch"] = AgentSignal.ToolDone,
        ["PostToolUse"] = AgentSignal.ToolDone,
        ["PostToolUseFailure"] = AgentSignal.Blocked,
        ["PreCompact"] = AgentSignal.Working,
        ["PostCompact"] = AgentSignal.ToolDone,
        ["SubagentStart"] = AgentSignal.SubagentStart,
        ["SubagentStop"] = AgentSignal.SubagentStop,
        ["PermissionRequest"] = AgentSignal.PermissionRequest,
        ["PermissionDenied"] = AgentSignal.Blocked,
        ["Notification"] = AgentSignal.Notification,
        ["Stop"] = AgentSignal.Done,
        ["StopFailure"] = AgentSignal.Blocked,
        ["WorktreeCreate"] = AgentSignal.Working,
        ["WorktreeRemove"] = AgentSignal.Attention,
        ["SessionEnd"] = AgentSignal.SessionEnd
    });

    public static AgentSignal ChooseSignal(string? eventName, IReadOnlyDictionary<string, object?> payload)
    {
        if (HookAdapterHelpers.FirstString(payload, "signal", "signal_name", "lamp_signal") is { } explicitSignal
            && AgentSignalParser.Normalize(explicitSignal) is { } normalizedSignal)
        {
            return normalizedSignal;
        }

        if (HookAdapterHelpers.FirstString(payload, "status", "state") is { } status)
        {
            if (AgentSignalParser.Normalize(status) is { } statusSignal)
            {
                return statusSignal;
            }

            if (HookAdapterHelpers.IsFailureWord(status))
            {
                return AgentSignal.Blocked;
            }
        }

        if (HookAdapterHelpers.ContainsFailureMarker(payload))
        {
            return AgentSignal.Blocked;
        }

        var resolvedEvent = eventName
            ?? EventName(payload)
            ?? "Stop";
        if (HookAdapterHelpers.NormalizeEventName(resolvedEvent) == HookAdapterHelpers.NormalizeEventName("Stop")
            && HookAdapterHelpers.FirstString(payload, "stop_reason") is { } stopReason)
        {
            var normalizedReason = HookAdapterHelpers.NormalizeEventName(stopReason);
            if (normalizedReason == HookAdapterHelpers.NormalizeEventName("max_tokens"))
            {
                return AgentSignal.MaxTokens;
            }

            if (HookAdapterHelpers.IsFailureWord(normalizedReason))
            {
                return AgentSignal.Error;
            }
        }

        return HookAdapterHelpers.SignalFor(resolvedEvent, Events) ?? AgentSignal.Attention;
    }

    public static string? EventName(IReadOnlyDictionary<string, object?> payload)
    {
        return HookAdapterHelpers.FirstString(payload, "hook_event_name", "event_name", "event", "hook", "type", "name");
    }
}

internal static class HookAdapterHelpers
{
    public static Dictionary<string, AgentSignal> NormalizedEventMap(Dictionary<string, AgentSignal> events)
    {
        return events.ToDictionary(pair => NormalizeEventName(pair.Key), pair => pair.Value);
    }

    public static AgentSignal? SignalFor(string? eventName, IReadOnlyDictionary<string, AgentSignal> events)
    {
        return eventName is null
            ? null
            : events.TryGetValue(NormalizeEventName(eventName), out var signal)
                ? signal
                : null;
    }

    public static string NormalizeEventName(string value)
    {
        return new string(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
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

        var normalizedKeys = keys.Select(NormalizeEventName).ToHashSet();
        foreach (var pair in payload)
        {
            if (normalizedKeys.Contains(NormalizeEventName(pair.Key))
                && ScalarString(pair.Value) is { } scalar
                && !string.IsNullOrWhiteSpace(scalar))
            {
                return scalar;
            }
        }

        return null;
    }

    public static bool ContainsFailureMarker(object? value)
    {
        var failureKeys = new[]
        {
            "error", "failure", "exception", "error_type", "error_message", "failure_reason", "exit_status", "tool_error"
        }.Select(NormalizeEventName).ToHashSet();

        if (value is IReadOnlyDictionary<string, object?> objectValue)
        {
            foreach (var pair in objectValue)
            {
                var normalizedKey = NormalizeEventName(pair.Key);
                if ((failureKeys.Contains(normalizedKey) || IsFailureWord(normalizedKey))
                    && FailureValue(pair.Value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsFailureWord(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Contains("error", StringComparison.Ordinal)
            || normalized.Contains("failed", StringComparison.Ordinal)
            || normalized.Contains("failure", StringComparison.Ordinal)
            || normalized.Contains("exception", StringComparison.Ordinal);
    }

    private static bool FailureValue(object? value)
    {
        return value switch
        {
            null => false,
            bool boolean => boolean,
            int integer => integer != 0,
            long integer => integer != 0,
            double number => Math.Abs(number) > double.Epsilon,
            string text => !string.IsNullOrWhiteSpace(text)
                && !new[] { "0", "false", "no", "none", "null", "success", "ok" }.Contains(text.Trim().ToLowerInvariant()),
            _ => true
        };
    }

    private static string? ScalarString(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            bool boolean => boolean ? "true" : "false",
            int integer => integer.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long integer => integer.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
    }
}
