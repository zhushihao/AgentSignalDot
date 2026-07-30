using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using AgentSignalBar.Core;

namespace AgentSignalBar.Windows;

internal sealed class FloatingSignalForm : Form
{
    private const int WindowWidth = 10;
    private const int WindowHeight = 10;

    // Win32 常量
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private readonly Action showSettings;
    private readonly Action hideFloatingSignal;
    private readonly ToolTip toolTip = new();
    private readonly System.Windows.Forms.Timer topMostTimer = new();
    private AgentSignal signal;
    private int tick;

    // 拖拽状态
    private bool isDragging;
    private Point dragStartCursor;
    private Point dragStartLocation;
    private bool isPositionLocked;

    public FloatingSignalForm(SignalSnapshot snapshot, Action showSettings, Action hideFloatingSignal)
    {
        this.showSettings = showSettings;
        this.hideFloatingSignal = hideFloatingSignal;
        signal = snapshot.Aggregate;
        isPositionLocked = Properties.Settings.Default.IsPositionLocked;

        Text = "Agent Signal Dot";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(WindowWidth, WindowHeight);
        BackColor = Color.FromArgb(240, 240, 240);
        TransparencyKey = Color.FromArgb(240, 240, 240);
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Cursor = isPositionLocked ? Cursors.Default : Cursors.SizeAll;

        Location = InitialLocation();
        ContextMenuStrip = BuildMenu();
        SetupToolTip();
        WireMouseEvents();
        SetupTopMostGuard();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // 工具窗口：不出现在 Alt+Tab 中，受窗口切换影响更小
            cp.ExStyle |= (int)WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            topMostTimer.Stop();
            topMostTimer.Dispose();
            toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    public void UpdateSnapshot(SignalSnapshot snapshot, int nextTick)
    {
        signal = snapshot.Aggregate;
        tick = nextTick;
        UpdateToolTip();
        Invalidate();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        TopMost = true;

        // Always force position: WinForms DPI translation puts it in wrong spot.
        var sw = GetPhysicalScreenSize();
        var x = Math.Clamp(Properties.Settings.Default.FloatingSignalX, 0, Math.Max(0, sw.width - Width));
        var y = Math.Clamp(Properties.Settings.Default.FloatingSignalY, 0, Math.Max(0, sw.height - Height));
        SetWindowPos(Handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

        Properties.Settings.Default.FloatingSignalX = x;
        Properties.Settings.Default.FloatingSignalY = y;
        Properties.Settings.Default.Save();

        ForceTopMost();
    }

    /// <summary>
    /// 定时重新置顶，防止被其他置顶窗口或系统窗口顶下去。
    /// </summary>
    private void SetupTopMostGuard()
    {
        topMostTimer.Interval = 200;
        topMostTimer.Tick += (_, _) => ForceTopMost();
        topMostTimer.Start();
    }

    private void ForceTopMost()
    {
        if (IsHandleCreated && !IsDisposed)
        {
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }

    private bool GetPhysicalBounds(out RECT rect)
    {
        rect = default;
        return IsHandleCreated && !IsDisposed && GetWindowRect(Handle, out rect);
    }

    private static (int width, int height) GetPhysicalScreenSize()
    {
        return (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
    }

    /// <summary>
    /// 切换锁定状态。锁定时禁用拖拽并固定位置；解锁时允许拖拽。
    /// </summary>
    public void SetPositionLocked(bool locked)
    {
        isPositionLocked = locked;
        Properties.Settings.Default.IsPositionLocked = locked;
        Properties.Settings.Default.Save();
        Cursor = locked ? Cursors.Default : Cursors.SizeAll;
    }

    /// <summary>
    /// 从设置重新应用锁定状态。设置窗口修改后调用。
    /// </summary>
    public void ApplySettings()
    {
        SetPositionLocked(Properties.Settings.Default.IsPositionLocked);
    }

    private void WireMouseEvents()
    {
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseDoubleClick += OnMouseDoubleClick;
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !isPositionLocked)
        {
            isDragging = true;
            dragStartCursor = Cursor.Position;
            dragStartLocation = Location;
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!isDragging)
        {
            return;
        }

        var delta = new Point(Cursor.Position.X - dragStartCursor.X, Cursor.Position.Y - dragStartCursor.Y);
        var next = new Point(dragStartLocation.X + delta.X, dragStartLocation.Y + delta.Y);

        // Physical screen clamp — 0px margin, use Win32 metrics directly.
        var sw = GetPhysicalScreenSize();
        next.X = Math.Clamp(next.X, 0, Math.Max(0, sw.width - Width));
        next.Y = Math.Clamp(next.Y, 0, Math.Max(0, sw.height - Height));

        Location = next;
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!isDragging)
        {
            return;
        }

        isDragging = false;

        // Use Win32 physical coordinates to avoid DPI mismatch between
        // Form.Location and Screen.Bounds.
        if (!GetPhysicalBounds(out var physRect))
        {
            return;
        }

        var sw = GetPhysicalScreenSize();
        var clampedX = Math.Clamp(physRect.left, 0, Math.Max(0, sw.width - Width));
        var clampedY = Math.Clamp(physRect.top, 0, Math.Max(0, sw.height - Height));

        // Save in physical coords so startup repositioning is consistent.
        Properties.Settings.Default.FloatingSignalX = clampedX;
        Properties.Settings.Default.FloatingSignalY = clampedY;
        Properties.Settings.Default.Save();

        // Force the window to the clamped physical position.
        SetWindowPos(Handle, HWND_TOPMOST, clampedX, clampedY, 0, 0,
            SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private void OnMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            showSettings();
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip
        {
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point)
        };

        var lockItem = new ToolStripMenuItem("锁定位置")
        {
            Checked = isPositionLocked
        };
        lockItem.Click += (_, _) =>
        {
            SetPositionLocked(!isPositionLocked);
            lockItem.Checked = isPositionLocked;
        };
        menu.Items.Add(lockItem);

        var settingsItem = new ToolStripMenuItem("打开设置");
        settingsItem.Click += (_, _) => showSettings();
        menu.Items.Add(settingsItem);

        var hideItem = new ToolStripMenuItem("隐藏悬浮灯");
        hideItem.Click += (_, _) => hideFloatingSignal();
        menu.Items.Add(hideItem);

        return menu;
    }

    private Point InitialLocation()
    {
        var savedX = Properties.Settings.Default.FloatingSignalX;
        var savedY = Properties.Settings.Default.FloatingSignalY;

        // Default: right-center of physical screen.
        if (savedX < 0 || savedY < 0)
        {
            var sw = GetPhysicalScreenSize();
            savedX = Math.Max(0, sw.width - Width);
            savedY = Math.Max(0, (sw.height - Height) / 2);
        }

        return new Point(savedX, savedY);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.FromArgb(240, 240, 240));
        DrawIndicator(graphics);
    }

    private void DrawIndicator(Graphics graphics)
    {
        var color = StatusColor(signal.DisplayState());
        var intensity = Intensity();
        var bounds = ClientRectangle;

        using var lit = new SolidBrush(Color.FromArgb((int)(255 * intensity), color));
        graphics.FillEllipse(lit, bounds);
    }

    private void SetupToolTip()
    {
        toolTip.SetToolTip(this, StatusText(signal));
    }

    private void UpdateToolTip()
    {
        toolTip.SetToolTip(this, StatusText(signal));
    }

    private static string StatusText(AgentSignal signal)
    {
        return signal.DisplayState() switch
        {
            DisplayState.Ready => "就绪",
            DisplayState.Completed => "完成",
            DisplayState.Active => "工作中",
            DisplayState.NeedsReview => "需审查",
            DisplayState.Stale => "陈旧",
            DisplayState.Permission => "待授权",
            DisplayState.Blocked => "阻塞",
            DisplayState.Paused => "暂停",
            _ => signal.ToString()
        };
    }

    private float Intensity()
    {
        var phase = tick % 4 < 2 ? 1f : 0.35f;
        return signal.DisplayState() switch
        {
            DisplayState.Ready or DisplayState.Completed => 0.35f,
            DisplayState.Active => phase,
            DisplayState.NeedsReview => phase,
            DisplayState.Stale => 0.75f,
            DisplayState.Permission => phase,
            DisplayState.Blocked => tick % 2 == 0 ? 1f : 0.2f,
            DisplayState.Paused => 0.25f,
            _ => 0.35f
        };
    }

    private static Color StatusColor(DisplayState displayState)
    {
        return displayState switch
        {
            DisplayState.Ready or DisplayState.Completed => Color.FromArgb(59, 130, 246),   // 鲜蓝
            DisplayState.Active => Color.FromArgb(34, 197, 94),                            // 鲜绿
            DisplayState.NeedsReview or DisplayState.Stale => Color.FromArgb(250, 204, 21), // 鲜黄
            DisplayState.Permission or DisplayState.Blocked => Color.FromArgb(239, 68, 68), // 鲜红
            DisplayState.Paused => Color.FromArgb(148, 163, 184),                          // 灰蓝
            _ => Color.FromArgb(59, 130, 246)
        };
    }
}
