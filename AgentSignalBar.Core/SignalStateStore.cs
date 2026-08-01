using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentSignalBar.Core;

public sealed class SignalStateStore
{
    private static readonly TimeSpan DuplicateEventWindow = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ThinkingTtl = TimeSpan.FromMinutes(5);
    private readonly JsonSerializerOptions jsonOptions;
    private readonly string mutexName;

    public string StateFilePath { get; }
    public TimeSpan SessionTtl { get; }
    public TimeSpan CompletedTtl { get; }
    public int EventLimit { get; }

    public SignalStateStore(
        string? stateFilePath = null,
        TimeSpan? sessionTtl = null,
        TimeSpan? completedTtl = null,
        int eventLimit = 50)
    {
        StateFilePath = stateFilePath ?? StatePath.DefaultStateFilePath();
        SessionTtl = sessionTtl ?? EnvironmentTimeSpan("SIGNAL_LIGHT_SESSION_TTL_SECONDS", TimeSpan.FromMinutes(30));
        CompletedTtl = completedTtl
            ?? EnvironmentTimeSpan(
                "AGENT_SIGNAL_LIGHT_COMPLETED_TTL_SECONDS",
                EnvironmentTimeSpan("SIGNAL_LIGHT_COMPLETED_TTL_SECONDS", TimeSpan.FromSeconds(30)));
        EventLimit = EnvironmentInt("AGENT_SIGNAL_LIGHT_EVENT_LIMIT", eventLimit);
        jsonOptions = JsonOptions.CreateIndented();
        mutexName = "Local\\AgentSignalBarState-" + Hash(StateFilePath.ToLowerInvariant());
    }

    public SignalSnapshot ReadSnapshot(DateTimeOffset? now = null)
    {
        return WithLock(() =>
        {
            var document = ReadDocument();
            if (document is null)
            {
                return new SignalStateDocument
                {
                    Aggregate = AgentSignal.Stale,
                    UpdatedAt = DateTimeOffset.UtcNow
                }.ToSnapshot(StateFilePath);
            }

            var originalJson = JsonSerializer.Serialize(document, jsonOptions);
            PrepareSnapshotDocument(document, now ?? DateTimeOffset.UtcNow);

            if (JsonSerializer.Serialize(document, jsonOptions) != originalJson)
            {
                WriteDocument(document);
            }

            return document.ToSnapshot(StateFilePath);
        });
    }

    public SignalSnapshot SetManualSignal(AgentSignal signal)
    {
        return WithLock(() =>
        {
            var now = DateTimeOffset.UtcNow;
            var document = ReadDocument() ?? new SignalStateDocument();
            PruneRuntimeSessions(document, now);
            var resolved = signal is AgentSignal.SessionStart or AgentSignal.SessionEnd or AgentSignal.TurnEnd
                ? AgentSignal.Idle
                : signal;

            switch (resolved.DisplayState())
            {
                case DisplayState.Ready:
                    document.Sessions.Clear();
                    document.Aggregate = AgentSignal.Idle;
                    break;
                case DisplayState.Paused:
                    document.Sessions.Clear();
                    document.Aggregate = resolved.NormalizedAggregateSignal();
                    break;
                default:
                    document.Sessions["manual"] = new SessionRecord("manual", resolved, "ManualSet", now);
                    document.Aggregate = document.AggregateSignal();
                    break;
            }

            AppendEvent(document, "manual", "manual", resolved, "ManualSet", now);
            document.UpdatedAt = now;
            WriteDocument(document);
            return document.ToSnapshot(StateFilePath);
        });
    }

    public SignalSnapshot ClearWarnings()
    {
        return WithLock(() =>
        {
            var now = DateTimeOffset.UtcNow;
            var document = ReadDocument() ?? new SignalStateDocument();
            PruneRuntimeSessions(document, now);
            document.Sessions = document.Sessions
                .Where(pair => !ShouldClearWarning(pair.Value.Signal))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            document.Aggregate = document.Sessions.Count == 0
                ? document.Aggregate?.DisplayState() == DisplayState.Paused ? document.Aggregate : AgentSignal.Idle
                : document.AggregateSignal();
            AppendEvent(document, "manual", "manual", AgentSignal.Idle, "ClearWarning", now);
            document.UpdatedAt = now;
            WriteDocument(document);
            return document.ToSnapshot(StateFilePath);
        });
    }

    public SignalSnapshot ClearSessions() => SetManualSignal(AgentSignal.Idle);

    // Removes every session owned by the given agent and recomputes the
    // aggregate. Used by the "track WorkBuddy / CodeBuddy" toggle so turning
    // tracking off immediately clears the stale dot instead of waiting for TTL.
    public SignalSnapshot ClearAgentSessions(string agent)
    {
        return WithLock(() =>
        {
            var now = DateTimeOffset.UtcNow;
            var document = ReadDocument() ?? new SignalStateDocument();
            PruneRuntimeSessions(document, now);
            document.Sessions = document.Sessions
                .Where(pair => pair.Value.Agent != agent)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            document.Aggregate = document.Sessions.Count == 0 && document.Aggregate?.DisplayState() != DisplayState.Paused
                ? AgentSignal.Idle
                : document.AggregateSignal();
            AppendEvent(document, "manual", "manual", AgentSignal.Idle, $"ClearAgent:{agent}", now);
            document.UpdatedAt = now;
            WriteDocument(document);
            return document.ToSnapshot(StateFilePath);
        });
    }

    public SignalSnapshot ApplySessionSignal(
        AgentSignal signal,
        string sessionId,
        string? agent = null,
        string? lastEvent = null,
        DateTimeOffset? updatedAt = null)
    {
        return WithLock(() =>
        {
            var now = DateTimeOffset.UtcNow;
            var eventDate = updatedAt ?? now;
            var document = ReadDocument() ?? new SignalStateDocument();

            if (document.Sessions.TryGetValue(sessionId, out var existing) && existing.UpdatedAt > eventDate)
            {
                return document.ToSnapshot(StateFilePath);
            }

            PruneRuntimeSessions(document, now);

            switch (signal)
            {
                case AgentSignal.Off:
                case AgentSignal.Pause:
                case AgentSignal.Paused:
                    document.Sessions.Clear();
                    document.Aggregate = AgentSignal.Off;
                    break;
                case AgentSignal.SessionEnd:
                    RemoveOrdinarySessionsForAgent(document, sessionId, agent, PreserveAgainstSessionEnd);
                    if (!document.Sessions.TryGetValue(sessionId, out var sessionEndCurrent)
                        || !PreserveAgainstSessionEnd(sessionEndCurrent.Signal))
                    {
                        document.Sessions.Remove(sessionId);
                    }
                    document.Aggregate = document.Sessions.Count == 0 && document.Aggregate?.DisplayState() != DisplayState.Paused
                        ? AgentSignal.Idle
                        : document.AggregateSignal();
                    break;
                case AgentSignal.TurnEnd:
                    if (!document.Sessions.TryGetValue(sessionId, out var turnEndCurrent)
                        || !BlocksTurnEndClear(turnEndCurrent.Signal))
                    {
                        document.Sessions.Remove(sessionId);
                    }
                    document.Aggregate = document.Sessions.Count == 0 && document.Aggregate?.DisplayState() != DisplayState.Paused
                        ? AgentSignal.Idle
                        : document.AggregateSignal();
                    break;
                case AgentSignal.Idle:
                case AgentSignal.SessionStart:
                    if (!document.Sessions.TryGetValue(sessionId, out var readyCurrent)
                        || !PreserveAgainstReadySignal(readyCurrent.Signal))
                    {
                        document.Sessions.Remove(sessionId);
                    }
                    document.Aggregate = document.Sessions.Count == 0 && document.Aggregate?.DisplayState() != DisplayState.Paused
                        ? AgentSignal.Idle
                        : document.AggregateSignal();
                    break;
                case AgentSignal.Done:
                    RemoveOrdinarySessionsForAgent(document, sessionId, agent, PreserveAgainstCompletedSignal);
                    if (!document.Sessions.TryGetValue(sessionId, out var doneCurrent)
                        || !PreserveAgainstCompletedSignal(doneCurrent.Signal))
                    {
                        document.Sessions[sessionId] = new SessionRecord(agent, signal, lastEvent, eventDate);
                    }
                    document.Aggregate = document.AggregateSignal();
                    break;
                default:
                    document.Sessions[sessionId] = new SessionRecord(agent, signal, lastEvent, eventDate);
                    document.Aggregate = document.AggregateSignal();
                    break;
            }

            AppendEvent(document, sessionId, agent, signal, lastEvent, eventDate);
            document.UpdatedAt = eventDate;
            WriteDocument(document);
            return document.ToSnapshot(StateFilePath);
        });
    }

    private static void RemoveOrdinarySessionsForAgent(
        SignalStateDocument document,
        string endingSessionId,
        string? agent,
        Func<AgentSignal, bool> preserve)
    {
        if (!IsClaudeCodeAgent(agent) && !IsCodexAgent(agent))
        {
            return;
        }

        var endingSessionIsScoped = !IsGlobalSessionId(endingSessionId, agent);
        var endingConversationId = ConversationId(endingSessionId);
        document.Sessions = document.Sessions
            .Where(pair =>
            {
                if (endingSessionIsScoped && !IsSameConversation(pair.Key, endingSessionId, endingConversationId, agent))
                {
                    return true;
                }

                if (!IsSameAgentFamily(pair.Value.Agent, agent))
                {
                    return true;
                }

                return preserve(pair.Value.Signal);
            })
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private void PrepareSnapshotDocument(SignalStateDocument document, DateTimeOffset now)
    {
        var prune = PruneRuntimeSessions(document, now);
        var removedReadySessions = RemoveReadySessions(document);
        CompactEventHistory(document);
        if ((prune.HadSessionsBeforePrune || removedReadySessions) && document.Sessions.Count == 0 && document.Aggregate?.DisplayState() != DisplayState.Paused)
        {
            document.Aggregate = prune.RemovedNonCompletedSession ? AgentSignal.Stale : AgentSignal.Idle;
            document.UpdatedAt = now;
        }
        else if (document.Sessions.Count > 0)
        {
            document.Aggregate = document.AggregateSignal();
        }
    }

    private RuntimePruneResult PruneRuntimeSessions(SignalStateDocument document, DateTimeOffset now)
    {
        var previous = document.Sessions;
        var removedNonCompleted = false;
        document.Sessions = previous
            .Where(pair =>
            {
                var ttl = RuntimeTtl(pair.Value.Signal);
                var keep = now - pair.Value.UpdatedAt <= ttl;
                if (!keep && ShouldExpiredSessionMarkStateStale(pair.Value.Signal))
                {
                    removedNonCompleted = true;
                }
                return keep;
            })
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return new RuntimePruneResult(previous.Count > 0, removedNonCompleted);
    }

    private static bool RemoveReadySessions(SignalStateDocument document)
    {
        var previousCount = document.Sessions.Count;
        document.Sessions = document.Sessions
            .Where(pair => pair.Value.Signal.DisplayState() != DisplayState.Ready)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        return document.Sessions.Count != previousCount;
    }

    private TimeSpan RuntimeTtl(AgentSignal signal)
    {
        return signal switch
        {
            AgentSignal.Done or AgentSignal.ToolDone or AgentSignal.SubagentStop => CompletedTtl,
            AgentSignal.Thinking => ThinkingTtl,
            _ => SessionTtl
        };
    }

    private static bool ShouldExpiredSessionMarkStateStale(AgentSignal signal)
    {
        return signal is not (AgentSignal.Done or AgentSignal.ToolDone or AgentSignal.SubagentStop
            or AgentSignal.Thinking or AgentSignal.Idle or AgentSignal.SessionStart or AgentSignal.SessionEnd or AgentSignal.TurnEnd)
            && signal.DisplayState() != DisplayState.Completed;
    }

    private static bool PreserveAgainstSessionEnd(AgentSignal signal)
    {
        return signal.DisplayState() is DisplayState.Completed or DisplayState.NeedsReview or DisplayState.Permission
            or DisplayState.Blocked or DisplayState.Stale or DisplayState.Paused;
    }

    private static bool PreserveAgainstCompletedSignal(AgentSignal signal)
    {
        return signal.DisplayState() is DisplayState.NeedsReview or DisplayState.Permission
            or DisplayState.Blocked or DisplayState.Stale or DisplayState.Paused;
    }

    private static bool PreserveAgainstReadySignal(AgentSignal signal)
    {
        return signal.DisplayState() is DisplayState.NeedsReview or DisplayState.Permission
            or DisplayState.Blocked or DisplayState.Stale or DisplayState.Paused;
    }

    private static bool BlocksTurnEndClear(AgentSignal signal)
    {
        return signal.DisplayState() is DisplayState.Permission or DisplayState.Blocked;
    }

    private static bool ShouldClearWarning(AgentSignal signal)
    {
        return signal.DisplayState() is DisplayState.Blocked or DisplayState.Permission or DisplayState.NeedsReview or DisplayState.Stale;
    }

    private static bool IsClaudeCodeAgent(string? agent)
    {
        return NormalizeAgent(agent) is "claude-code" or "claudecode" or "claude";
    }

    private static bool IsCodexAgent(string? agent)
    {
        var normalized = NormalizeAgent(agent);
        return normalized is not null
            && (normalized == "codex"
                || normalized.StartsWith("codex-", StringComparison.Ordinal)
                || normalized.StartsWith("codex", StringComparison.Ordinal));
    }

    private static bool IsSameAgent(string? left, string? right)
    {
        var normalizedLeft = NormalizeAgent(left);
        var normalizedRight = NormalizeAgent(right);
        return normalizedLeft is not null && normalizedRight is not null && normalizedLeft == normalizedRight;
    }

    private static bool IsSameAgentFamily(string? left, string? right)
    {
        return IsSameAgent(left, right)
            || (IsClaudeCodeAgent(left) && IsClaudeCodeAgent(right))
            || (IsCodexAgent(left) && IsCodexAgent(right));
    }

    private static bool IsSameConversation(string candidateSessionId, string endingSessionId, string endingConversationId, string? agent)
    {
        if (string.Equals(candidateSessionId, endingSessionId, StringComparison.Ordinal))
        {
            return true;
        }

        if (IsCodexAgent(agent))
        {
            return string.Equals(ConversationId(candidateSessionId), endingConversationId, StringComparison.Ordinal);
        }

        return false;
    }

    private static string ConversationId(string sessionId)
    {
        var separator = sessionId.IndexOf(':', StringComparison.Ordinal);
        return separator >= 0 && separator + 1 < sessionId.Length
            ? sessionId[(separator + 1)..]
            : sessionId;
    }

    private static bool IsGlobalSessionId(string sessionId, string? agent)
    {
        var normalizedSession = NormalizeAgent(sessionId);
        var normalizedAgent = NormalizeAgent(agent);
        return normalizedSession is "global" or "claude-global" or "claudeglobal"
            || (normalizedAgent is not null && normalizedSession == normalizedAgent + "global");
    }

    private static string? NormalizeAgent(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant().Replace("_", "-", StringComparison.Ordinal).Replace(" ", "-", StringComparison.Ordinal);
    }

    private void AppendEvent(SignalStateDocument document, string sessionId, string? agent, AgentSignal signal, string? @event, DateTimeOffset updatedAt)
    {
        var record = new SignalEventRecord(sessionId, agent, signal, @event, updatedAt);
        RemoveDuplicateEvent(document.Events, record);
        document.Events.Add(record);
        CompactEventHistory(document);
    }

    private void CompactEventHistory(SignalStateDocument document)
    {
        var compacted = new List<SignalEventRecord>();
        foreach (var signalEvent in document.Events)
        {
            RemoveDuplicateEvent(compacted, signalEvent);
            compacted.Add(signalEvent);
        }

        if (compacted.Count > EventLimit)
        {
            compacted = compacted.TakeLast(EventLimit).ToList();
        }

        document.Events = compacted;
    }

    private static void RemoveDuplicateEvent(List<SignalEventRecord> events, SignalEventRecord signalEvent)
    {
        var key = EventDeduplicationKey(signalEvent);
        var index = events.FindLastIndex(existing =>
            EventDeduplicationKey(existing) == key
            && (existing.UpdatedAt - signalEvent.UpdatedAt).Duration() <= DuplicateEventWindow);
        if (index >= 0)
        {
            events.RemoveAt(index);
        }
    }

    private static string EventDeduplicationKey(SignalEventRecord signalEvent)
    {
        var agent = NormalizeDeduplicationPart(signalEvent.Agent);
        var eventName = NormalizeDeduplicationPart(signalEvent.Event) ?? AgentSignalParser.ToRawValue(signalEvent.Signal);
        return $"{signalEvent.SessionId}|{agent}|{AgentSignalParser.ToRawValue(signalEvent.Signal)}|{eventName}";
    }

    private static string? NormalizeDeduplicationPart(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant().Replace("_", "-", StringComparison.Ordinal).Replace(" ", "-", StringComparison.Ordinal);
    }

    private SignalStateDocument? ReadDocument()
    {
        if (!File.Exists(StateFilePath))
        {
            return new SignalStateDocument();
        }

        try
        {
            var json = File.ReadAllText(StateFilePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<SignalStateDocument>(json, jsonOptions) ?? new SignalStateDocument();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void WriteDocument(SignalStateDocument document)
    {
        var directory = Path.GetDirectoryName(StateFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = StateFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var json = JsonSerializer.Serialize(document, jsonOptions);
        File.WriteAllText(tempPath, json + Environment.NewLine, Encoding.UTF8);
        if (File.Exists(StateFilePath))
        {
            File.Replace(tempPath, StateFilePath, null);
        }
        else
        {
            File.Move(tempPath, StateFilePath);
        }
    }

    private T WithLock<T>(Func<T> body)
    {
        using var mutex = new Mutex(false, mutexName);
        mutex.WaitOne();
        try
        {
            return body();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static TimeSpan EnvironmentTimeSpan(string key, TimeSpan fallback)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        return double.TryParse(raw, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : fallback;
    }

    private static int EnvironmentInt(string key, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
    }

    private sealed record RuntimePruneResult(bool HadSessionsBeforePrune, bool RemovedNonCompletedSession);
}
