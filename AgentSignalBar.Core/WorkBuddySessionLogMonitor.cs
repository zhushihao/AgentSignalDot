using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Linq;
using System.Globalization;

namespace AgentSignalBar.Core;

/// <summary>
/// Monitors WorkBuddy session log files to determine whether each session
/// is actually busy (thinking/tool-using) or idle (waiting for input).
///
/// WorkBuddy session heartbeat files are updated constantly regardless of
/// agent state, so we parse the <c>SessionRunStateMachine</c> log entries
/// that contain precise <c>busy=true/false</c> transitions.
/// </summary>
public sealed class WorkBuddySessionLogMonitor
{
#if DEBUG
    private static readonly string DebugLogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentSignalBar", "monitor_debug.log");

    private static void DebugWrite(string msg)
    {
        try { File.AppendAllText(DebugLogPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); }
        catch { /* best effort */ }
    }
#endif
    private static readonly Regex StateTransitionPattern = new(
        @"\[SessionRunStateMachine\]\s+transition\s+\|\s+sessionId=(?<sessionId>[^\s|]+)\s+.*?\bbusy=(?<busy>true|false)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PermissionAskPattern = new(
        @"\[tool-permission\]\s+ASK\s",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Regex to detect events that end a tool lifecycle (AGENT_ENDED / failures).
    private static readonly Regex ToolEndingEventPattern = new(
        @"event=(?:TOOL_ENDED|AGENT_ENDED|RUN_FAILED|RUN_SKIPPED)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ToolStartedPattern = new(
        @"event=TOOL_STARTED\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // WorkBuddy enters this state when a tool requires explicit user approval
    // (e.g. plan mode, sandbox permissions). The agent is blocked until the
    // user approves or denies, then either TOOL_STARTED or AGENT_ENDED follows.
    private static readonly Regex PermissionWaitingPattern = new(
        @"event=WAITING_FOR_PERMISSION\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // In FullAccess mode WorkBuddy auto-approves tool permissions. The
    // [tool-permission] ASK line is still logged, but a [HandleInterruptions]
    // Auto-approving line follows immediately — the user is never actually
    // prompted, so this must NOT be treated as a blocking state.
    private static readonly Regex PermissionAutoApprovedPattern = new(
        @"\[HandleInterruptions\]\s+Auto-approving tool .*?session\s+(?<sessionId>[^\s)]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // A user-initiated cancel (acp_cancel / ESC) ends the session and voids
    // any pending permission block. WorkBuddy logs this as a CANCEL_REQUESTED
    // transition (busy=false). We must treat it as an explicit "session is
    // done" signal — it clears both busy and blocked state, so a cancelled
    // session never stays red (blocked) or green (busy) forever.
    private static readonly Regex CancelRequestedPattern = new(
        @"event=CANCEL_REQUESTED\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // WorkBuddy logs use LOCAL timestamps (verified: a "now" line reads as the
    // current local time, never UTC). We parse the leading [yyyy/M/d H:mm:ss.fff]
    // so per-session staleness is based on the session's real last activity,
    // not the file's mtime (which stays fresh due to keepalive writes).
    private static readonly Regex LogTimestampPattern = new(
        @"^\[(\d{4}/\d{1,2}/\d{1,2} \d{2}:\d{2}:\d{2}\.\d{1,3})\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static DateTime? TryParseLogTimestamp(string line)
    {
        var match = LogTimestampPattern.Match(line);
        if (!match.Success)
        {
            return null;
        }

        if (DateTime.TryParse(
                match.Groups[1].Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var local))
        {
            return local.ToUniversalTime();
        }

        return null;
    }

    private readonly string? logsDirectoryOverride;
    private readonly Dictionary<string, long> offsetsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> busyBySession = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> blockedSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> toolExecutingSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> pendingPermissionSessions = new(StringComparer.OrdinalIgnoreCase);
    private string? lastToolSession;
    private bool primed;

    // ── Session liveness (process-exit) support ───────────────────────
    // When enabled, the monitor cross-references WorkBuddy's session pid
    // files (sessions/<pid>.json) against live OS processes. A session that is
    // neither represented by a live process NOR recently active in its log is
    // "provably dead" (e.g. cancelled/closed) and its sticky busy/block state
    // is cleared. This is what stops a 2-day-old cancelled session from keeping
    // the red dot blinking forever, and from being revived by Prime on every
    // tray restart. Disabled by default so the (hermetic) test suite is
    // unaffected; the tray app opts in at construction time.
    private readonly bool enableSessionLiveness;
    private readonly string? sessionsDirectoryOverride;
    private Dictionary<string, List<int>>? pidSessionMap;
    private HashSet<int>? livePids;
    private DateTime pidMapRefreshedUtc = DateTime.MinValue;
    private static readonly TimeSpan PidMapRefreshInterval = TimeSpan.FromSeconds(30);

    // Tracks the last time we saw log activity for each session.
    private readonly Dictionary<string, DateTime> lastActivityUtc = new(StringComparer.OrdinalIgnoreCase);

    // Most recent transition timestamp seen across ALL sessions (any log file),
    // persisted across polls. Drives the cross-session liveness gate: a busy
    // session is "orphaned" when it has been silent longer than
    // AbandonedSessionTimeout *relative to this global timestamp* while the app
    // is otherwise alive. MinValue until the first transition is observed.
    private DateTime _lastAnyTransitionUtc = DateTime.MinValue;

    // How long a busy session may stay completely silent (no transition AND no
    // tool executing) before we treat it as hard-dead and force it back to idle.
    //
    // This is NOT a "keepalive" timeout: WorkBuddy emits SessionRunStateMachine
    // transitions only on discrete state changes, and during normal operation a
    // session can be legitimately busy for a long stretch without logging a single
    // transition (e.g. while the model streams a long response, or between
    // tool calls). Empirically a single session shows 26+ gaps longer than 30s
    // and a max gap of ~85 minutes across a normal day. Flushing busy->idle on
    // a short silence would make the light go dark while a task is still running,
    // which is exactly the bug we must avoid. So the safe rule is:
    //   * busy stays busy until an explicit busy=false transition is logged, OR
    //   * a VERY long silence (30 min) with no tool running proves it is dead.
    // The 30-min floor is far longer than any real in-task gap, so a live task
    // never flips to idle, while a truly-finished session that omitted a clean
    // busy=false transition is still reclaimed within half an hour.
    private static readonly TimeSpan BusyHardDeadTimeout = TimeSpan.FromMinutes(30);

    // A busy session that goes silent WHILE the app keeps emitting transitions
    // for OTHER sessions is orphaned: it was interrupted/abandoned (e.g. a stream
    // aborted mid-flight) and never logged a clean busy=false. We idle it much
    // sooner than the 30-min hard-dead window by inferring death from *absence
    // relative to overall app liveness* rather than absolute silence.
    //
    // Safety argument: a genuinely running session either writes its own
    // transitions (resetting its own silence window) or runs in solitude (the app
    // is also quiet, so the global timestamp below does not advance and this
    // branch can never fire — the 30-min rule still applies). Therefore this only
    // fires when the app is demonstrably alive and busy elsewhere while THIS
    // session is mute, which is exactly the orphaned case. The threshold is kept
    // well above any single legitimate operation gap that can coincide with other
    // live activity (a long model generation): 5 min is ~6x faster than the
    // hard-dead window while keeping false flicker rare and self-healing.
    private static readonly TimeSpan AbandonedSessionTimeout = TimeSpan.FromMinutes(5);

    public WorkBuddySessionLogMonitor(
        string? logsDirectory = null,
        bool enableSessionLiveness = false,
        string? sessionsDirectory = null)
    {
        logsDirectoryOverride = logsDirectory;
        this.enableSessionLiveness = enableSessionLiveness;
        this.sessionsDirectoryOverride = sessionsDirectory;
    }

    // ── Session liveness (process-exit) helpers ───────────────────────

    /// <summary>
    /// Roots that may contain WorkBuddy session pid files (sessions/*.json).
    /// Empty when liveness is disabled.
    /// </summary>
    private IReadOnlyList<string> GetSessionRoots()
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(sessionsDirectoryOverride) && Directory.Exists(sessionsDirectoryOverride))
        {
            roots.Add(sessionsDirectoryOverride);
        }
        else if (enableSessionLiveness)
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                roots.Add(Path.Combine(userProfile, ".workbuddy", "sessions"));
            }
            foreach (var r in EnumerateWorkBuddyDataRoots())
            {
                roots.Add(Path.Combine(r, ".workbuddy", "sessions"));
            }
        }
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Refreshes the sessionId→pids map and the set of live process ids, at
    /// most once per <see cref="PidMapRefreshInterval"/>. Cheap enough to call
    /// liberally; does nothing when liveness is disabled.
    /// </summary>
    private void RefreshSessionPidMapIfStale()
    {
        if (!enableSessionLiveness)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (pidSessionMap is not null && livePids is not null && (now - pidMapRefreshedUtc) < PidMapRefreshInterval)
        {
            return;
        }

        var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var root in GetSessionRoots())
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }
                foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var text = File.ReadAllText(file);
                        var sid = ExtractSessionId(text);
                        if (string.IsNullOrWhiteSpace(sid))
                        {
                            continue;
                        }
                        if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out var pid))
                        {
                            continue;
                        }
                        if (!map.TryGetValue(sid, out var list))
                        {
                            list = new List<int>();
                            map[sid] = list;
                        }
                        list.Add(pid);
                    }
                    catch
                    {
                        // Skip unreadable / malformed pid file.
                    }
                }
            }
        }
        catch
        {
            // Roots unavailable (sandbox / permissions) — liveness simply no-ops.
        }

        // Snapshot live process ids once so IsSessionProvablyDead can do O(1) lookups.
        HashSet<int>? live = null;
        try
        {
            var procs = Process.GetProcesses();
            live = new HashSet<int>(procs.Select(p => p.Id));
            foreach (var p in procs)
            {
                p.Dispose();
            }
        }
        catch
        {
            live = null;
        }

        pidSessionMap = map;
        livePids = live;
        pidMapRefreshedUtc = now;
    }

    private static readonly Regex SessionIdJsonPattern = new(
        "\"sessionId\"\\s*:\\s*\"(?<sid>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string? ExtractSessionId(string json)
    {
        var m = SessionIdJsonPattern.Match(json);
        return m.Success ? m.Groups["sid"].Value : null;
    }

    /// <summary>
    /// A session is "provably dead" when no process representing it is alive
    /// AND its log has been silent beyond the hard-dead window. This is safe:
    /// a live task always has a live pid (→ not dead), and a freshly-started
    /// session with no pid file yet is protected by its recent log activity
    /// (→ not dead). Only a session that is gone AND silent qualifies, which is
    /// exactly the cancelled/closed case we must reclaim.
    /// </summary>
    private bool IsSessionProvablyDead(string sessionId)
    {
        if (!enableSessionLiveness || pidSessionMap is null || livePids is null)
        {
            return false;
        }

        var hasPidFile = pidSessionMap.TryGetValue(sessionId, out var pids) && pids.Count > 0;
        if (hasPidFile)
        {
            foreach (var pid in pids!)
            {
                if (livePids.Contains(pid))
                {
                    return false; // a live process owns this session
                }
            }
            // We have explicit pid files for this session but every listed
            // process is gone. A pid file is only written for a live session
            // and (normally) removed on clean shutdown; a pid file whose
            // process no longer exists is a strong, recency-independent death
            // signal. Do not gate this behind log staleness — a cancelled/
            // killed session is reclaimed immediately, without waiting for
            // its log to grow stale.
            return true;
        }

        // No pid file references this session at all. It may be a brand-new
        // session whose pid file hasn't been flushed yet, or one that already
        // ended (file removed). Reclaim only if its log is also clearly stale,
        // so we never kill a just-started session that simply hasn't materialised
        // a pid file yet.
        if (lastActivityUtc.TryGetValue(sessionId, out var last))
        {
            return (DateTime.UtcNow - last) > AbandonedSessionTimeout;
        }
        // Never saw activity for it — defensive: treat as stale.
        return true;
    }

    // WorkBuddy splits session logs by calendar day, but a session started
    // before midnight keeps writing to that day's folder after midnight. We
    // must watch BOTH today's and yesterday's folders, otherwise a running
    // session that crosses midnight silently disappears from monitoring.
    private IReadOnlyList<string> GetLogDirectories()
    {
        var dirs = new List<string>();

        // 1a. Constructor override (code-injected, trusted). Tests pass temp
        //     fixtures here directly, so this path is NOT attacker-controllable
        //     and must bypass the trust check — otherwise the monitor would
        //     ignore its own injected fixtures and 29 regression tests fail.
        if (!string.IsNullOrWhiteSpace(logsDirectoryOverride))
        {
            AddRecentDayDirs(dirs, logsDirectoryOverride);
            return dirs;
        }

        // 1b. Env var override: AGENT_SIGNAL_WORKBUDDY_LOGS_DIR.
        //     This IS attacker-controllable, so only honor it when it points at
        //     a trusted root (user .workbuddy/logs, ProgramData/WorkBuddy/users,
        //     or the app's own directory). Rejecting other locations prevents a
        //     malicious env value from pointing at a forged log that injects
        //     fake busy/blocked state into the status light.
        string? envBase = Environment.GetEnvironmentVariable("AGENT_SIGNAL_WORKBUDDY_LOGS_DIR");
        if (!string.IsNullOrWhiteSpace(envBase) && IsTrustedLogRoot(envBase))
        {
            AddRecentDayDirs(dirs, envBase);
            return dirs;
        }

        // 2. User profile .workbuddy/logs (legacy / non-sandboxed installs).
        var userBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".workbuddy",
            "logs");
        AddRecentDayDirs(dirs, userBase);

        // 3. WorkBuddy sandboxed data root. When LOCALAPPDATA is redirected to
        //    C:\ProgramData\WorkBuddy\users\<id>\..., a desktop monitor running
        //    under the real user profile cannot see the live logs there, so we
        //    must scan ProgramData explicitly.
        foreach (var root in EnumerateWorkBuddyDataRoots())
        {
            var pb = Path.Combine(root, ".workbuddy", "logs");
            if (Directory.Exists(pb))
            {
                AddRecentDayDirs(dirs, pb);
            }
        }

        return dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Adds the most recently written day folders (yyyy-MM-dd) under basePath.
    /// WorkBuddy stores a session's log in the folder for the day the file was
    /// created, so a session crossing midnight keeps its log in the previous
    /// day's folder. We keep the 3 most recently written day folders to cover
    /// such sessions without scanning the whole history.
    /// </summary>
    private static void AddRecentDayDirs(List<string> dirs, string basePath)
    {
        if (!Directory.Exists(basePath))
        {
            return;
        }

        foreach (var d in Directory.GetDirectories(basePath)
                     .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^\d{4}-\d{2}-\d{2}$"))
                     .OrderByDescending(d => new DirectoryInfo(d).LastWriteTimeUtc)
                     .Take(3))
        {
            dirs.Add(d);
        }
    }

    /// <summary>
    /// Enumerates WorkBuddy real data roots under C:\ProgramData\WorkBuddy\users.
    /// </summary>
    private static IEnumerable<string> EnumerateWorkBuddyDataRoots()
    {
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var wbUsers = Path.Combine(common, "WorkBuddy", "users");
        if (Directory.Exists(wbUsers))
        {
            foreach (var userDir in Directory.GetDirectories(wbUsers))
            {
                yield return userDir;
            }
        }
    }

    /// <summary>
    /// Returns the set of session IDs whose log shows them as currently busy.
    /// </summary>
    public IReadOnlySet<string> BusySessionIds => new HashSet<string>(
        busyBySession.Where(kv => kv.Value).Select(kv => kv.Key),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the set of session IDs that are currently waiting for tool permission approval.
    /// </summary>
    public IReadOnlySet<string> BlockedSessionIds => new HashSet<string>(
        blockedSessions,
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Polls today's session log files for new state transitions and permission events.
    /// Returns any sessions whose busy or blocked state changed.
    /// </summary>
    public (IReadOnlyList<WorkBuddyStateChange> StateChanges, IReadOnlyList<WorkBuddyBlockedChange> BlockedChanges) Poll()
    {
        var changes = new List<WorkBuddyStateChange>();
        var blockedChanges = new List<WorkBuddyBlockedChange>();

        IReadOnlyList<FileInfo> logFiles;
        try
        {
            logFiles = GetLogFiles();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AgentSignalDot] GetLogFiles error: {ex}");
            return (changes, blockedChanges);
        }

        if (!primed)
        {
            Prime(logFiles);
            primed = true;

            // Re-reading very old log files (up to 3 day directories) can resurrect
            // sessions whose last transition was busy=true hours/days ago. Force
            // back to idle only sessions that are HARD-DEAD (silent for the
            // full safety timeout) — never a session that is merely between
            // transitions, or the light would go dark mid-task.
            var pruneBefore = DateTime.UtcNow - BusyHardDeadTimeout;
            foreach (var sid in busyBySession.Keys.ToArray())
            {
                if (busyBySession[sid] && lastActivityUtc.TryGetValue(sid, out var last) && last < pruneBefore)
                {
                    busyBySession[sid] = false;
                    changes.Add(new WorkBuddyStateChange(sid, false));
                    if (blockedSessions.Remove(sid))
                    {
                        blockedChanges.Add(new WorkBuddyBlockedChange(sid, false));
                    }
                }
            }

            // Do not revive a block/busy state for a session that is provably dead
            // (cancelled/closed — no live process and a stale log). Without this,
            // a 2-day-old cancelled session would re-light the dot on every restart
            // because Prime rebuilds state from the old log and the block sticks.
            // Moved here (rather than inside Prime()) so the clearing
            // BlockedChange(false)/StateChange(false) emissions can reach the store
            // and overwrite stale records left from the previous tray run.
            if (enableSessionLiveness)
            {
                RefreshSessionPidMapIfStale();
                foreach (var sid in blockedSessions.ToArray())
                {
                    if (IsSessionProvablyDead(sid))
                    {
                        blockedSessions.Remove(sid);
                        blockedChanges.Add(new WorkBuddyBlockedChange(sid, false));
                    }
                }
                foreach (var sid in busyBySession.Keys.ToArray())
                {
                    if (busyBySession[sid] && IsSessionProvablyDead(sid))
                    {
                        busyBySession[sid] = false;
                        changes.Add(new WorkBuddyStateChange(sid, false));
                    }
                }
            }

            // Emit the full initial state so the store catches up immediately.
            // Prime reconstructed busyBySession and blockedSessions from the logs;
            // without this the store stays stale until the next transition arrives.
            foreach (var kv in busyBySession.Where(x => x.Value))
            {
                changes.Add(new WorkBuddyStateChange(kv.Key, true));
            }
            foreach (var sid in blockedSessions)
            {
                blockedChanges.Add(new WorkBuddyBlockedChange(sid, true));
            }

            return (changes, blockedChanges);
        }

        var now = DateTime.UtcNow;
        var resolvedPermissionSessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in logFiles)
        {
            IReadOnlyList<string> lines;
            try
            {
                lines = ReadNewLines(file.FullName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AgentSignalDot] read error {file.FullName}: {ex}");
                continue;
            }

            foreach (var line in lines)
            {
                // Check for state machine transitions first.
                var stateChange = ParseStateChange(line);
                if (stateChange is not null)
                {
                var sid = stateChange.SessionId;
                var previousBusy = busyBySession.TryGetValue(sid, out var old) && old;
                busyBySession[sid] = stateChange.IsBusy;
                var ts = TryParseLogTimestamp(line) ?? DateTime.UtcNow;
                lastActivityUtc[sid] = ts;
                if (ts > _lastAnyTransitionUtc)
                {
                    _lastAnyTransitionUtc = ts;
                }

                // A fresh busy transition proves the session is actively working
                // again. In WorkBuddy that is mutually exclusive with still
                // awaiting tool permission — a blocked agent cannot simultaneously
                // be producing new busy activity. So any prior blocked/permission
                // state for this same session is stale and must be cleared so the
                // light returns to green (busy wins over block). This is what
                // resolves the "ghost block" where FullAccess auto-approves a tool
                // but no explicit TOOL_STARTED / Auto-approving line reaches us:
                // the next busy event for the session is enough to flip it green.
                if (stateChange.IsBusy)
                {
                    pendingPermissionSessions.Remove(sid);
                    if (blockedSessions.Remove(sid))
                    {
                        blockedChanges.Add(new WorkBuddyBlockedChange(sid, false));
                    }
                }

                // Emit a state change only when the busy state actually flipped.
                if (stateChange.IsBusy != previousBusy)
                {
                    changes.Add(stateChange);
                }

                // An explicit user cancellation voids any pending permission
                // block and ends the session. Treat it as a hard "session is
                // done" signal even though its transition carries busy=false
                // (which by itself would NOT clear a stuck block).
                if (CancelRequestedPattern.IsMatch(line))
                {
                    pendingPermissionSessions.Remove(sid);
                    if (blockedSessions.Remove(sid))
                    {
                        blockedChanges.Add(new WorkBuddyBlockedChange(sid, false));
                    }
                }

                if (ToolStartedPattern.IsMatch(line))
                    {
                        toolExecutingSessions.Add(sid);
                        lastToolSession = sid;
                        // Tool started — any pending/blocked permission for this
                        // session was resolved (user approved or auto-approved).
                        pendingPermissionSessions.Remove(sid);
                        if (blockedSessions.Remove(sid))
                        {
                            blockedChanges.Add(new WorkBuddyBlockedChange(sid, false));
                        }
                    }
                    else if (ToolEndingEventPattern.IsMatch(line))
                    {
                        toolExecutingSessions.Remove(sid);
                        if (lastToolSession == sid) lastToolSession = null;
                        // A tool lifecycle end resolves any pending permission.
                        pendingPermissionSessions.Remove(sid);
                        if (blockedSessions.Remove(sid))
                        {
                            blockedChanges.Add(new WorkBuddyBlockedChange(sid, false));
                        }
                    }
                    else if (PermissionWaitingPattern.IsMatch(line))
                    {
                        // Agent entered waiting_for_permission. Defer the blocking
                        // decision until we know whether it was auto-approved.
                        // Skip if this session was already auto-approved in this batch.
                        lastToolSession = sid;
                        if (!resolvedPermissionSessionIds.Contains(sid))
                        {
                            pendingPermissionSessions.Add(sid);
                        }
                    }

                    continue;
                }

                // Permission asks outside a state transition line. Defer the block:
                // in FullAccess mode an Auto-approving line follows immediately, so
                // we must not block a session that was never actually paused.
                if (PermissionAskPattern.IsMatch(line) && lastToolSession is not null)
                {
                    pendingPermissionSessions.Add(lastToolSession);
                }
                else if (PermissionAutoApprovedPattern.IsMatch(line))
                {
                    var sid = PermissionAutoApprovedPattern.Match(line).Groups["sessionId"].Value;
                    if (!string.IsNullOrWhiteSpace(sid))
                    {
                        resolvedPermissionSessionIds.Add(sid);
                        pendingPermissionSessions.Remove(sid);
                        if (blockedSessions.Remove(sid))
                        {
                            blockedChanges.Add(new WorkBuddyBlockedChange(sid, false));
                        }
                    }
                }
            }
        }

        // Resolve deferred permission requests. Anything still pending after all
        // lines were read was NOT auto-approved and is genuinely awaiting the user.
        foreach (var sid in pendingPermissionSessions)
        {
            if (blockedSessions.Add(sid))
            {
                blockedChanges.Add(new WorkBuddyBlockedChange(sid, true));
            }
        }
        pendingPermissionSessions.Clear();

        // A busy session whose log stopped producing transitions is no longer
        // actually working, even if its last logged state was busy=true (some
        // sessions end without emitting a clean busy=false transition). Drop such
        // stale busy sessions so the dot does not stay green forever. Sessions
        // actively executing a tool are exempt — a long-running tool may produce
        // no log lines for a while and must not be flapped to idle.
        foreach (var sid in busyBySession.Keys.ToArray())
        {
            if (!busyBySession[sid])
            {
                continue;
            }

            if (toolExecutingSessions.Contains(sid))
            {
                continue;
            }

            // A session genuinely awaiting tool-permission approval must stay
            // red even when its log goes silent — it is blocked on the user, not
            // idle. Never stale-clear a blocked session, or a "waiting for your
            // approval" prompt would silently vanish from the dot.
            if (blockedSessions.Contains(sid))
            {
                continue;
            }

            // Only force a busy session back to idle when it has been HARD-DEAD
            // for the full safety timeout — i.e. no transition AND no tool
            // executing for 30 minutes. Any shorter window would flip a live
            // A busy session that has been completely silent (no transition at
            // all) for longer than AbandonedSessionTimeout fires the silence
            // gate — the task is either finished without a clean busy=false, or
            // was interrupted and never logged AGENT_ENDED. This replaces the
            // two-tier hard-dead (30 min) + cross-session orphaned (5 min) logic
            // with a single absolute 5-min gate that works equally for solo and
            // multi-session scenarios. A genuinely-running long task that happens
            // to produce no transition for > 5 min will briefly go dark but
            // self-heals on the next poll when it writes new activity.
            if (lastActivityUtc.TryGetValue(sid, out var last))
            {
                if ((now - last) > AbandonedSessionTimeout)
                {
                    busyBySession[sid] = false;
                    pendingPermissionSessions.Remove(sid);
                    blockedSessions.Remove(sid);
                    changes.Add(new WorkBuddyStateChange(sid, false));
                }
            }
        }

        // Session liveness gate: a busy/blocked session whose process is
        // provably dead (no live pid AND log silent beyond the hard-dead
        // window) is cleared so a cancelled/abandoned session cannot keep the
        // dot red or green forever. This is the runtime counterpart to the
        // Prime-time prune below.
        if (enableSessionLiveness)
        {
            RefreshSessionPidMapIfStale();
            foreach (var sid in busyBySession.Keys.ToArray())
            {
                var isBusy = busyBySession[sid];
                var isBlocked = blockedSessions.Contains(sid);
                if (!isBusy && !isBlocked)
                {
                    continue;
                }
                if (IsSessionProvablyDead(sid))
                {
                    busyBySession[sid] = false;
                    if (blockedSessions.Remove(sid))
                    {
                        blockedChanges.Add(new WorkBuddyBlockedChange(sid, false));
                    }
                    if (isBusy)
                    {
                        changes.Add(new WorkBuddyStateChange(sid, false));
                    }
                }
            }
        }

        return (changes, blockedChanges);
    }

    private WorkBuddyStateChange? ParseStateChange(string line)
    {
        var match = StateTransitionPattern.Match(line);
        if (!match.Success)
        {
            return null;
        }

        var sessionId = match.Groups["sessionId"].Value;
        var isBusy = bool.Parse(match.Groups["busy"].Value);

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        return new WorkBuddyStateChange(sessionId, isBusy);
    }

    private void Prime(IReadOnlyList<FileInfo> logFiles)
    {
        // Local set so permission patterns across all files are resolved together.
        var pendingPermissionSessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedPermissionSessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in logFiles)
        {
            try
            {
                file.Refresh();

                // Snapshot the file length BEFORE we start reading. If WorkBuddy
                // appends more data while we are still reading (very likely for
                // large log files), file.Length at the end would include those new
                // bytes, and setting our offset to that value would permanently
                // skip them. Using the pre-read length ensures ReadNewLines picks
                // up everything written after this snapshot, regardless of how
                // long the prime read takes.
                var lengthBeforeRead = file.Length;

                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    var change = ParseStateChange(line);
                    if (change is not null)
                    {
                        var sid = change.SessionId;
                        busyBySession[sid] = change.IsBusy;
                        // A busy transition during prime means the session is actively
                        // working, so any pending/blocked permission state for it is
                        // stale — a working agent cannot be awaiting permission.
                        if (change.IsBusy)
                        {
                            pendingPermissionSessions.Remove(sid);
                        }
                        // Prime seeds staleness from the transition's real timestamp, not
                        // the file mtime (keepalive writes keep the mtime fresh).
                        var primeTs = TryParseLogTimestamp(line) ?? file.LastWriteTimeUtc;
                        lastActivityUtc[sid] = primeTs;
                        if (primeTs > _lastAnyTransitionUtc)
                        {
                            _lastAnyTransitionUtc = primeTs;
                        }

#if DEBUG
                        // Temporary debug: log what file+line produced which state.
                        DebugWrite($"[PRIME] {Path.GetFileName(file.FullName)} → sid={sid} busy={change.IsBusy} line={line[..Math.Min(120, line.Length)]}");
#endif

                        if (ToolStartedPattern.IsMatch(line))
                        {
                            toolExecutingSessions.Add(sid);
                            lastToolSession = sid;
                        }
                        else if (ToolEndingEventPattern.IsMatch(line))
                        {
                            toolExecutingSessions.Remove(sid);
                            if (lastToolSession == sid) lastToolSession = null;
                            // A tool lifecycle end resolves any pending permission.
                            pendingPermissionSessions.Remove(sid);
                        }
                        else if (PermissionWaitingPattern.IsMatch(line))
                        {
                            // Agent entered waiting_for_permission. Defer blocking
                            // until we know whether it was auto-approved.
                            lastToolSession = sid;
                            if (!resolvedPermissionSessionIds.Contains(sid))
                            {
                                pendingPermissionSessions.Add(sid);
                            }
                        }
                    }

                    // Permission patterns that can appear outside state-transition lines.
                    if (PermissionAskPattern.IsMatch(line) && lastToolSession is not null)
                    {
                        pendingPermissionSessions.Add(lastToolSession);
                    }
                    else if (PermissionAutoApprovedPattern.IsMatch(line))
                    {
                        var approvedSid = PermissionAutoApprovedPattern.Match(line).Groups["sessionId"].Value;
                        if (!string.IsNullOrWhiteSpace(approvedSid))
                        {
                            resolvedPermissionSessionIds.Add(approvedSid);
                            pendingPermissionSessions.Remove(approvedSid);
                        }
                    }
                }

                // Use the pre-read length — data appended during the read will be
                // picked up by ReadNewLines on the very next Poll cycle.
                offsetsByPath[file.FullName] = lengthBeforeRead;
            }
            catch (Exception ex)
            {
                // A single unreadable file (locked/rotated by WorkBuddy) must not
                // abort priming for every other session's log.
                System.Diagnostics.Debug.WriteLine($"[AgentSignalDot] prime error {file.FullName}: {ex}");
            }
        }

        // Resolve deferred permission requests. Anything still pending after all
        // files were read was NOT auto-approved and is genuinely awaiting the user.
        foreach (var sid in pendingPermissionSessions)
        {
            blockedSessions.Add(sid);
#if DEBUG
            DebugWrite($"[PRIME] blocked session resolved: sid={sid}");
#endif
        }
    }

    private IReadOnlyList<string> ReadNewLines(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            fileInfo.Refresh();

            if (!fileInfo.Exists)
            {
                return [];
            }

            if (!offsetsByPath.TryGetValue(path, out var offset))
            {
                offsetsByPath[path] = fileInfo.Length;
                return [];
            }

            if (fileInfo.Length <= offset)
            {
                if (fileInfo.Length < offset)
                {
                    // File was truncated — reset.
                    offsetsByPath[path] = 0;
                }
                return [];
            }

            // Snapshot length before reading (same race-condition fix as Prime):
            // data appended while we read must not be skipped permanently.
            var lengthBeforeRead = fileInfo.Length;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }
            }

            // Use pre-read length so the next poll catches any data appended
            // during this read.
            offsetsByPath[path] = lengthBeforeRead;
            return lines;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AgentSignalDot] ReadNewLines error {path}: {ex}");
            return [];
        }
    }

    private IReadOnlyList<FileInfo> GetLogFiles()
    {
        var files = new List<FileInfo>();
        foreach (var dir in GetLogDirectories())
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(dir, "*.log", SearchOption.TopDirectoryOnly))
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists)
                    {
                        files.Add(fi);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AgentSignalDot] enumerate error {dir}: {ex}");
            }
        }

        return files.OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
    }

    /// <summary>
    /// 校验日志根目录是否位于受信位置：用户 .workbuddy/logs、ProgramData/WorkBuddy/users、
    /// 或程序自身目录。拒绝其它位置（如攻击者可控的目录），避免伪造日志注入。
    /// </summary>
    private static bool IsTrustedLogRoot(string basePath)
    {
        try
        {
            var full = Path.GetFullPath(basePath);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var allowed = new List<string>();
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                allowed.Add(Path.Combine(Path.GetFullPath(userProfile), ".workbuddy"));
            }
            if (!string.IsNullOrWhiteSpace(common))
            {
                allowed.Add(Path.Combine(Path.GetFullPath(common), "WorkBuddy", "users"));
            }
            allowed.Add(Path.GetFullPath(AppContext.BaseDirectory));
            return allowed.Any(r => full == r || full.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}

public sealed record WorkBuddyStateChange(string SessionId, bool IsBusy);

public sealed record WorkBuddyBlockedChange(string SessionId, bool IsBlocked);
