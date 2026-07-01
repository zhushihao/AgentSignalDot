namespace AgentSignalBar.Core;

public enum AgentIdentity
{
    Default,
    Codex,
    ClaudeCode
}

public static class AgentIdentityBadge
{
    public static AgentIdentity Resolve(SignalSnapshot snapshot)
    {
        var sessionAgent = snapshot.Sessions
            .OrderByDescending(session => session.Signal.DisplayState().Priority())
            .ThenByDescending(session => session.UpdatedAt)
            .Select(session => session.Agent)
            .FirstOrDefault(agent => !string.IsNullOrWhiteSpace(agent));

        if (sessionAgent is not null)
        {
            return ResolveAgentName(sessionAgent);
        }

        var eventAgent = snapshot.RecentEvents
            .Select(signalEvent => signalEvent.Agent)
            .FirstOrDefault(agent => !string.IsNullOrWhiteSpace(agent));

        return ResolveAgentName(eventAgent);
    }

    public static AgentIdentity ResolveAgentName(string? agent)
    {
        if (string.IsNullOrWhiteSpace(agent))
        {
            return AgentIdentity.Default;
        }

        var normalized = agent.Trim().ToLowerInvariant().Replace("_", "-", StringComparison.Ordinal).Replace(" ", "-", StringComparison.Ordinal);
        if (normalized.StartsWith("codex", StringComparison.Ordinal))
        {
            return AgentIdentity.Codex;
        }

        if (normalized is "claude" or "claude-code" || normalized.StartsWith("claude-code-", StringComparison.Ordinal))
        {
            return AgentIdentity.ClaudeCode;
        }

        return AgentIdentity.Default;
    }
}
