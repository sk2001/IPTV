using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IptvPlayer;

internal static class WindowEffects
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Barre de titre sombre (Windows 10 20H1+ / 11) assortie au thème.</summary>
    public static void DarkTitleBar(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).EnsureHandle();
            int on = 1;
            DwmSetWindowAttribute(h, 20, ref on, sizeof(int));            // USE_IMMERSIVE_DARK_MODE
            int caption = 0x17110E;                                         // #0E1117 (BGR)
            DwmSetWindowAttribute(h, 35, ref caption, sizeof(int));        // CAPTION_COLOR (Win11)
        }
        catch { }
    }
}
