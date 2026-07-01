using AgentSignalBar.Core;

namespace AgentSignalBar.Windows;

internal static class TrayIconRenderer
{
    public static Icon Render(AgentSignal signal, int tick)
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var colors = Intensities(signal, tick);
        DrawLamp(graphics, new Rectangle(8, 20, 14, 24), Color.FromArgb(239, 68, 68), colors.Red);
        DrawLamp(graphics, new Rectangle(25, 20, 14, 24), Color.FromArgb(250, 204, 21), colors.Yellow);
        DrawLamp(graphics, new Rectangle(42, 20, 14, 24), Color.FromArgb(34, 197, 94), colors.Green);

        var handle = bitmap.GetHicon();
        try
        {
            return Icon.FromHandle(handle).Clone() as Icon ?? SystemIcons.Application;
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private static void DrawLamp(Graphics graphics, Rectangle bounds, Color color, float intensity)
    {
        using var dimBrush = new SolidBrush(Color.FromArgb(70, 95, 95, 95));
        graphics.FillEllipse(dimBrush, bounds);
        if (intensity <= 0)
        {
            return;
        }

        using var glowBrush = new SolidBrush(Color.FromArgb((int)(110 * intensity), color));
        var glow = Rectangle.Inflate(bounds, 5, 5);
        graphics.FillEllipse(glowBrush, glow);
        using var brush = new SolidBrush(Color.FromArgb((int)(255 * intensity), color));
        graphics.FillEllipse(brush, bounds);
    }

    private static (float Red, float Yellow, float Green) Intensities(AgentSignal signal, int tick)
    {
        var phase = tick % 4 < 2 ? 1f : 0.25f;
        return signal.DisplayState() switch
        {
            DisplayState.Ready or DisplayState.Completed => (0.12f, 0.12f, 0.12f),   // 暗淡：空闲/完成
            DisplayState.Active => (0f, 0f, phase),                                  // 绿色呼吸：工作中
            DisplayState.NeedsReview => (0f, phase, 0f),                             // 黄色呼吸：需关注
            DisplayState.Stale => (0f, 0.65f, 0f),                                   // 黄色常亮：过期
            DisplayState.Permission => (phase, 0f, 0f),                              // 红色呼吸：权限
            DisplayState.Blocked => (tick % 2 == 0 ? 1f : 0.1f, 0f, 0f),             // 红色闪烁：阻塞
            DisplayState.Paused => (0.25f, 0.25f, 0.25f),                            // 灰色：暂停
            _ => (0.12f, 0.12f, 0.12f)
        };
    }
}
