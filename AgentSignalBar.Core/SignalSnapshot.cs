namespace AgentSignalBar.Core;

public sealed record SessionStatus(
    string SessionId,
    AgentSignal Signal,
    DateTimeOffset UpdatedAt,
    string? Agent,
    string? LastEvent);

public sealed record RecentSignalEvent(
    string Id,
    string SessionId,
    AgentSignal Signal,
    DateTimeOffset UpdatedAt,
    string? Agent,
    string? Event);

public sealed record SignalSnapshot(
    AgentSignal Aggregate,
    IReadOnlyList<SessionStatus> Sessions,
    IReadOnlyList<RecentSignalEvent> RecentEvents,
    string StateFilePath,
    DateTimeOffset? UpdatedAt);

public static class SignalStateDocumentSnapshotExtensions
{
    public static SignalSnapshot ToSnapshot(this SignalStateDocument document, string stateFilePath)
    {
        var sessions = document.Sessions
            .Select(pair => new SessionStatus(
                pair.Key,
                pair.Value.Signal,
                pair.Value.UpdatedAt,
                pair.Value.Agent,
                pair.Value.LastEvent))
            .OrderByDescending(session => session.Signal.DisplayState().Priority())
            .ThenByDescending(session => session.UpdatedAt)
            .ToArray();

        var recentEvents = document.Events
            .AsEnumerable()
            .Reverse()
            .Select(record => new RecentSignalEvent(
                record.Id,
                record.SessionId,
                record.Signal,
                record.UpdatedAt,
                record.Agent,
                record.Event))
            .ToArray();

        return new SignalSnapshot(
            document.AggregateSignal(),
            sessions,
            recentEvents,
            stateFilePath,
            document.UpdatedAt);
    }
}
