namespace AgentSignalBar.Windows;

internal enum WindowsBackdropMode
{
    Solid,
    Mica,
    Acrylic
}

internal static class WindowsBackdrop
{
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int BackdropNone = 1;
    private const int BackdropMica = 2;
    private const int BackdropAcrylic = 3;

    public static bool Apply(Form form, WindowsBackdropMode mode, bool darkMode)
    {
        var dark = darkMode ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        var value = mode switch
        {
            WindowsBackdropMode.Mica => BackdropMica,
            WindowsBackdropMode.Acrylic => BackdropAcrylic,
            _ => BackdropNone
        };

        var result = NativeMethods.DwmSetWindowAttribute(form.Handle, DwmwaSystemBackdropType, ref value, sizeof(int));
        return result == 0;
    }
}
