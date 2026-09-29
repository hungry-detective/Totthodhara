using System;
using System.Runtime.InteropServices;

namespace ClipDropPro.Services
{
    /// <summary>
    /// Live OS-theme helper. Every method reads the current mode FRESH from the
    /// registry on each call — never caches — so flips while the app is open are
    /// picked up immediately by existing timers / event handlers.
    /// Key: HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize
    ///      AppsUseLightTheme (preferred) → fallback SystemUsesLightTheme.
    /// </summary>
    public static class OsThemeHelper
    {
        private const string PersonalizeKey =
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public static bool IsAppsLightTheme()
        {
            try
            {
                // Preferred per spec: AppsUseLightTheme controls app mode.
                var apps = Microsoft.Win32.Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", null);
                if (apps is int ai) return ai == 1;
                // Fallback: some builds only populate SystemUsesLightTheme.
                var sys = Microsoft.Win32.Registry.GetValue(PersonalizeKey, "SystemUsesLightTheme", 0);
                return sys is int si && si == 1;
            }
            catch
            {
                return false; // dark default — matches App.xaml baked dark defaults
            }
        }

        // --- Native caption + control theming (Win32) -----------------------
        // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (Win10 20H1+), legacy 19 (older 10).
        // Apply BOTH so titlebars follow on every supported build.
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttributeNative(
            IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowThemeNative(
            IntPtr hwnd, string pszSubAppName, string pszSubIdList);

        /// <summary>
        /// Apply immersive titlebar (attrs 20 + legacy 19) and Explorer control
        /// theme per top-level window. Dark → "DarkMode_Explorer" so scrollbars,
        /// buttons and toasts go dark; Light → "Explorer".
        ///
        /// NOTE: do NOT put list views on "DarkMode_ItemsView" — it leaves their
        /// scrollbars light. Keep list views on Explorer and only put *headers*
        /// on ItemsView (see ApplyListViewTheme). Custom-drawn row colors are
        /// owned by the app and left untouched.
        /// </summary>
        public static void ApplyWindowTheme(IntPtr hwnd, bool isLightTheme)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int dark = isLightTheme ? 0 : 1;
                // New attr first, then legacy — ignore failures (older OS).
                DwmSetWindowAttributeNative(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                DwmSetWindowAttributeNative(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, ref dark, sizeof(int));
            }
            catch { }
            try
            {
                SetWindowThemeNative(hwnd,
                    isLightTheme ? "Explorer" : "DarkMode_Explorer", null);
            }
            catch { }
        }

        /// <summary>
        /// List-view rule: body stays on Explorer (correct dark scrollbars),
        /// header-only may use DarkMode_ItemsView. Row colors are custom-drawn
        /// by the app and are never altered here.
        /// </summary>
        public static void ApplyListViewTheme(IntPtr listHwnd, IntPtr headerHwnd, bool isLightTheme)
        {
            try
            {
                if (listHwnd != IntPtr.Zero)
                    SetWindowThemeNative(listHwnd,
                        isLightTheme ? "Explorer" : "Explorer", null);
                if (headerHwnd != IntPtr.Zero && !isLightTheme)
                    SetWindowThemeNative(headerHwnd, "DarkMode_ItemsView", null);
            }
            catch { }
        }

        public static void ApplyWindowTheme(System.Windows.Window win, bool isLightTheme)
        {
            if (win == null) return;
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
                if (hwnd != IntPtr.Zero)
                    ApplyWindowTheme(hwnd, isLightTheme);
            }
            catch { }
        }

        // --- Taskbar tone following (System mode) ---------------------------
        // The Win11 taskbar is blurred wallpaper + tint, so over a blue
        // wallpaper it reads blue-grey — a flat fill can never match it.
        // These helpers let the shelf sample the wallpaper's bottom strip
        // (what sits behind the taskbar/shelf) and tint toward it, the same
        // way DWM does. Fresh reads every call; callers cache by key.

        /// <summary>
        /// Cache key for the current wallpaper: file path + timestamp, or the
        /// solid background color when no wallpaper file is set.
        /// </summary>
        public static string GetWallpaperKey()
        {
            try
            {
                var path = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Control Panel\Desktop", "Wallpaper", "") as string;
                if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                    return path + "|" + System.IO.File.GetLastWriteTimeUtc(path).Ticks;
                var bg = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Control Panel\Colors", "Background", "") as string;
                return "color:" + (bg ?? "");
            }
            catch { return ""; }
        }

        /// <summary>
        /// Average color of the wallpaper's bottom strip (behind taskbar/shelf),
        /// downscaled to 1x1 (== a cheap blur). Falls back to the solid
        /// background color, then to a neutral slate.
        /// </summary>
        public static System.Windows.Media.Color GetWallpaperBottomAverage()
        {
            try
            {
                var path = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Control Panel\Desktop", "Wallpaper", "") as string;
                if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                {
                    using var fs = System.IO.File.OpenRead(path);
                    using var bmp = new System.Drawing.Bitmap(fs);
                    if (bmp.Width > 0 && bmp.Height > 0)
                    {
                        // Bottom-seventh center band — what the taskbar/shelf sit on.
                        int sx = bmp.Width / 5, sw = Math.Max(1, bmp.Width * 3 / 5);
                        int sh = Math.Max(1, bmp.Height / 7);
                        int sy = Math.Max(0, bmp.Height - sh);
                        using var avg = new System.Drawing.Bitmap(1, 1);
                        using (var g = System.Drawing.Graphics.FromImage(avg))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            g.DrawImage(bmp, new System.Drawing.Rectangle(0, 0, 1, 1),
                                new System.Drawing.Rectangle(sx, sy, sw, sh),
                                System.Drawing.GraphicsUnit.Pixel);
                        }
                        var px = avg.GetPixel(0, 0);
                        return System.Windows.Media.Color.FromRgb(px.R, px.G, px.B);
                    }
                }
            }
            catch { }
            try
            {
                // Solid-color wallpaper: "R G B".
                var bg = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Control Panel\Colors", "Background", "") as string;
                var parts = (bg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3
                    && byte.TryParse(parts[0], out byte r)
                    && byte.TryParse(parts[1], out byte g)
                    && byte.TryParse(parts[2], out byte b))
                    return System.Windows.Media.Color.FromRgb(r, g, b);
            }
            catch { }
            return System.Windows.Media.Color.FromRgb(0x2E, 0x3A, 0x55);
        }

        /// <summary>
        /// Effective light-ness of the app's CURRENT resources (for dialogs that
        /// don't know the Theme setting, e.g. ThemedMessageBox). Light theme uses
        /// dark text (#222222); dark/transparent themes use white text.
        /// </summary>
        public static bool CurrentResourcesAreLight()
        {
            try
            {
                if (System.Windows.Application.Current?.Resources["TextColor"]
                    is System.Windows.Media.SolidColorBrush tb)
                {
                    // Light theme → TextColor #222222 (dark). Dark → White.
                    int sum = tb.Color.R + tb.Color.G + tb.Color.B;
                    return sum < 384;
                }
            }
            catch { }
            return !IsAppsLightTheme() ? false : true;
        }
    }
}
