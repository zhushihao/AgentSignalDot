namespace AgentSignalBar.Core;

public sealed record ManualSignalTestCase(
    string ButtonText,
    AgentSignal Signal,
    DisplayState ExpectedDisplayState)
{
    public const string SessionId = "manual";
    public const string Agent = "manual";
    public const string Event = "SignalTest";

    public static IReadOnlyList<ManualSignalTestCase> All { get; } =
    [
        new("测试待命", AgentSignal.Idle, DisplayState.Ready),
        new("测试工作中", AgentSignal.Working, DisplayState.Active),
        new("测试完成", AgentSignal.Done, DisplayState.Completed),
        new("测试需要查看", AgentSignal.Attention, DisplayState.NeedsReview),
        new("测试授权", AgentSignal.PermissionRequest, DisplayState.Permission),
        new("测试阻塞", AgentSignal.Blocked, DisplayState.Blocked),
        new("测试状态过期", AgentSignal.Stale, DisplayState.Stale),
        new("测试暂停", AgentSignal.Paused, DisplayState.Paused)
    ];
}
