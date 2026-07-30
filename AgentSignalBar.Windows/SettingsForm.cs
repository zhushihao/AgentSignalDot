using AgentSignalBar.Core;
using Microsoft.Win32;

namespace AgentSignalBar.Windows;

internal sealed class SettingsForm : Form
{
    private readonly SignalStateStore store;
    private readonly Action<bool> setFloatingSignalVisible;
    private readonly Action? repositionFloatingSignal;
    private readonly Label statusLabel = new();
    private readonly ListView sessionList = new();
    private readonly ComboBox backdropCombo = new();
    private readonly Label diagnosticsSummaryLabel = new();
    private readonly ListView diagnosticsList = new();
    private SignalSnapshot snapshot;

    public SettingsForm(SignalStateStore store, SignalSnapshot snapshot, Action<bool> setFloatingSignalVisible, Action? repositionFloatingSignal = null)
    {
        this.store = store;
        this.snapshot = snapshot;
        this.setFloatingSignalVisible = setFloatingSignalVisible;
        this.repositionFloatingSignal = repositionFloatingSignal;
        Text = "Agent Signal Dot";
        MinimumSize = new Size(820, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(246, 247, 249);
        ForeColor = Color.FromArgb(31, 41, 55);
        Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        BuildUi();
        UpdateSnapshot(snapshot);
        ApplySavedBackdrop();
    }

    public void UpdateSnapshot(SignalSnapshot next)
    {
        snapshot = next;
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateSnapshot(next));
            return;
        }

        statusLabel.Text = $"当前状态: {AgentSignalParser.ToRawValue(snapshot.Aggregate)}    状态文件: {snapshot.StateFilePath}";
        sessionList.Items.Clear();
        foreach (var session in snapshot.Sessions)
        {
            sessionList.Items.Add(new ListViewItem([
                session.SessionId,
                session.Agent ?? "",
                AgentSignalParser.ToRawValue(session.Signal),
                session.LastEvent ?? "",
                session.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
            ]));
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplySavedBackdrop();
    }

    private void BuildUi()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(12, 6)
        };
        tabs.TabPages.Add(ActivityPage());
        tabs.TabPages.Add(GeneralPage());
        tabs.TabPages.Add(ConnectionsPage());
        tabs.TabPages.Add(DiagnosticsPage());
        tabs.TabPages.Add(AdvancedPage());
        tabs.TabPages.Add(AboutPage());
        Controls.Add(tabs);
    }

    private TabPage ActivityPage()
    {
        var page = new TabPage("活动");
        statusLabel.Dock = DockStyle.Top;
        statusLabel.Height = 54;
        statusLabel.Padding = new Padding(14);
        statusLabel.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point);

        sessionList.Dock = DockStyle.Fill;
        sessionList.View = View.Details;
        sessionList.FullRowSelect = true;
        sessionList.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        sessionList.Columns.Add("Session", 200);
        sessionList.Columns.Add("Agent", 140);
        sessionList.Columns.Add("Signal", 120);
        sessionList.Columns.Add("Event", 180);
        sessionList.Columns.Add("Updated", 170);

        page.Controls.Add(sessionList);
        page.Controls.Add(statusLabel);
        return page;
    }

    private TabPage GeneralPage()
    {
        var page = new TabPage("通用");
        var panel = StackPanel();
        var launchAtLogin = new CheckBox
        {
            Text = "开机启动",
            Checked = LaunchAtLoginManager.IsEnabled(),
            AutoSize = true
        };
        launchAtLogin.CheckedChanged += (_, _) => LaunchAtLoginManager.SetEnabled(launchAtLogin.Checked);
        panel.Controls.Add(launchAtLogin);
        var floatingSignal = new CheckBox
        {
            Text = "显示桌面悬浮灯",
            Checked = Properties.Settings.Default.ShowFloatingSignal,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        floatingSignal.CheckedChanged += (_, _) => setFloatingSignalVisible(floatingSignal.Checked);
        panel.Controls.Add(floatingSignal);

        var lockPosition = new CheckBox
        {
            Text = "锁定悬浮灯位置（禁用拖拽）",
            Checked = Properties.Settings.Default.IsPositionLocked,
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0)
        };
        lockPosition.CheckedChanged += (_, _) =>
        {
            Properties.Settings.Default.IsPositionLocked = lockPosition.Checked;
            Properties.Settings.Default.Save();
            repositionFloatingSignal?.Invoke();
        };
        panel.Controls.Add(lockPosition);

        panel.Controls.Add(new Label
        {
            Text = "提示：取消锁定后可用鼠标拖拽悬浮灯；位置会自动保存。",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(107, 114, 128)
        });

        panel.Controls.Add(new Label { Text = "窗口效果", AutoSize = true, Margin = new Padding(0, 16, 0, 4) });
        backdropCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        backdropCombo.Items.AddRange(["纯色", "Mica", "Acrylic"]);
        backdropCombo.SelectedIndexChanged += (_, _) =>
        {
            SaveBackdrop();
            ApplySavedBackdrop();
        };
        panel.Controls.Add(backdropCombo);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage ConnectionsPage()
    {
        var page = new TabPage("连接");
        var panel = StackPanel();
        panel.Controls.Add(Button("安装/检查 Claude Code Hooks", () =>
        {
            var preview = HookConfigInstaller.InstallClaude(WindowsUserPaths.HomeDirectory(), WindowsCliLocator.FindAgentSignalCli());
            MessageBox.Show($"Claude Code hooks 已写入:\n{preview.Path}", "Agent Signal Dot");
        }));
        panel.Controls.Add(Button("安装/检查 Codex Hooks", () =>
        {
            var path = Path.Combine(WindowsUserPaths.HomeDirectory(), ".codex", "hooks.json");
            var preview = HookConfigInstaller.InstallCodex(path, WindowsCliLocator.FindAgentSignalCli());
            MessageBox.Show($"Codex hooks 已写入:\n{preview.Path}", "Agent Signal Dot");
        }));
        panel.Controls.Add(Button("安装/检查 WorkBuddy Hooks", () =>
        {
            var preview = HookConfigInstaller.InstallCodeBuddy(WindowsUserPaths.HomeDirectory(), WindowsCliLocator.FindAgentSignalCli());
            MessageBox.Show($"WorkBuddy / CodeBuddy hooks 已写入:\n{preview.Path}", "Agent Signal Dot");
        }));
        panel.Controls.Add(Button("打开状态文件目录", () =>
        {
            var directory = Path.GetDirectoryName(store.StateFilePath) ?? ".";
            Directory.CreateDirectory(directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }));
        page.Controls.Add(panel);
        return page;
    }

    private TabPage DiagnosticsPage()
    {
        var page = new TabPage("诊断");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        actions.Controls.Add(Button("刷新诊断", RefreshDiagnostics));
        actions.Controls.Add(Button("运行状态自测", RunSignalSelfTest));

        diagnosticsSummaryLabel.AutoSize = true;
        diagnosticsSummaryLabel.Margin = new Padding(0, 0, 0, 12);
        diagnosticsSummaryLabel.Text = "点击刷新诊断，检查 hooks、状态文件、Codex 日志和 WorkBuddy 日志。";
        diagnosticsSummaryLabel.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point);

        diagnosticsList.Dock = DockStyle.Fill;
        diagnosticsList.View = View.Details;
        diagnosticsList.FullRowSelect = true;
        diagnosticsList.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        diagnosticsList.Columns.Add("项目", 170);
        diagnosticsList.Columns.Add("状态", 100);
        diagnosticsList.Columns.Add("摘要", 200);
        diagnosticsList.Columns.Add("详情", 520);

        layout.Controls.Add(actions, 0, 0);
        layout.Controls.Add(diagnosticsSummaryLabel, 0, 1);
        layout.Controls.Add(diagnosticsList, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private void RefreshDiagnostics()
    {
        diagnosticsList.Items.Clear();
        try
        {
            var report = ConnectionHealthCheck.Run(DiagnosticsOptions());
            diagnosticsSummaryLabel.Text = report.HasProblems
                ? $"诊断完成：{report.Items.Count(item => item.Severity != ConnectionHealthSeverity.Ok)} 项需要检查。"
                : "诊断完成：连接状态正常。";

            foreach (var item in report.Items)
            {
                diagnosticsList.Items.Add(new ListViewItem([
                    item.Name,
                    SeverityText(item.Severity),
                    item.Summary,
                    item.Detail
                ]));
            }
        }
        catch (Exception error)
        {
            diagnosticsSummaryLabel.Text = "诊断失败。";
            diagnosticsList.Items.Add(new ListViewItem([
                "诊断",
                "错误",
                error.GetType().Name,
                error.Message
            ]));
        }
    }

    private void RunSignalSelfTest()
    {
        diagnosticsList.Items.Clear();
        var report = SignalSelfTestRunner.Run(store);
        UpdateSnapshot(report.FinalSnapshot);
        diagnosticsSummaryLabel.Text = report.Passed
            ? $"状态自测通过：{report.Results.Count} 项状态全部可写可读。"
            : $"状态自测发现问题：{report.Results.Count(result => !result.Passed)} 项失败。";

        foreach (var result in report.Results)
        {
            diagnosticsList.Items.Add(new ListViewItem([
                result.Name,
                result.Passed ? "通过" : "失败",
                AgentSignalParser.ToRawValue(result.Signal),
                $"{result.Detail}; expected={result.ExpectedDisplayState}; actual={result.ActualDisplayState}"
            ]));
        }
    }

    private ConnectionHealthCheckOptions DiagnosticsOptions()
    {
        var home = WindowsUserPaths.HomeDirectory();
        return new ConnectionHealthCheckOptions(
            HomeDirectory: home,
            AgentSignalCliPath: WindowsCliLocator.FindAgentSignalCli(),
            StateFilePath: store.StateFilePath,
            CodexHooksPath: Path.Combine(home, ".codex", "hooks.json"),
            CodexSessionsDirectory: CodexSessionLogMonitor.DefaultSessionRoot());
    }

    private static string SeverityText(ConnectionHealthSeverity severity)
    {
        return severity switch
        {
            ConnectionHealthSeverity.Ok => "正常",
            ConnectionHealthSeverity.Warning => "注意",
            ConnectionHealthSeverity.Missing => "缺失",
            ConnectionHealthSeverity.Error => "错误",
            _ => severity.ToString()
        };
    }

    private TabPage AdvancedPage()
    {
        var page = new TabPage("高级");
        var panel = StackPanel();
        panel.Controls.Add(Button("清除提醒", () => UpdateSnapshot(store.ClearWarnings())));
        panel.Controls.Add(Button("重置为空闲", () => UpdateSnapshot(store.ClearSessions())));
        foreach (var testCase in ManualSignalTestCase.All)
        {
            panel.Controls.Add(Button(testCase.ButtonText, () => UpdateSnapshot(ApplyManualTestSignal(testCase))));
        }
        page.Controls.Add(panel);
        return page;
    }

    private SignalSnapshot ApplyManualTestSignal(ManualSignalTestCase testCase)
    {
        return testCase.Signal.DisplayState() is DisplayState.Ready or DisplayState.Paused
            ? store.SetManualSignal(testCase.Signal)
            : store.ApplySessionSignal(
                testCase.Signal,
                ManualSignalTestCase.SessionId,
                ManualSignalTestCase.Agent,
                ManualSignalTestCase.Event);
    }

    private TabPage AboutPage()
    {
        var page = new TabPage("关于");
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular, GraphicsUnit.Point),
            Text = "Agent Signal Dot for Windows\n本地 AI Agent 的 Windows 托盘信号灯。\n\n支持 Claude Code / Codex / WorkBuddy (CodeBuddy) hooks，本地状态文件和 Windows Mica/Acrylic 窗口效果。"
        };
        page.Controls.Add(label);
        return page;
    }

    private static FlowLayoutPanel StackPanel()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(18),
            AutoScroll = true
        };
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            Width = 260,
            Height = 40,
            Margin = new Padding(0, 0, 0, 12),
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void ApplySavedBackdrop()
    {
        var mode = SavedBackdropMode();
        backdropCombo.SelectedIndex = mode switch
        {
            WindowsBackdropMode.Mica => 1,
            WindowsBackdropMode.Acrylic => 2,
            _ => 0
        };

        var applied = WindowsBackdrop.Apply(this, mode, darkMode: false);
        if (!applied || mode == WindowsBackdropMode.Solid)
        {
            BackColor = Color.FromArgb(246, 247, 249);
        }
        else
        {
            BackColor = Color.FromArgb(235, 240, 246);
        }
    }

    private WindowsBackdropMode SavedBackdropMode()
    {
        return Properties.Settings.Default.BackdropMode switch
        {
            "Mica" => WindowsBackdropMode.Mica,
            "Acrylic" => WindowsBackdropMode.Acrylic,
            _ => WindowsBackdropMode.Solid
        };
    }

    private void SaveBackdrop()
    {
        Properties.Settings.Default.BackdropMode = backdropCombo.SelectedIndex switch
        {
            1 => "Mica",
            2 => "Acrylic",
            _ => "Solid"
        };
        Properties.Settings.Default.Save();
    }
}

internal static class LaunchAtLoginManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AgentSignalBar";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && value.Contains(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
