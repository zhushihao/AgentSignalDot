namespace AgentSignalBar.Core;

public readonly record struct SignalRectangle(int X, int Y, int Width, int Height);

public enum FloatingSignalPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
    LeftCenter,
    RightCenter
}

public static class FloatingSignalWindowPlacement
{
    public const int DefaultWidth = 50;
    public const int DefaultHeight = 28;
    public const int DefaultMargin = 0;

    public static SignalRectangle DefaultPlacement(SignalRectangle workArea)
    {
        return PlacementFor(workArea, FloatingSignalPosition.TopRight, DefaultWidth, DefaultHeight);
    }

    public static SignalRectangle PlacementFor(
        SignalRectangle workArea,
        FloatingSignalPosition position,
        int width,
        int height)
    {
        var x = position switch
        {
            FloatingSignalPosition.TopLeft or FloatingSignalPosition.BottomLeft or FloatingSignalPosition.LeftCenter
                => workArea.X + DefaultMargin,
            FloatingSignalPosition.TopCenter or FloatingSignalPosition.BottomCenter
                => workArea.X + (workArea.Width - width) / 2,
            FloatingSignalPosition.TopRight or FloatingSignalPosition.BottomRight or FloatingSignalPosition.RightCenter
                => workArea.X + workArea.Width - width - DefaultMargin,
            _ => workArea.X + workArea.Width - width - DefaultMargin
        };

        var y = position switch
        {
            FloatingSignalPosition.TopLeft or FloatingSignalPosition.TopCenter or FloatingSignalPosition.TopRight
                => workArea.Y + DefaultMargin,
            FloatingSignalPosition.LeftCenter or FloatingSignalPosition.RightCenter
                => workArea.Y + (workArea.Height - height) / 2,
            FloatingSignalPosition.BottomLeft or FloatingSignalPosition.BottomCenter or FloatingSignalPosition.BottomRight
                => workArea.Y + workArea.Height - height - DefaultMargin,
            _ => workArea.Y + DefaultMargin
        };

        return Clamp(new SignalRectangle(x, y, width, height), workArea);
    }

    public static SignalRectangle Clamp(SignalRectangle placement, SignalRectangle workArea)
    {
        var width = placement.Width <= 0 ? DefaultWidth : placement.Width;
        var height = placement.Height <= 0 ? DefaultHeight : placement.Height;
        var minX = workArea.X;
        var minY = workArea.Y;
        var maxX = workArea.X + Math.Max(0, workArea.Width - width);
        var maxY = workArea.Y + Math.Max(0, workArea.Height - height);

        return new SignalRectangle(
            Math.Clamp(placement.X, minX, maxX),
            Math.Clamp(placement.Y, minY, maxY),
            width,
            height);
    }
}
