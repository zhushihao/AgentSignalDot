namespace AgentSignalBar.Core;

public sealed record FloatingSignalText(
    string AgentName,
    string StatusText,
    string DetailText)
{
    public static FloatingSignalText Resolve(SignalSnapshot snapshot)
    {
        var identity = AgentIdentityBadge.Resolve(snapshot);
        var agentName = identity switch
        {
            AgentIdentity.Codex => "Codex",
            AgentIdentity.ClaudeCode => "Claude",
            _ => "Agent"
        };

        var displayState = snapshot.Aggregate.DisplayState();
        var (status, detail) = displayState switch
        {
            DisplayState.Ready => ("空闲", "待命"),
            DisplayState.Active => ("工作中", "处理中"),
            DisplayState.Completed => ("已完成", "完成"),
            DisplayState.NeedsReview => ("需查看", "有提醒"),
            DisplayState.Permission => ("等授权", "需确认"),
            DisplayState.Blocked => ("已阻塞", "出错"),
            DisplayState.Stale => ("状态旧", "需刷新"),
            DisplayState.Paused => ("已暂停", "监控关"),
            _ => ("空闲", "待命")
        };

        return new FloatingSignalText(agentName, status, detail);
    }
}
