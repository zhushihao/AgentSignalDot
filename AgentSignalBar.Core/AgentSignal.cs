using System.Text.Json.Serialization;

namespace AgentSignalBar.Core;

[JsonConverter(typeof(JsonStringEnumMemberConverter<DisplayState>))]
public enum DisplayState
{
    Ready,
    Active,
    Completed,
    NeedsReview,
    Permission,
    Blocked,
    Stale,
    Paused
}

[JsonConverter(typeof(JsonStringEnumMemberConverter<AgentSignal>))]
public enum AgentSignal
{
    Idle,
    Thinking,
    Working,
    ToolDone,
    SubagentStart,
    SubagentStop,
    Attention,
    Notification,
    Done,
    Permission,
    PermissionRequest,
    Blocked,
    Failure,
    Error,
    Exception,
    MaxTokens,
    Stale,
    SessionStart,
    SessionEnd,
    TurnEnd,
    Off,
    Pause,
    Paused
}

public static class AgentSignalExtensions
{
    public static DisplayState DisplayState(this AgentSignal signal) => signal switch
    {
        AgentSignal.Idle or AgentSignal.SessionStart or AgentSignal.SessionEnd or AgentSignal.TurnEnd => Core.DisplayState.Ready,
        AgentSignal.Thinking or AgentSignal.Working or AgentSignal.ToolDone or AgentSignal.SubagentStart or AgentSignal.SubagentStop => Core.DisplayState.Active,
        AgentSignal.Done => Core.DisplayState.Completed,
        AgentSignal.Attention or AgentSignal.Notification => Core.DisplayState.NeedsReview,
        AgentSignal.Permission or AgentSignal.PermissionRequest => Core.DisplayState.Permission,
        AgentSignal.Blocked or AgentSignal.Failure or AgentSignal.Error or AgentSignal.Exception or AgentSignal.MaxTokens => Core.DisplayState.Blocked,
        AgentSignal.Stale => Core.DisplayState.Stale,
        AgentSignal.Off or AgentSignal.Pause or AgentSignal.Paused => Core.DisplayState.Paused,
        _ => Core.DisplayState.Ready
    };

    public static int Priority(this DisplayState state) => state switch
    {
        Core.DisplayState.Paused => 100,
        Core.DisplayState.Blocked => 90,
        Core.DisplayState.Permission => 80,
        Core.DisplayState.NeedsReview => 70,
        Core.DisplayState.Stale => 60,
        Core.DisplayState.Active => 50,
        Core.DisplayState.Completed => 40,
        _ => 0
    };

    public static AgentSignal NormalizedAggregateSignal(this AgentSignal signal) => signal.DisplayState() switch
    {
        Core.DisplayState.Ready => AgentSignal.Idle,
        Core.DisplayState.Active => signal,
        Core.DisplayState.Completed => AgentSignal.Done,
        Core.DisplayState.NeedsReview => AgentSignal.Attention,
        Core.DisplayState.Permission => AgentSignal.Permission,
        Core.DisplayState.Blocked => AgentSignal.Blocked,
        Core.DisplayState.Stale => AgentSignal.Stale,
        Core.DisplayState.Paused => AgentSignal.Off,
        _ => AgentSignal.Idle
    };

    public static string HumanAction(this DisplayState state) => state switch
    {
        Core.DisplayState.Ready or Core.DisplayState.Active or Core.DisplayState.Completed => "No action needed",
        Core.DisplayState.NeedsReview => "Review when you have a moment",
        Core.DisplayState.Permission or Core.DisplayState.Blocked => "Handle now",
        Core.DisplayState.Stale => "Confirm status",
        Core.DisplayState.Paused => "Monitoring paused",
        _ => "No action needed"
    };

    public static string DisplayName(this AgentSignal signal) => signal switch
    {
        AgentSignal.Idle => "Idle",
        AgentSignal.Thinking => "Thinking",
        AgentSignal.Working => "Working",
        AgentSignal.ToolDone => "Tool Done",
        AgentSignal.SubagentStart => "Subagent Started",
        AgentSignal.SubagentStop => "Subagent Done",
        AgentSignal.Attention => "Needs Review",
        AgentSignal.Notification => "Notification",
        AgentSignal.Done => "Done",
        AgentSignal.Permission => "Permission",
        AgentSignal.PermissionRequest => "Permission Required",
        AgentSignal.Blocked => "Blocked",
        AgentSignal.Failure => "Failure",
        AgentSignal.Error => "Error",
        AgentSignal.Exception => "Exception",
        AgentSignal.MaxTokens => "Max Tokens",
        AgentSignal.Stale => "Stale",
        AgentSignal.SessionStart => "Session Started",
        AgentSignal.SessionEnd => "Session Ended",
        AgentSignal.TurnEnd => "Turn Ended",
        AgentSignal.Off => "Off",
        AgentSignal.Pause or AgentSignal.Paused => "Paused",
        _ => signal.ToString()
    };

    public static string Summary(this AgentSignal signal) => signal switch
    {
        AgentSignal.Idle or AgentSignal.SessionStart or AgentSignal.SessionEnd or AgentSignal.TurnEnd => "Agent is idle.",
        AgentSignal.Thinking => "Agent received work and is thinking.",
        AgentSignal.Working => "Agent is reading, writing, running tools, or testing.",
        AgentSignal.ToolDone => "A tool step completed while the agent remains in the workflow.",
        AgentSignal.SubagentStart => "A subagent started running.",
        AgentSignal.SubagentStop => "A subagent finished and the main workflow may continue.",
        AgentSignal.Attention => "Agent needs a review or follow-up.",
        AgentSignal.Notification => "Agent emitted a notification that should be checked.",
        AgentSignal.Done => "Task completed.",
        AgentSignal.Permission => "Agent is requesting permission or explicit approval.",
        AgentSignal.PermissionRequest => "Agent is waiting for user approval.",
        AgentSignal.Blocked => "Agent hit a failure, blocker, or cannot continue.",
        AgentSignal.Failure => "Agent or tool reported failure.",
        AgentSignal.Error => "Agent or tool reported an error.",
        AgentSignal.Exception => "Agent or tool reported an exception.",
        AgentSignal.MaxTokens => "Agent cannot continue because of context or token limits.",
        AgentSignal.Stale => "State file is expired, corrupt, or not trustworthy.",
        AgentSignal.Off or AgentSignal.Pause or AgentSignal.Paused => "Monitoring is paused.",
        _ => "Agent status updated."
    };
}

public static class AgentSignalParser
{
    private static readonly Dictionary<string, AgentSignal> Aliases = new()
    {
        ["tooluse"] = AgentSignal.Working,
        ["tool_use"] = AgentSignal.Working,
        ["pre_tool_use"] = AgentSignal.Working,
        ["post_tool_use"] = AgentSignal.ToolDone,
        ["subagentstart"] = AgentSignal.SubagentStart,
        ["subagentstop"] = AgentSignal.SubagentStop,
        ["permissionrequest"] = AgentSignal.PermissionRequest,
        ["failed"] = AgentSignal.Failure,
        ["maxtokens"] = AgentSignal.MaxTokens,
        ["max_tokens"] = AgentSignal.MaxTokens
    };

    private static readonly Dictionary<string, AgentSignal> RawValues = Enum.GetValues<AgentSignal>()
        .ToDictionary(ToRawValue, signal => signal);

    public static AgentSignal? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim()
            .ToLowerInvariant()
            .Replace("-", "_")
            .Replace(" ", "_");

        return Aliases.TryGetValue(normalized, out var alias)
            ? alias
            : RawValues.TryGetValue(normalized, out var signal)
                ? signal
                : null;
    }

    public static string ToRawValue(AgentSignal signal) => signal switch
    {
        AgentSignal.ToolDone => "tool_done",
        AgentSignal.SubagentStart => "subagent_start",
        AgentSignal.SubagentStop => "subagent_stop",
        AgentSignal.PermissionRequest => "permission_request",
        AgentSignal.MaxTokens => "max_tokens",
        AgentSignal.SessionStart => "session_start",
        AgentSignal.SessionEnd => "session_end",
        AgentSignal.TurnEnd => "turn_end",
        _ => signal.ToString().ToLowerInvariant()
    };

    public static string ToRawValue(DisplayState state) => state switch
    {
        Core.DisplayState.NeedsReview => "needs_review",
        _ => state.ToString().ToLowerInvariant()
    };
}
