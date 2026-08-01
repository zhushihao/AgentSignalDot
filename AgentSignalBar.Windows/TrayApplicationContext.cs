using System.Linq;
using AgentSignalBar.Core;

namespace AgentSignalBar.Windows;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SignalStateStore store = new();
    private readonly NotifyIcon notifyIcon;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private readonly FileSystemWatcher watcher;
    private readonly CodexSessionLogMonitor codexSessionLogMonitor = CodexSessionLogMonitor.ForCurrentUser();
    private readonly WorkBuddySessionMonitor workBuddySessionMonitor = new();
    private readonly WorkBuddySessionLogMonitor workBuddySessionLogMonitor = new(enableSessionLiveness: true);
    private readonly string stateDirectory;
    private SignalSnapshot snapshot;
    private SettingsForm? settingsForm;
    private FloatingSignalForm? floatingForm;
    private bool monitoringPaused;
    private int tick;

    public TrayApplicationContext()
    {
        snapshot = store.ReadSnapshot();

        // If the previous session ended in a paused/off state but monitoring
        // is now active, clear the stale aggregate.
        if (snapshot.Aggregate is AgentSignal.Off or AgentSignal.Pause or AgentSignal.Paused)
        {
            snapshot = store.SetManualSignal(AgentSignal.Idle);
        }
        stateDirectory = Path.GetDirectoryName(store.StateFilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Directory.CreateDirectory(stateDirectory);

        notifyIcon = new NotifyIcon
        {
            Text = "Agent Signal Dot",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        notifyIcon.DoubleClick += (_, _) => ShowSettings();
        if (Properties.Settings.Default.ShowFloatingSignal)
        {
            ShowFloatingSignal();
        }

        watcher = new FileSystemWatcher(stateDirectory)
        {
            Filter = Path.GetFileName(store.StateFilePath),
            EnableRaisingEvents = true,
            IncludeSubdirectories = false
        };
        watcher.Changed += (_, _) => Reload();
        watcher.Created += (_, _) => Reload();
        watcher.Renamed += (_, _) => Reload();
        watcher.Deleted += (_, _) => Reload();

        refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 250
        };
        refreshTimer.Tick += (_, _) =>
        {
            tick++;
            if (monitoringPaused)
            {
                UpdateTrayIcon();
                return;
            }

            // A single failed poll must never halt monitoring: the dot stays
            // alive and recovers on the next tick once the transient error clears.
            try
            {
                PollCodexSessionLogs();
                // WorkBuddy / CodeBuddy tracking is a user-toggleable feature
                // (Settings › 通用 › "跟踪 WorkBuddy / CodeBuddy 状态"). When
                // off we skip both WorkBuddy pollers so the dot stops reflecting
                // WorkBuddy activity. The setting is read every tick, so the
                // toggle takes effect on the next poll without a restart.
                if (Properties.Settings.Default.TrackWorkBuddy)
                {
                    PollWorkBuddySessionLogs();
                    PollWorkBuddySessions();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AgentSignalDot] poll error: {ex}");
            }

            try
            {
                Reload();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AgentSignalDot] reload error: {ex}");
            }

            UpdateTrayIcon();
        };
        refreshTimer.Start();
        UpdateTrayIcon();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refreshTimer.Dispose();
            watcher.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            settingsForm?.Dispose();
            floatingForm?.Dispose();
        }
        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => RebuildMenu(menu);
        RebuildMenu(menu);
        return menu;
    }

    private void RebuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add(new ToolStripMenuItem("Agent Signal Dot") { Enabled = false });
        menu.Items.Add(new ToolStripMenuItem($"状态: {AgentSignalParser.ToRawValue(snapshot.Aggregate)}") { Enabled = false });
        var latest = snapshot.RecentEvents.FirstOrDefault();
        if (latest is not null)
        {
            menu.Items.Add(new ToolStripMenuItem($"最新: {latest.Agent ?? "agent"} · {latest.Event ?? AgentSignalParser.ToRawValue(latest.Signal)}") { Enabled = false });
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem("打开设置", (_, _) => ShowSettings()));
        menu.Items.Add(MenuItem("清除提醒", (_, _) =>
        {
            snapshot = store.ClearWarnings();
            UpdateTrayIcon();
        }));
        menu.Items.Add(MenuItem(monitoringPaused ? "恢复监控" : "暂停监控", (_, _) =>
        {
            monitoringPaused = !monitoringPaused;
            snapshot = monitoringPaused
                ? store.ApplySessionSignal(AgentSignal.Off, "manual", "manual", "Pause")
                : store.SetManualSignal(AgentSignal.Idle);
            UpdateTrayIcon();
        }));
        menu.Items.Add(MenuItem(IsFloatingSignalVisible() ? "隐藏悬浮灯" : "显示悬浮灯", (_, _) => ToggleFloatingSignal()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem("安装/检查 Claude Code Hooks", (_, _) => InstallClaudeHooks()));
        menu.Items.Add(MenuItem("安装/检查 Codex Hooks", (_, _) => InstallCodexHooks()));
        menu.Items.Add(MenuItem("安装/检查 WorkBuddy Hooks", (_, _) => InstallCodeBuddyHooks()));
        menu.Items.Add(MenuItem("打开状态文件目录", (_, _) => OpenStateDirectory()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem("退出", (_, _) => ExitThread()));
    }

    private static ToolStripMenuItem MenuItem(string text, EventHandler onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += onClick;
        return item;
    }

    private void Reload()
    {
        if (monitoringPaused)
        {
            return;
        }

        try
        {
            snapshot = store.ReadSnapshot();
            settingsForm?.UpdateSnapshot(snapshot);
            floatingForm?.UpdateSnapshot(snapshot, tick);
            UpdateTrayIcon();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AgentSignalDot] reload error: {ex}");
        }
    }

    private void PollCodexSessionLogs()
    {
        foreach (var activity in codexSessionLogMonitor.Poll(DateTimeOffset.UtcNow))
        {
            snapshot = store.ApplySessionSignal(
                activity.Signal,
                activity.SessionId,
                activity.Agent,
                activity.Event,
                activity.Timestamp);
        }
    }

    private void PollWorkBuddySessions()
    {
        // Only emit thinking for sessions that the log monitor has confirmed are busy.
        // Sessions without log entries (no SessionRunStateMachine transitions) are
        // treated as idle to avoid persistent false positives from stale heartbeat files.
        var busyIds = workBuddySessionLogMonitor.BusySessionIds;
        foreach (var activity in workBuddySessionMonitor.Poll(DateTimeOffset.UtcNow, busyIds))
        {
            snapshot = store.ApplySessionSignal(
                activity.Signal,
                activity.SessionId,
                activity.Agent,
                activity.Event,
                activity.Timestamp);
        }
    }

    private void PollWorkBuddySessionLogs()
    {
        var (stateChanges, blockedChanges) = workBuddySessionLogMonitor.Poll();

        foreach (var change in stateChanges)
        {
            if (change.IsBusy)
            {
                // Log says the session became busy — emit thinking immediately.
                // Don't wait for the heartbeat monitor; within a single batch
                // the busy state may toggle back to false before the heartbeat
                // monitor runs, swallowing the thinking signal.
                snapshot = store.ApplySessionSignal(
                    AgentSignal.Thinking,
                    $"workbuddy:{change.SessionId}",
                    "workbuddy",
                    "SessionBecameBusy",
                    DateTimeOffset.UtcNow);
            }
            else
            {
                snapshot = store.ApplySessionSignal(
                    AgentSignal.SessionEnd,
                    $"workbuddy:{change.SessionId}",
                    "workbuddy",
                    "SessionBecameIdle",
                    DateTimeOffset.UtcNow);
            }
        }

        foreach (var blocked in blockedChanges)
        {
            var key = $"workbuddy:{blocked.SessionId}";
            if (!blocked.IsBlocked)
            {
                // A genuinely-alive session whose block was resolved — emit
                // Thinking so the dot returns to green immediately.
                snapshot = store.ApplySessionSignal(
                    AgentSignal.Thinking,
                    key,
                    "workbuddy",
                    "ToolPermissionResolved",
                    DateTimeOffset.UtcNow);

                // A dead / non-busy session (no longer in BusySessionIds) must
                // NOT produce a lingering green dot. The Blocked→Thinking write
                // above overrides the stale Blocked record; immediately follow
                // with Idle so the Thinking record is cleared and no green dot
                // remains for a session whose process is already gone.
                if (!workBuddySessionLogMonitor.BusySessionIds.Contains(blocked.SessionId))
                {
                    snapshot = store.ApplySessionSignal(
                        AgentSignal.Idle,
                        key,
                        "workbuddy",
                        "ToolPermissionResolved",
                        DateTimeOffset.UtcNow);
                }
            }
            else
            {
                snapshot = store.ApplySessionSignal(
                    AgentSignal.Blocked,
                    key,
                    "workbuddy",
                    "ToolPermissionBlocked",
                    DateTimeOffset.UtcNow);
            }
        }

        // A session the monitor still lists as busy is mid-work (or merely
        // between transitions within a turn) — keep it lit green. Silence alone
        // must NOT flip it to idle: long model streams and tool calls produce no
        // log lines for minutes at a time, so a busy session going quiet is normal
        // thinking, not "open but idle". The only ways a busy session goes dark
        // are real signals the monitor emits elsewhere:
        //   * a busy=false transition (turn actually ended),
        //   * the orphaned gate (app alive elsewhere, this session silent >5min), or
        //   * the 30-minute hard-dead timeout.
        // We only re-apply thinking when needed (no record yet, or the store shows
        // something other than Active) to avoid re-writing the same signal every poll.
        foreach (var sid in workBuddySessionLogMonitor.BusySessionIds)
        {
            if (workBuddySessionLogMonitor.BlockedSessionIds.Contains(sid))
            {
                continue;
            }

            var key = $"workbuddy:{sid}";
            var record = snapshot.Sessions.FirstOrDefault(s => s.SessionId == key);

            if (record is null || record.Signal.DisplayState() != DisplayState.Active)
            {
                snapshot = store.ApplySessionSignal(
                    AgentSignal.Thinking,
                    key,
                    "workbuddy",
                    "SessionBecameBusy",
                    DateTimeOffset.UtcNow);
            }
        }

        // Mirror of the busy-session fallback above: sessions that were blocked
        // when the monitor started must have their blocked signal written into
        // the store, otherwise a genuinely-blocked session with no new log lines
        // would never light up red until the next permission event arrives.
        foreach (var sid in workBuddySessionLogMonitor.BlockedSessionIds)
        {
            if (workBuddySessionLogMonitor.BusySessionIds.Contains(sid))
            {
                continue;
            }

            var key = $"workbuddy:{sid}";
            var record = snapshot.Sessions.FirstOrDefault(s => s.SessionId == key);
            if (record is null
                || record.Signal.DisplayState() is DisplayState.Blocked or DisplayState.Permission)
            {
                snapshot = store.ApplySessionSignal(
                    AgentSignal.Blocked,
                    key,
                    "workbuddy",
                    "ToolPermissionBlocked",
                    DateTimeOffset.UtcNow);
            }
        }
    }

    private void UpdateTrayIcon()
    {
        var previousIcon = notifyIcon.Icon;
        notifyIcon.Icon = TrayIconRenderer.Render(snapshot.Aggregate, tick);
        previousIcon?.Dispose();
        notifyIcon.Text = $"Agent Signal Dot - {AgentSignalParser.ToRawValue(snapshot.Aggregate)}";
        floatingForm?.UpdateSnapshot(snapshot, tick);
    }

    private void ShowSettings()
    {
        if (settingsForm is { IsDisposed: false })
        {
            settingsForm.Show();
            settingsForm.Activate();
            return;
        }

        settingsForm = new SettingsForm(store, snapshot, SetFloatingSignalVisible, () => floatingForm?.ApplySettings());
        settingsForm.Show();
    }

    private bool IsFloatingSignalVisible()
    {
        return floatingForm is { IsDisposed: false, Visible: true };
    }

    private void ToggleFloatingSignal()
    {
        SetFloatingSignalVisible(!IsFloatingSignalVisible());
    }

    private void SetFloatingSignalVisible(bool visible)
    {
        Properties.Settings.Default.ShowFloatingSignal = visible;
        Properties.Settings.Default.Save();

        if (visible)
        {
            ShowFloatingSignal();
            return;
        }

        floatingForm?.Close();
        floatingForm?.Dispose();
        floatingForm = null;
    }

    private void ShowFloatingSignal()
    {
        if (floatingForm is { IsDisposed: false })
        {
            floatingForm.Show();
            floatingForm.UpdateSnapshot(snapshot, tick);
            return;
        }

        floatingForm = new FloatingSignalForm(snapshot, ShowSettings, () => SetFloatingSignalVisible(false));
        floatingForm.FormClosed += (_, _) =>
        {
            if (floatingForm is { IsDisposed: true })
            {
                floatingForm = null;
            }
        };
        floatingForm.Show();
        floatingForm.UpdateSnapshot(snapshot, tick);
    }

    private void InstallClaudeHooks()
    {
        try
        {
            var cliPath = WindowsCliLocator.FindAgentSignalCli();
            var home = WindowsUserPaths.HomeDirectory();
            var preview = HookConfigInstaller.InstallClaude(home, cliPath);
            MessageBox.Show($"Claude Code hooks 已写入:\n{preview.Path}", "Agent Signal Dot");
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Hook 安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void InstallCodeBuddyHooks()
    {
        try
        {
            var cliPath = WindowsCliLocator.FindAgentSignalCli();
            var home = WindowsUserPaths.HomeDirectory();
            var preview = HookConfigInstaller.InstallCodeBuddy(home, cliPath);
            MessageBox.Show($"WorkBuddy / CodeBuddy hooks 已写入:\n{preview.Path}", "Agent Signal Dot");
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Hook 安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void InstallCodexHooks()
    {
        try
        {
            var cliPath = WindowsCliLocator.FindAgentSignalCli();
            var home = WindowsUserPaths.HomeDirectory();
            var path = Path.Combine(home, ".codex", "hooks.json");
            var preview = HookConfigInstaller.InstallCodex(path, cliPath);
            MessageBox.Show($"Codex hooks 已写入:\n{preview.Path}", "Agent Signal Dot");
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Hook 安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenStateDirectory()
    {
        Directory.CreateDirectory(stateDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = stateDirectory,
            UseShellExecute = true
        });
    }
}
