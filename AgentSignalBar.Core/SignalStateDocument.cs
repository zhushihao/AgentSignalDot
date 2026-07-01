using System.Text.Json.Serialization;

namespace AgentSignalBar.Core;

public sealed record SessionRecord(
    [property: JsonPropertyName("agent")] string? Agent,
    [property: JsonPropertyName("signal")] AgentSignal Signal,
    [property: JsonPropertyName("last_event")] string? LastEvent,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public sealed class SignalEventRecord
{
    public SignalEventRecord()
    {
    }

    public SignalEventRecord(string sessionId, string? agent, AgentSignal signal, string? @event, DateTimeOffset updatedAt)
    {
        Id = Guid.NewGuid().ToString().ToUpperInvariant();
        SessionId = sessionId;
        Agent = agent;
        Signal = signal;
        Event = @event;
        UpdatedAt = updatedAt;
    }

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString().ToUpperInvariant();

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("agent")]
    public string? Agent { get; set; }

    [JsonPropertyName("signal")]
    public AgentSignal Signal { get; set; } = AgentSignal.Idle;

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SignalStateDocument
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("aggregate")]
    public AgentSignal? Aggregate { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("sessions")]
    public Dictionary<string, SessionRecord> Sessions { get; set; } = new();

    [JsonPropertyName("events")]
    public List<SignalEventRecord> Events { get; set; } = new();

    public AgentSignal AggregateSignal()
    {
        var candidates = Sessions.Values.Select(record => record.Signal).ToList();
        if (Aggregate is { } aggregate && candidates.Count == 0)
        {
            candidates.Add(aggregate);
        }

        return candidates
            .OrderByDescending(signal => signal.DisplayState().Priority())
            .FirstOrDefault(AgentSignal.Idle)
            .NormalizedAggregateSignal();
    }
}
