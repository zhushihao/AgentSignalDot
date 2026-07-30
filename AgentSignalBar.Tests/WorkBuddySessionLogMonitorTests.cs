using AgentSignalBar.Core;
using System.Diagnostics;

static class WorkBuddySessionLogMonitorTests
{
    // ── helpers ────────────────────────────────────────────────────────

    private const string LogsDirName = "logs";
    private const string DayDir = "2026-07-08";

    /// <summary>Create a temp fixture with a logs/yyyy-MM-dd/ directory inside.</summary>
    private static (TempFixture Fixture, string LogsRoot, string DayPath) CreateLogFixture()
    {
        var fixture = TempFixture.Create();
        var logsRoot = Path.Combine(fixture.DirectoryPath, LogsDirName);
        var dayPath = Path.Combine(logsRoot, DayDir);
        Directory.CreateDirectory(dayPath);
        return (fixture, logsRoot, dayPath);
    }

    /// <summary>Write a log file and return its path.</summary>
    private static string WriteLogFile(string dayPath, string fileName, string content)
    {
        var path = Path.Combine(dayPath, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static string AppendToFile(string path, string content)
    {
        File.AppendAllText(path, content);
        return path;
    }

    // ── log line generators ────────────────────────────────────────────

    /// <summary>Returns a timestamp string near the current time.</summary>
    private static string NowTs(int offsetSeconds = 0)
    {
        var t = DateTime.Now.AddSeconds(offsetSeconds);
        return $"{t.Year}/{t.Month}/{t.Day} {t.Hour:D2}:{t.Minute:D2}:{t.Second:D2}.{t.Millisecond:D3}";
    }

    private static string SmartLine(string sessionId, bool busy, string eventName, int offsetSeconds = 0)
    {
        var ts = NowTs(offsetSeconds);
        return $"[{ts}] [Info] [SessionRunStateMachine] transition | sessionId={sessionId} | event={eventName} | from=idle | to=agent_running | lifecycle=running | busy={busy.ToString().ToLower()} | queueBusy=true | elapsedSinceLastTransitionMs=0";
    }

    private static string BusyLine(string sessionId, string eventName = "MODEL_STREAM_STARTED")
        => SmartLine(sessionId, true, eventName);

    private static string IdleLine(string sessionId, string eventName = "AGENT_ENDED")
        => SmartLine(sessionId, false, eventName);

    private static string ToolStartedLine(string sessionId)
        => SmartLine(sessionId, true, "TOOL_STARTED");

    private static string ToolEndedLine(string sessionId, string eventName = "TOOL_ENDED")
        => SmartLine(sessionId, true, eventName);

    private static string PermissionWaitingLine(string sessionId)
        => SmartLine(sessionId, true, "WAITING_FOR_PERMISSION");

    private static string PermissionAskLine()
        => $"[{NowTs(1)}] [Info] [tool-permission] ASK tool=Bash";

    private static string PermissionAutoApprovedLine(string sessionId)
        => $"[{NowTs(2)}] [Info] [HandleInterruptions] Auto-approving tool Bash session {sessionId} (FullAccess)";

    // ── session-liveness (process-exit) test fixtures ─────────────────

    /// <summary>Create a sessions/ dir inside the fixture and return its path.</summary>
    private static string CreateSessionsDir(TempFixture fixture)
    {
        var sessionsRoot = Path.Combine(fixture.DirectoryPath, "sessions");
        Directory.CreateDirectory(sessionsRoot);
        return sessionsRoot;
    }

    /// <summary>Write a WorkBuddy pid file (sessions/&lt;pid&gt;.json) bound to a sessionId.</summary>
    private static void WritePidFile(string sessionsRoot, int pid, string sessionId)
    {
        var path = Path.Combine(sessionsRoot, $"{pid}.json");
        File.WriteAllText(path, $"{{\"pid\":{pid},\"sessionId\":\"{sessionId}\",\"cwd\":\"C:\\\\x\"}}");
    }

    // ── test cases ─────────────────────────────────────────────────────

    // 1. Prime detects a busy session and returns it as initial state.
    public static void PrimeDetectsBusySession()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                BusyLine("sid-1", "MODEL_STREAM_STARTED") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var (stateChanges, blockedChanges) = monitor.Poll();

            // First Poll must return the busy session as initial state.
            Assert.Equal(1, stateChanges.Count);
            Assert.Equal("sid-1", stateChanges[0].SessionId);
            Assert.Equal(true, stateChanges[0].IsBusy);
            Assert.Equal(0, blockedChanges.Count);

            // BusySessionIds must reflect the session.
            Assert.Equal(1, monitor.BusySessionIds.Count);
            Assert.Equal(true, monitor.BusySessionIds.Contains("sid-1"));
            Assert.Equal(0, monitor.BlockedSessionIds.Count);
        }
    }

    // 2. Prime detects idle session and does NOT emit it.
    public static void PrimeSkipsIdleSession()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                IdleLine("sid-1") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var (stateChanges, blockedChanges) = monitor.Poll();

            Assert.Equal(0, stateChanges.Count);
            Assert.Equal(0, blockedChanges.Count);
            Assert.Equal(0, monitor.BusySessionIds.Count);
        }
    }

    // 3. Prime: last line busy wins over earlier idle.
    public static void PrimeLastTransitionWins()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                BusyLine("sid-1") + "\n" +
                IdleLine("sid-1") + "\n" +
                BusyLine("sid-1", "MODEL_STREAM_STARTED_AGAIN") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var (stateChanges, _) = monitor.Poll();

            Assert.Equal(1, stateChanges.Count);
            Assert.Equal(true, stateChanges[0].IsBusy);
            Assert.Equal("sid-1", stateChanges[0].SessionId);
            Assert.Equal(1, monitor.BusySessionIds.Count);
        }
    }

    // 4. Prime: blocked session via WAITING_FOR_PERMISSION + ASK.
    public static void PrimeDetectsBlockedSession()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                ToolStartedLine("sid-1") + "\n" +
                PermissionAskLine() + "\n" +
                PermissionWaitingLine("sid-1") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var (stateChanges, blockedChanges) = monitor.Poll();

            // Should have both: busy stateChange + blocked blockedChange.
            Assert.Equal(1, stateChanges.Count);
            Assert.Equal(true, stateChanges[0].IsBusy);

            Assert.Equal(1, blockedChanges.Count);
            Assert.Equal("sid-1", blockedChanges[0].SessionId);
            Assert.Equal(true, blockedChanges[0].IsBlocked);

            Assert.Equal(1, monitor.BusySessionIds.Count);
            Assert.Equal(1, monitor.BlockedSessionIds.Count);
            Assert.Equal(true, monitor.BlockedSessionIds.Contains("sid-1"));
        }
    }

    // 5. Prime: auto-approved permission must NOT block.
    public static void PrimeSkipsAutoApprovedPermission()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                ToolStartedLine("sid-1") + "\n" +
                PermissionAskLine() + "\n" +
                PermissionAutoApprovedLine("sid-1") + "\n" +
                PermissionWaitingLine("sid-1") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var (stateChanges, blockedChanges) = monitor.Poll();

            // Auto-approve clears pending → should NOT be blocked.
            Assert.Equal(1, stateChanges.Count); // busy from WAITING_FOR_PERMISSION
            Assert.Equal(0, blockedChanges.Count);
            Assert.Equal(0, monitor.BlockedSessionIds.Count);
        }
    }

    // 6. ReadNewLines: appended lines are picked up after Prime.
    public static void ReadNewLinesPicksUpAppendedData()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            var logPath = WriteLogFile(dayPath, "session-a.log",
                IdleLine("sid-1") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);

            // Prime — no busy sessions.
            var first = monitor.Poll();
            Assert.Equal(0, first.StateChanges.Count);
            Assert.Equal(0, monitor.BusySessionIds.Count);

            // Append a busy line while monitor is running.
            AppendToFile(logPath, BusyLine("sid-1") + "\n");

            // Second Poll must pick it up.
            var second = monitor.Poll();
            Assert.Equal(1, second.StateChanges.Count);
            Assert.Equal("sid-1", second.StateChanges[0].SessionId);
            Assert.Equal(true, second.StateChanges[0].IsBusy);
            Assert.Equal(1, monitor.BusySessionIds.Count);

            // Third Poll with no new data → no changes.
            var third = monitor.Poll();
            Assert.Equal(0, third.StateChanges.Count);
            Assert.Equal(0, third.BlockedChanges.Count);
        }
    }

    // 7. ToolEnded resolves blocked state (the fix from 23:25).
    public static void ToolEndedResolvesBlockedState()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                ToolStartedLine("sid-1") + "\n" +
                PermissionAskLine() + "\n" +
                PermissionWaitingLine("sid-1") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var first = monitor.Poll();

            Assert.Equal(1, first.BlockedChanges.Count);
            Assert.Equal(true, first.BlockedChanges[0].IsBlocked);
            Assert.Equal(1, monitor.BlockedSessionIds.Count);

            // Now append AGENT_ENDED + RUN_SKIPPED transitions.
            var logPath = Path.Combine(dayPath, "session-a.log");
            AppendToFile(logPath,
                ToolEndedLine("sid-1", "AGENT_ENDED") + "\n" +
                ToolEndedLine("sid-1", "RUN_SKIPPED") + "\n");

            var second = monitor.Poll();

            // Should have unblock notification.
            var unblock = second.BlockedChanges.FirstOrDefault(c => !c.IsBlocked);
            Assert.NotNull(unblock);
            Assert.Equal("sid-1", unblock.SessionId);
            Assert.Equal(0, monitor.BlockedSessionIds.Count);
        }
    }

    // 8. ToolStarted resolves blocked state.
    public static void ToolStartedResolvesBlockedState()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                ToolStartedLine("sid-1") + "\n" +
                PermissionAskLine() + "\n" +
                PermissionWaitingLine("sid-1") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var first = monitor.Poll();
            Assert.Equal(1, monitor.BlockedSessionIds.Count);

            var logPath = Path.Combine(dayPath, "session-a.log");
            AppendToFile(logPath, ToolStartedLine("sid-1") + "\n");

            var second = monitor.Poll();
            var unblock = second.BlockedChanges.FirstOrDefault(c => !c.IsBlocked);
            Assert.NotNull(unblock);
            Assert.Equal("sid-1", unblock.SessionId);
            Assert.Equal(0, monitor.BlockedSessionIds.Count);
        }
    }

    // 9. PermissionAutoApproved resolves blocked state.
    public static void PermissionAutoApprovedResolvesBlockedState()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                ToolStartedLine("sid-1") + "\n" +
                PermissionAskLine() + "\n");

            // NO PermissionWaitingLine — the ASK line alone defers block.
            // At end of Prime the pending permission is resolved as blocked.
            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var first = monitor.Poll();
            Assert.Equal(1, monitor.BlockedSessionIds.Count);

            var logPath = Path.Combine(dayPath, "session-a.log");
            AppendToFile(logPath, PermissionAutoApprovedLine("sid-1") + "\n");

            var second = monitor.Poll();
            var unblock = second.BlockedChanges.FirstOrDefault(c => !c.IsBlocked);
            Assert.NotNull(unblock);
            Assert.Equal("sid-1", unblock.SessionId);
            Assert.Equal(0, monitor.BlockedSessionIds.Count);
        }
    }

    // 9b. A fresh busy transition for a session currently blocked resolves the
    // block. In WorkBuddy busy and blocked are mutually exclusive — a working
    // agent is never still awaiting permission — so any new busy activity for
    // the same session clears a stale block. This is the fix for the "ghost
    // block" where FullAccess auto-approves a tool but no explicit TOOL_STARTED
    // / Auto-approving line reaches the monitor: the next busy event alone is
    // enough to flip the light back to green.
    public static void BusyTransitionResolvesBlockedState()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                ToolStartedLine("sid-1") + "\n" +
                PermissionAskLine() + "\n" +
                PermissionWaitingLine("sid-1") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var first = monitor.Poll();
            Assert.Equal(1, monitor.BlockedSessionIds.Count);

            var logPath = Path.Combine(dayPath, "session-a.log");
            // A plain busy=true transition (NOT a TOOL_STARTED / Auto-approving
            // line) — only the new busy-wins-over-block rule should clear it.
            AppendToFile(logPath, BusyLine("sid-1", "MODEL_STREAM_STARTED_AGAIN") + "\n");

            var second = monitor.Poll();
            var unblock = second.BlockedChanges.FirstOrDefault(c => !c.IsBlocked);
            Assert.NotNull(unblock);
            Assert.Equal("sid-1", unblock.SessionId);
            Assert.Equal(0, monitor.BlockedSessionIds.Count);
            Assert.Equal(1, monitor.BusySessionIds.Count);
        }
    }

    // 9c. Same as 9b but the busy transition appears in the SAME Prime read:
    // if a session is blocked (WAITING_FOR_PERMISSION) and then shows a busy
    // transition later in the file, Prime must not report it as blocked.
    public static void BusyTransitionWithinPrimeResolvesBlockedState()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                PermissionAskLine() + "\n" +
                PermissionWaitingLine("sid-1") + "\n" +
                BusyLine("sid-1", "MODEL_STREAM_STARTED") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var (_, blockedChanges) = monitor.Poll();

            // The trailing busy transition must cancel the deferred block.
            Assert.Equal(0, monitor.BlockedSessionIds.Count);
            Assert.Equal(0, blockedChanges.Count(c => c.IsBlocked));
            Assert.Equal(1, monitor.BusySessionIds.Count);
        }
    }

    // 10. Sticky-busy: a busy session with only a 60s log gap must STAY busy.
    // Regression guard for the "no response while busy" bug — a short silence
    // must never flip a live task back to idle.
    public static void BusySessionPersistsAcrossShortGap()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            // Busy line timestamped 60s in the past (well inside the 30-min floor).
            WriteLogFile(dayPath, "session-a.log",
                SmartLine("sid-1", true, "MODEL_STREAM_STARTED", -60) + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var first = monitor.Poll();
            Assert.Equal(1, first.StateChanges.Count); // initial busy emission
            Assert.Equal(1, monitor.BusySessionIds.Count);

            // Second Poll — with no new activity, busy must PERSIST (not clear).
            var second = monitor.Poll();
            Assert.Equal(0, second.StateChanges.Count(c => !c.IsBusy));
            Assert.Equal(1, monitor.BusySessionIds.Count);
        }
    }

    // 11. Tool-executing session is NOT stale-cleared.
    public static void ToolExecutingSessionNotStale()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                SmartLine("sid-1", true, "TOOL_STARTED", -60) + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            monitor.Poll(); // Prime
            Assert.Equal(1, monitor.BusySessionIds.Count);

            // Even with old timestamp, tool is executing → not stale.
            var second = monitor.Poll();
            Assert.Equal(0, second.StateChanges.Count(c => !c.IsBusy));
            Assert.Equal(1, monitor.BusySessionIds.Count);
        }
    }

    // 12. Blocked session is NOT stale-cleared.
    public static void BlockedSessionNotStale()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                SmartLine("sid-1", true, "TOOL_STARTED", -60) + "\n" +
                $"[{NowTs(-59)}] [Info] [tool-permission] ASK tool=Bash\n" +
                SmartLine("sid-1", true, "WAITING_FOR_PERMISSION", -60) + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            monitor.Poll(); // Prime
            Assert.Equal(1, monitor.BlockedSessionIds.Count);
            Assert.Equal(1, monitor.BusySessionIds.Count);

            // Stale check must skip blocked sessions.
            var second = monitor.Poll();
            Assert.Equal(0, second.StateChanges.Count(c => !c.IsBusy));
            Assert.Equal(1, monitor.BusySessionIds.Count);
            Assert.Equal(1, monitor.BlockedSessionIds.Count);
        }
    }

    // 13. Mixed busy + blocked: both initial states emitted.
    public static void MixedBusyAndBlockedInitialState()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                BusyLine("sid-busy") + "\n" +
                ToolStartedLine("sid-blocked") + "\n" +
                PermissionAskLine() + "\n" +
                PermissionWaitingLine("sid-blocked") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var (stateChanges, blockedChanges) = monitor.Poll();

            // sid-busy → thinking, sid-blocked → blocked (both busy=true from transitions, but blocked wins).
            Assert.Equal(true, stateChanges.Any(c => c.SessionId == "sid-busy" && c.IsBusy));
            Assert.Equal(true, stateChanges.Any(c => c.SessionId == "sid-blocked" && c.IsBusy));
            Assert.Equal(true, blockedChanges.Any(c => c.SessionId == "sid-blocked" && c.IsBlocked));
            Assert.Equal(false, blockedChanges.Any(c => c.SessionId == "sid-busy"));

            Assert.Equal(2, monitor.BusySessionIds.Count);
            Assert.Equal(1, monitor.BlockedSessionIds.Count);
        }
    }

    // 14. Multiple log files: sessions from independent files.
    public static void CrossFileSessions()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "project-a.log",
                BusyLine("sid-a") + "\n");
            WriteLogFile(dayPath, "project-b.log",
                ToolStartedLine("sid-b") + "\n" +
                PermissionAskLine() + "\n" +
                PermissionWaitingLine("sid-b") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var (stateChanges, blockedChanges) = monitor.Poll();

            Assert.Equal(true, stateChanges.Any(c => c.SessionId == "sid-a"));
            Assert.Equal(true, stateChanges.Any(c => c.SessionId == "sid-b"));
            Assert.Equal(true, blockedChanges.Any(c => c.SessionId == "sid-b" && c.IsBlocked));
            Assert.Equal(2, monitor.BusySessionIds.Count);
            Assert.Equal(1, monitor.BlockedSessionIds.Count);
        }
    }

    // 15. Session goes busy → idle → busy across polls.
    public static void SessionLifecycleAcrossPolls()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            var logPath = WriteLogFile(dayPath, "session-a.log", "");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            monitor.Poll(); // Prime — nothing.
            Assert.Equal(0, monitor.BusySessionIds.Count);

            // Phase 1: session becomes busy.
            AppendToFile(logPath, BusyLine("sid-1") + "\n");
            var r1 = monitor.Poll();
            Assert.Equal(1, r1.StateChanges.Count(c => c.IsBusy));
            Assert.Equal("sid-1", r1.StateChanges[0].SessionId);
            Assert.Equal(1, monitor.BusySessionIds.Count);

            // Phase 2: session becomes idle.
            AppendToFile(logPath, IdleLine("sid-1") + "\n");
            var r2 = monitor.Poll();
            Assert.Equal(1, r2.StateChanges.Count(c => !c.IsBusy));
            Assert.Equal(0, monitor.BusySessionIds.Count);

            // Phase 3: session becomes busy again.
            AppendToFile(logPath, BusyLine("sid-1", "MODEL_STREAM_STARTED") + "\n");
            var r3 = monitor.Poll();
            Assert.Equal(1, r3.StateChanges.Count(c => c.IsBusy));
            Assert.Equal(1, monitor.BusySessionIds.Count);
        }
    }

    // 16. Blocked → unblock → blocked again.
    public static void BlockedLifecycleAcrossPolls()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            var logPath = WriteLogFile(dayPath, "session-a.log", "");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            monitor.Poll(); // Prime — nothing.

            // Phase 1: session gets blocked.
            AppendToFile(logPath,
                ToolStartedLine("sid-1") + "\n" +
                PermissionAskLine() + "\n" +
                PermissionWaitingLine("sid-1") + "\n");
            var r1 = monitor.Poll();
            Assert.Equal(1, r1.BlockedChanges.Count(c => c.IsBlocked));

            // Phase 2: unblock via TOOL_ENDED.
            AppendToFile(logPath, ToolEndedLine("sid-1") + "\n");
            var r2 = monitor.Poll();
            Assert.Equal(1, r2.BlockedChanges.Count(c => !c.IsBlocked));
            Assert.Equal(0, monitor.BlockedSessionIds.Count);

            // Phase 3: blocked again.
            AppendToFile(logPath,
                ToolStartedLine("sid-1") + "\n" +
                PermissionAskLine() + "\n" +
                PermissionWaitingLine("sid-1") + "\n");
            var r3 = monitor.Poll();
            Assert.Equal(1, r3.BlockedChanges.Count(c => c.IsBlocked));
            Assert.Equal(1, monitor.BlockedSessionIds.Count);
        }
    }

    // 17. Empty log directory (no files) → no errors, no state.
    public static void EmptyLogDirectoryNoErrors()
    {
        var (fixture, logsRoot, _) = CreateLogFixture();
        using (fixture)
        {
            // dayPath exists but no .log files.
            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var (stateChanges, blockedChanges) = monitor.Poll();

            Assert.Equal(0, stateChanges.Count);
            Assert.Equal(0, blockedChanges.Count);
            Assert.Equal(0, monitor.BusySessionIds.Count);
            Assert.Equal(0, monitor.BlockedSessionIds.Count);

            // Second Poll also no errors.
            var second = monitor.Poll();
            Assert.Equal(0, second.StateChanges.Count);
            Assert.Equal(0, second.BlockedChanges.Count);
        }
    }

    // 18. No pending permission after Prime if blocked was already resolved.
    public static void PrimeBlockedThenResolvedWithinSameRead()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                ToolStartedLine("sid-1") + "\n" +
                PermissionAskLine() + "\n" +
                PermissionWaitingLine("sid-1") + "\n" +
                ToolEndedLine("sid-1") + "\n");  // Resolved before Prime finishes reading

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var (stateChanges, blockedChanges) = monitor.Poll();

            // The blocked state was set then cleared within same Prime read.
            // It should NOT be emitted as blocked.
            Assert.Equal(true, stateChanges.Any(c => c.SessionId == "sid-1"));
            Assert.Equal(false, blockedChanges.Any(c => c.IsBlocked));
            Assert.Equal(0, monitor.BlockedSessionIds.Count);
        }
    }

    // 19. Prime: pendingPermissionSessions must be resolved per-file (not accumulated across files).
    public static void PendingPermissionResolvedPerFile()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            // File A: blocked session that gets resolved.
            WriteLogFile(dayPath, "project-a.log",
                ToolStartedLine("sid-a") + "\n" +
                PermissionAskLine() + "\n" +
                PermissionWaitingLine("sid-a") + "\n" +
                ToolEndedLine("sid-a") + "\n");

            // File B: genuinely blocked session.
            WriteLogFile(dayPath, "project-b.log",
                ToolStartedLine("sid-b") + "\n" +
                PermissionAskLine() + "\n" +
                PermissionWaitingLine("sid-b") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var (stateChanges, blockedChanges) = monitor.Poll();

            Assert.Equal(1, blockedChanges.Count(c => c.IsBlocked));
            Assert.Equal("sid-b", blockedChanges.First(c => c.IsBlocked).SessionId);
            Assert.Equal(1, monitor.BlockedSessionIds.Count);
            Assert.Equal(true, monitor.BlockedSessionIds.Contains("sid-b"));
            Assert.Equal(false, monitor.BlockedSessionIds.Contains("sid-a"));
        }
    }

    // 20. READ_NEW_LINES: second poll with no file growth returns empty.
    public static void SecondPollNoGrowthReturnsEmpty()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                BusyLine("sid-1") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var first = monitor.Poll();
            Assert.Equal(1, first.StateChanges.Count);

            // No new data appended.
            var second = monitor.Poll();
            Assert.Equal(0, second.StateChanges.Count);
            Assert.Equal(0, second.BlockedChanges.Count);

            // BusySessionIds still reflects the state.
            Assert.Equal(1, monitor.BusySessionIds.Count);
        }
    }

    // 21. WAITING_FOR_PERMISSION without preceding ASK → still defers block.
    public static void PermissionWaitingWithoutAskDefersBlock()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                PermissionWaitingLine("sid-1") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var (stateChanges, blockedChanges) = monitor.Poll();

            // The WAITING_FOR_PERMISSION line adds to pendingPermissionSessions.
            // At end of Prime, any unresolved pending becomes blocked.
            Assert.Equal(1, blockedChanges.Count(c => c.IsBlocked));
            Assert.Equal("sid-1", blockedChanges.First(c => c.IsBlocked).SessionId);
            Assert.Equal(1, monitor.BlockedSessionIds.Count);
        }
    }

    // 22. Multiple tool lifecycle: permission events track lastToolSession.
    public static void PermissionAskTracksLastToolSession()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                ToolStartedLine("sid-1") + "\n" +   // lastToolSession = sid-1
                PermissionAskLine() + "\n" +         // pendingPermissionSessions.Add(sid-1)
                ToolEndedLine("sid-1") + "\n" +      // clears pending for sid-1
                ToolStartedLine("sid-2") + "\n" +    // lastToolSession = sid-2
                PermissionAskLine() + "\n");          // pendingPermissionSessions.Add(sid-2)

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var (stateChanges, blockedChanges) = monitor.Poll();

            // sid-1's pending was cleared by ToolEnded.
            // sid-2's pending remains → blocked.
            Assert.Equal(1, blockedChanges.Count(c => c.IsBlocked));
            Assert.Equal("sid-2", blockedChanges.First(c => c.IsBlocked).SessionId);
            Assert.Equal(1, monitor.BlockedSessionIds.Count);
        }
    }

    // 23. Non-WorkBuddy log lines are safely ignored.
    public static void IgnoresNonWorkBuddyLines()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                "[2026/7/8 13:00:00.000] [Info] [OtherComponent] Some random log\n" +
                "Just a plain text line\n" +
                BusyLine("sid-1") + "\n" +
                "[2026/7/8 13:00:02.000] [Debug] Trace output\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var (stateChanges, _) = monitor.Poll();

            Assert.Equal(1, stateChanges.Count);
            Assert.Equal("sid-1", stateChanges[0].SessionId);
            Assert.Equal(1, monitor.BusySessionIds.Count);
        }
    }

    // 24. PermissionAutoApproved on non-WAITING_FOR_PERMISSION line clears pending.
    public static void AutoApprovedClearsPendingWithoutStateTransition()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                ToolStartedLine("sid-1") + "\n" +
                PermissionAskLine() + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var first = monitor.Poll();
            Assert.Equal(1, first.BlockedChanges.Count(c => c.IsBlocked));
            Assert.Equal(1, monitor.BlockedSessionIds.Count);

            // Append auto-approve without a new state transition.
            var logPath = Path.Combine(dayPath, "session-a.log");
            AppendToFile(logPath, PermissionAutoApprovedLine("sid-1") + "\n");

            var second = monitor.Poll();
            Assert.Equal(1, second.BlockedChanges.Count(c => !c.IsBlocked));
            Assert.Equal(0, monitor.BlockedSessionIds.Count);
        }
    }

    // 25. Concurrent sessions in same file: all detected.
    public static void ConcurrentSessionsInSameFile()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "multi.log",
                BusyLine("agent-1") + "\n" +
                BusyLine("agent-2") + "\n" +
                ToolStartedLine("agent-3") + "\n" +
                PermissionAskLine() + "\n" +
                PermissionWaitingLine("agent-3") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var (stateChanges, blockedChanges) = monitor.Poll();

            Assert.Equal(3, stateChanges.Count); // agent-1, agent-2, agent-3 all busy
            Assert.Equal(1, blockedChanges.Count(c => c.IsBlocked)); // agent-3 blocked
            Assert.Equal(3, monitor.BusySessionIds.Count);
            Assert.Equal(1, monitor.BlockedSessionIds.Count);
        }
    }

    // 26. Sticky-busy across a long (5-min) gap — must never flip to idle.
    public static void BusySessionPersistsAcrossLongGap()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                SmartLine("sid-1", true, "MODEL_STREAM_STARTED", -300) + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var first = monitor.Poll();
            Assert.Equal(1, first.StateChanges.Count); // initial busy emission
            Assert.Equal(1, monitor.BusySessionIds.Count);

            // Append another busy line a moment later; still ~5 min old overall.
            var logPath = Path.Combine(dayPath, "session-a.log");
            AppendToFile(logPath, SmartLine("sid-1", true, "MODEL_STREAM_STARTED", -290) + "\n");

            var second = monitor.Poll();
            // No idle transition may be emitted; busy must persist.
            Assert.Equal(0, second.StateChanges.Count(c => !c.IsBusy));
            Assert.Equal(1, monitor.BusySessionIds.Count);
        }
    }

    // 27. Busy session is only cleared after a 30-min HARD-DEAD silence.
    // Exercises the runtime Poll() stale-clear path (not just Prime).
    public static void BusySessionClearedOnlyAfterHardDeadTimeout()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            var logPath = WriteLogFile(dayPath, "session-a.log",
                SmartLine("sid-1", true, "MODEL_STREAM_STARTED", -1) + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var first = monitor.Poll();
            Assert.Equal(1, first.StateChanges.Count(c => c.IsBusy));
            Assert.Equal(1, monitor.BusySessionIds.Count);

            // A new transition arrives, but its timestamp is 31 min in the past
            // (the session really died long ago without a clean busy=false).
            AppendToFile(logPath, SmartLine("sid-1", true, "MODEL_STREAM_STARTED", -1900) + "\n");

            var second = monitor.Poll();
            // The hard-dead guard must now flip it to idle and emit the change.
            var idleChange = second.StateChanges.FirstOrDefault(c => !c.IsBusy);
            Assert.NotNull(idleChange);
            Assert.Equal("sid-1", idleChange.SessionId);
            Assert.Equal(0, monitor.BusySessionIds.Count);
        }
    }

    // 28. Busy session that is already 31-min-old at Prime is treated as
    // hard-dead and never shown (Prime-path guard).
    public static void BusySessionHardDeadAtPrime()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                SmartLine("sid-1", true, "MODEL_STREAM_STARTED", -1900) + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var first = monitor.Poll();
            // Prime must NOT emit a BUSY signal for a session silent for >30 min,
            // but it SHOULD emit a StateChange(false) so the store can flush any
            // stale record left from the previous tray run.
            Assert.Equal(0, first.StateChanges.Count(c => c.IsBusy));
            var idleChange = first.StateChanges.FirstOrDefault(c => !c.IsBusy);
            Assert.NotNull(idleChange);
            Assert.Equal("sid-1", idleChange!.SessionId);
            Assert.Equal(0, monitor.BusySessionIds.Count);
        }
    }

    // 29. Explicit busy=false transition clears the session IMMEDIATELY —
    // we never wait for the hard-dead timeout when WorkBuddy tells us directly.
    public static void ExplicitBusyFalseClearsImmediately()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            var logPath = WriteLogFile(dayPath, "session-a.log",
                SmartLine("sid-1", true, "MODEL_STREAM_STARTED", -1) + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var first = monitor.Poll();
            Assert.Equal(1, first.StateChanges.Count(c => c.IsBusy));
            Assert.Equal(1, monitor.BusySessionIds.Count);

            AppendToFile(logPath, IdleLine("sid-1") + "\n");

            var second = monitor.Poll();
            var idleChange = second.StateChanges.FirstOrDefault(c => !c.IsBusy);
            Assert.NotNull(idleChange);
            Assert.Equal("sid-1", idleChange.SessionId);
            Assert.Equal(0, monitor.BusySessionIds.Count);
        }
    }

    // 30. Cross-session liveness: a busy session that goes silent while the app
    // keeps working on OTHER sessions is orphaned (interrupted without a clean
    // busy=false) and must be idled well before the 30-min hard-dead window,
    // while the still-active session stays busy.
    public static void OrphanedBusySessionIdledWhenAppAliveElsewhere()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            // sid-1: went busy ~6.7 min ago then fell silent (abandoned).
            // sid-2: busy only ~10s ago — proves the app is still alive elsewhere.
            WriteLogFile(dayPath, "session-a.log",
                SmartLine("sid-1", true, "MODEL_STREAM_STARTED", -400) + "\n" +
                SmartLine("sid-2", true, "MODEL_STREAM_STARTED", -10) + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var first = monitor.Poll();
            // At prime both look busy (the orphan is silent but not yet deduced).
            Assert.Equal(2, first.StateChanges.Count(c => c.IsBusy));

            // Next poll: app is alive (sid-2 recent) but sid-1 has been mute for
            // > AbandonedSessionTimeout relative to global activity → idle it.
            var second = monitor.Poll();
            var idleChange = second.StateChanges.FirstOrDefault(c => !c.IsBusy);
            Assert.NotNull(idleChange);
            Assert.Equal("sid-1", idleChange.SessionId);
            Assert.Equal(1, monitor.BusySessionIds.Count);
        }
    }

    // 31. A solo busy session that has been completely silent for > 5 min
    // the static silence gate now idles it — the user chose "N=5 min auto
    // dark" over "stay green for 30 min". A long-running task that pauses
    // for > 5 min may briefly go dark but self-heals on the next activity.
    public static void SoloBusySessionIdledAfterSilenceTimeout()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            WriteLogFile(dayPath, "session-a.log",
                SmartLine("sid-1", true, "MODEL_STREAM_STARTED", -400) + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var first = monitor.Poll();
            Assert.Equal(1, first.StateChanges.Count(c => c.IsBusy));

            // Silent for > 5 min (400s) → cleared by the absolute silence gate.
            var second = monitor.Poll();
            var idleChange = second.StateChanges.FirstOrDefault(c => !c.IsBusy);
            Assert.NotNull(idleChange);
            Assert.Equal("sid-1", idleChange!.SessionId);
            Assert.Equal(0, monitor.BusySessionIds.Count);
        }
    }

    // 32. Liveness gate: a blocked session whose process is provably dead
    // (no live pid AND a stale log) must NOT be revived by Prime. This is the
    // exact root cause of the "red dot blinks forever" bug — a session the
    // user cancelled days ago leaves a sticky block that Prime rebuilds from
    // the old log on every tray restart. The log ends in CANCEL_REQUESTED
    // (busy=false) so the existing "hard-dead only clears BUSY sessions" prune
    // can't mask the fix: only the liveness gate clears this idle-but-blocked
    // session. With liveness on + no pid file + an ancient log, it's dropped.
    public static void DeadBlockedSessionNotRevivedByPrimeLiveness()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            var sessionsRoot = CreateSessionsDir(fixture); // no pid file for sid-1

            WriteLogFile(dayPath, "session-a.log",
                SmartLine("sid-1", true, "TOOL_STARTED", -1900) + "\n" +
                $"[{NowTs(-1899)}] [Info] [tool-permission] ASK tool=Bash\n" +
                SmartLine("sid-1", true, "WAITING_FOR_PERMISSION", -1900) + "\n" +
                SmartLine("sid-1", false, "CANCEL_REQUESTED", -1899) + "\n");

            var monitor = new WorkBuddySessionLogMonitor(
                logsRoot, enableSessionLiveness: true, sessionsDirectory: sessionsRoot);
            var (stateChanges, blockedChanges) = monitor.Poll(); // Prime

            // Dead session must NOT be reported blocked or busy.
            Assert.Equal(0, blockedChanges.Count(c => c.IsBlocked));
            Assert.Equal(0, monitor.BlockedSessionIds.Count);
            Assert.Equal(0, monitor.BusySessionIds.Count);
        }
    }

    // 33. Liveness gate must NOT over-kill: a blocked session backed by a LIVE
    // process (pid file whose pid is the current process) stays blocked even
    // though its log is ancient. A live process is definitive proof the
    // session is still around, so the dot must keep showing red. Uses a
    // recent log so the existing hard-dead Prime prune does not interfere,
    // isolating the liveness "live pid → preserve" decision.
    public static void LiveBlockedSessionPreservedByLiveness()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            var sessionsRoot = CreateSessionsDir(fixture);
            var livePid = Process.GetCurrentProcess().Id;
            WritePidFile(sessionsRoot, livePid, "sid-1");

            WriteLogFile(dayPath, "session-a.log",
                SmartLine("sid-1", true, "TOOL_STARTED", -10) + "\n" +
                $"[{NowTs(-9)}] [Info] [tool-permission] ASK tool=Bash\n" +
                SmartLine("sid-1", true, "WAITING_FOR_PERMISSION", -10) + "\n");

            var monitor = new WorkBuddySessionLogMonitor(
                logsRoot, enableSessionLiveness: true, sessionsDirectory: sessionsRoot);
            var (stateChanges, blockedChanges) = monitor.Poll(); // Prime

            Assert.Equal(1, blockedChanges.Count(c => c.IsBlocked));
            Assert.Equal("sid-1", blockedChanges.First(c => c.IsBlocked).SessionId);
            Assert.Equal(1, monitor.BlockedSessionIds.Count);
            Assert.Equal(1, monitor.BusySessionIds.Count);
        }
    }

    // 34. Liveness is OPT-IN. With the default (liveness off) the monitor must
    // behave exactly as before — a blocked session with no pid file is still
    // revived at Prime. This pins the contract that turning the feature on is
    // purely additive and never changes behaviour for existing callers. Uses a
    // recent log so the hard-dead prune does not clear it first.
    public static void LivenessDisabledByDefaultRevivesBlocked()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            CreateSessionsDir(fixture); // sessions dir exists but has no pid file

            WriteLogFile(dayPath, "session-a.log",
                SmartLine("sid-1", true, "TOOL_STARTED", -10) + "\n" +
                $"[{NowTs(-9)}] [Info] [tool-permission] ASK tool=Bash\n" +
                SmartLine("sid-1", true, "WAITING_FOR_PERMISSION", -10) + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot); // default: off
            var (_, blockedChanges) = monitor.Poll(); // Prime

            // No liveness → old behaviour: the block is still reported.
            Assert.Equal(1, blockedChanges.Count(c => c.IsBlocked));
            Assert.Equal(1, monitor.BlockedSessionIds.Count);
        }
    }

    // 35. CANCEL_REQUESTED (user hit ESC / acp_cancel) must clear a sticky
    // block immediately at runtime, without waiting for a restart or a
    // 30-min timeout. This is the proactive fix so future cancellations never
    // leave the red dot stuck again.
    public static void CancelRequestedClearsBlock()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            var logPath = WriteLogFile(dayPath, "session-a.log",
                ToolStartedLine("sid-1") + "\n" +
                PermissionAskLine() + "\n" +
                PermissionWaitingLine("sid-1") + "\n");

            var monitor = new WorkBuddySessionLogMonitor(logsRoot);
            var first = monitor.Poll();
            Assert.Equal(1, first.BlockedChanges.Count(c => c.IsBlocked));
            Assert.Equal(1, monitor.BlockedSessionIds.Count);
            Assert.Equal(1, monitor.BusySessionIds.Count);

            // User cancels the session (busy=false transition).
            AppendToFile(logPath, SmartLine("sid-1", false, "CANCEL_REQUESTED") + "\n");

            var second = monitor.Poll();
            var unblock = second.BlockedChanges.FirstOrDefault(c => !c.IsBlocked);
            Assert.NotNull(unblock);
            Assert.Equal("sid-1", unblock!.SessionId);
            Assert.Equal(0, monitor.BlockedSessionIds.Count);
            Assert.Equal(0, monitor.BusySessionIds.Count);
        }
    }

    // 36. A blocked session with a dead pid file (process gone) must be cleared
    // at Prime immediately, regardless of log recency. A dead pid file is a
    // strong, recency-independent death signal — the session was running but
    // its process is gone, so the block is stale. This is the exact fix for
    // the live "red dot blinks forever" bug: 3f8dc3c4 has two stale pid files
    // from dead processes, and this test models that scenario.
    public static void DeadPidBlockedSessionClearedRegardlessOfLogRecency()
    {
        var (fixture, logsRoot, dayPath) = CreateLogFixture();
        using (fixture)
        {
            var sessionsRoot = CreateSessionsDir(fixture);
            WritePidFile(sessionsRoot, 999999, "sid-1"); // dead pid

            // Log is RECENT (-10s), so neither the existing hard-dead (30-min)
            // nor the log-staleness liveness gate (5-min) can clear it. Only
            // the dead-pid-immediate branch should do it.
            WriteLogFile(dayPath, "session-a.log",
                SmartLine("sid-1", true, "TOOL_STARTED", -10) + "\n" +
                $"[{NowTs(-9)}] [Info] [tool-permission] ASK tool=Bash\n" +
                SmartLine("sid-1", true, "WAITING_FOR_PERMISSION", -10) + "\n");

            var monitor = new WorkBuddySessionLogMonitor(
                logsRoot, enableSessionLiveness: true, sessionsDirectory: sessionsRoot);
            var (stateChanges, blockedChanges) = monitor.Poll(); // Prime

            // Dead pid + recent log → cleared (dead-pid-immediate branch).
            Assert.Equal(0, blockedChanges.Count(c => c.IsBlocked));
            Assert.Equal(0, monitor.BlockedSessionIds.Count);
            Assert.Equal(0, monitor.BusySessionIds.Count);
        }
    }
}
