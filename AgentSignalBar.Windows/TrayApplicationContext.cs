using AgentSignalBar.Core;

namespace AgentSignalBar.Windows;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SignalStateStore store = new();
    private readonly NotifyIcon notifyIcon;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private readonly FileSystemWatcher watcher;
    private readonly CodexSessionLogMonitor codexSessionLogMonitor = CodexSessionLogMonitor.ForCurrentUser();
    private readonly string stateDirectory;
    private SignalSnapshot snapshot;
    private SettingsForm? settingsForm;
    private FloatingSignalForm? floatingForm;
    private bool monitoringPaused;
    private int tick;

    public TrayApplicationContext()
    {
        snapshot = store.ReadSnapshot();
        stateDirectory = Path.GetDirectoryName(store.StateFilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Directory.CreateDirectory(stateDirectory);

        notifyIcon = new NotifyIcon
        {
            Text = "Agent Signal Bar",
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
            Interval = 500
        };
        refreshTimer.Tick += (_, _) =>
        {
            tick++;
            if (!monitoringPaused)
            {
                PollCodexSessionLogs();
                Reload();
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
        menu.Items.Add(new ToolStripMenuItem("Agent Signal Bar") { Enabled = false });
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

        snapshot = store.ReadSnapshot();
        settingsForm?.UpdateSnapshot(snapshot);
        floatingForm?.UpdateSnapshot(snapshot, tick);
        UpdateTrayIcon();
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

    private void UpdateTrayIcon()
    {
        var previousIcon = notifyIcon.Icon;
        notifyIcon.Icon = TrayIconRenderer.Render(snapshot.Aggregate, tick);
        previousIcon?.Dispose();
        notifyIcon.Text = $"Agent Signal Bar - {AgentSignalParser.ToRawValue(snapshot.Aggregate)}";
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
            MessageBox.Show($"Claude Code hooks 已写入:\n{preview.Path}", "Agent Signal Bar");
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
            MessageBox.Show($"Codex hooks 已写入:\n{preview.Path}", "Agent Signal Bar");
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
