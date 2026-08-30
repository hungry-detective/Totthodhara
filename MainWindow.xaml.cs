using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Interop;
using ClipDropPro.Models;
using ClipDropPro.ViewModels;
using ClipDropPro.Plugins;
using Wpf.Ui.Appearance;
using System.Windows.Media;

namespace ClipDropPro
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private const int DesiredHeight = 36;

        private void SetItemSizes(double circleSize, double margin, double padH, double padV, double fontSize, double menuH = 36)
        {
            double cardHeight = circleSize + (padV * 2) + 6;
            Resources["ItemCircleSize"] = circleSize;
            Resources["ItemCircleCorner"] = new System.Windows.CornerRadius(cardHeight / 2);
            Resources["ItemMargin"] = new System.Windows.Thickness(margin, 0, margin, 0);
            Resources["ItemPadding"] = new System.Windows.Thickness(padH, padV, padH, padV);
            Resources["ItemFontSize"] = fontSize;
            Resources["ItemSmallFontSize"] = Math.Max(8, fontSize - 2);
            Resources["ItemIndexFontSize"] = Math.Max(9, fontSize);
            Resources["ItemMenuHeight"] = menuH;
            Resources["ItemCardHeight"] = cardHeight;

            // Item icon border + inner icon sizes
            double itemIconSize = cardHeight - 4;
            Resources["ItemIconSize"] = itemIconSize;
            Resources["ItemSymbolFontSize"] = fontSize + 2;
            Resources["ItemImageIconSize"] = fontSize + 4;

            // Image content template sizes
            Resources["ImageThumbWidth"] = fontSize + 10;
            Resources["ImageThumbHeight"] = fontSize + 6;
            Resources["ImageResFontSize"] = Math.Max(9, fontSize - 2);

            // Toolbar button matches card height, icon scales with font
            double toolBarBtnSize = cardHeight;
            double toolBarIconSize = fontSize + 6;
            Resources["ToolBarBtnSize"] = toolBarBtnSize;
            Resources["ToolBarIconSize"] = toolBarIconSize;

            // System monitor sizes scale with bar size
            double sysIconSize = Math.Max(10, fontSize + 4);
            double sysTextSize = Math.Max(9, fontSize);
            Resources["SysMonitorIconSize"] = sysIconSize;
            Resources["SysMonitorTextSize"] = sysTextSize;
            Resources["SysMonitorCanvasSize"] = sysIconSize + 2;
        }
        private static System.Drawing.Icon? _keepTrayIconAlive;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;

            // Set window icon from app.png
            try
            {
                this.Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/app.png"));
            }
            catch { }

            // No WPF-UI per-window Apply — the global base is always Light,
            // and UpdateTheme overrides all color resources for both modes.



            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            if (_viewModel.SettingsViewModel != null)
            {
                _viewModel.SettingsViewModel.PropertyChanged += ViewModel_SettingsPropertyChanged;
            }
            
            // Initial theme application
            UpdateTheme();
        }

        private void SetupTrayIcon()
        {
            try
            {
                if (TrayIcon == null) return;

                System.Drawing.Icon? created = CreateTrayIconFromPng();
                if (created == null)
                    created = CreateTrayIconFromIco();

                if (created == null)
                {
                    Log("Tray icon setup failed: no valid icon resource.");
                    return;
                }

                _keepTrayIconAlive?.Dispose();
                _keepTrayIconAlive = created;
                TrayIcon.Icon = _keepTrayIconAlive;
                TrayIcon.Visibility = Visibility.Visible;
                TrayIcon.ForceCreate();
                Log("Tray icon initialized successfully.");
            }
            catch (Exception ex)
            {
                Log($"Tray icon setup error: {ex.Message}");
            }
        }

        private static System.Drawing.Icon? CreateTrayIconFromPng()
        {
            try
            {
                var pngStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.png"));
                if (pngStream == null) return null;

                using (pngStream.Stream)
                using (var sourceBmp = new System.Drawing.Bitmap(pngStream.Stream))
                {
                    var window = System.Windows.Application.Current?.MainWindow as MainWindow;
                    using var cropped = window != null
                        ? window.CropImageTransparency(sourceBmp)
                        : new System.Drawing.Bitmap(sourceBmp);
                    using var resized = new System.Drawing.Bitmap(32, 32);
                    using (var g = System.Drawing.Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        g.Clear(System.Drawing.Color.Transparent);
                        g.DrawImage(cropped, 0, 0, 32, 32);
                    }
                    IntPtr hIcon = resized.GetHicon();
                    try
                    {
                        using var tmp = System.Drawing.Icon.FromHandle(hIcon);
                        return (System.Drawing.Icon)tmp.Clone();
                    }
                    finally
                    {
                        DestroyIcon(hIcon);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static System.Drawing.Icon? CreateTrayIconFromIco()
        {
            try
            {
                var icoStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
                if (icoStream != null)
                {
                    using var ms = new MemoryStream();
                    icoStream.Stream.CopyTo(ms);
                    ms.Position = 0;
                    using var tmp = new System.Drawing.Icon(ms);
                    return (System.Drawing.Icon)tmp.Clone();
                }

                string localIcoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(localIcoPath))
                {
                    using var tmp = new System.Drawing.Icon(localIcoPath);
                    return (System.Drawing.Icon)tmp.Clone();
                }
            }
            catch { }
            return null;
        }

        private System.Drawing.Bitmap CropImageTransparency(System.Drawing.Bitmap source)
        {
            try {
                int minX = source.Width, minY = source.Height, maxX = -1, maxY = -1;
                for (int y = 0; y < source.Height; y++) {
                    for (int x = 0; x < source.Width; x++) {
                        if (source.GetPixel(x, y).A > 0) {
                            if (x < minX) minX = x;
                            if (y < minY) minY = y;
                            if (x > maxX) maxX = x;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
                if (maxX == -1) return new System.Drawing.Bitmap(source); // Empty
                int width = maxX - minX + 1;
                int height = maxY - minY + 1;
                var cropped = source.Clone(new System.Drawing.Rectangle(minX, minY, width, height), source.PixelFormat);

                // Pad to square so resize doesn't stretch (preserve aspect ratio)
                int size = Math.Max(width, height);
                var square = new System.Drawing.Bitmap(size, size);
                using (var g = System.Drawing.Graphics.FromImage(square))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    int xOff = (size - width) / 2;
                    int yOff = (size - height) / 2;
                    g.DrawImage(cropped, xOff, yOff, width, height);
                }
                return square;
            } catch { return new System.Drawing.Bitmap(source); }
        }

        private void ViewModel_SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(SettingsViewModel.Theme):
                    UpdateTheme();
                    break;
                case nameof(SettingsViewModel.AlwaysOnTop):
                    Topmost = _viewModel.SettingsViewModel.AlwaysOnTop;
                    break;
                case nameof(SettingsViewModel.HideClipboard):
                    _viewModel.HideClipboard = _viewModel.SettingsViewModel.HideClipboard;
                    if (_viewModel.HideClipboard)
                    {
                        CloseSearch();
                        if (_viewModel.IsMultiPasteMode)
                            _viewModel.IsMultiPasteMode = false;
                    }
                    UpdateLayoutForHideClipboard();
                    SetAppBarPos();
                    break;
            }
        }

        #region AppBar Logic
        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uCallbackMessage;
            public int uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        enum ABMsg : int
        {
            ABM_NEW = 0,
            ABM_REMOVE = 1,
            ABM_QUERYPOS = 2,
            ABM_SETPOS = 3,
            ABM_GETSTATE = 4,
            ABM_GETTASKBARPOS = 5,
            ABM_ACTIVATE = 6,
            ABM_GETAUTOHIDEBAR = 7,
            ABM_SETAUTOHIDEBAR = 8,
            ABM_WINDOWPOSCHANGED = 9,
            ABM_SETSTATE = 10
        }

        enum ABEdge : int
        {
            ABE_LEFT = 0,
            ABE_TOP = 1,
            ABE_RIGHT = 2,
            ABE_BOTTOM = 3
        }

        enum ABNotify : int
        {
            ABN_STATECHANGE = 0,
            ABN_POSCHANGED = 1,
            ABN_FULLSCREENAPP = 2,
            ABN_WINDOWARRANGE = 3
        }

        [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageA", CharSet = CharSet.Ansi)]
        static extern int RegisterWindowMessage(string lpString);

        [DllImport("dwmapi.dll", PreserveSig = false)]
        public static extern void DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        static extern IntPtr WindowFromPoint(System.Drawing.Point pt);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        const uint SPI_GETWORKAREA = 0x0030;
        [DllImport("user32.dll")]
        static extern bool SystemParametersInfo(uint uAction, uint uParam, ref RECT lpvParam, uint fwWinIni);

        private int uCallbackMessage;
        private bool fIsAppBarRegistered = false;

        private void RegisterAppBar()
        {
            if (fIsAppBarRegistered) return;

            var wndHelper = new WindowInteropHelper(this);

            int DWMWA_EXCLUDED_FROM_PEEK = 12;
            int disablePeek = 1;
            DwmSetWindowAttribute(wndHelper.Handle, DWMWA_EXCLUDED_FROM_PEEK, ref disablePeek, sizeof(int));

            int exStyle = GetWindowLong(wndHelper.Handle, GWL_EXSTYLE);
            SetWindowLong(wndHelper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = wndHelper.Handle;
            abd.uCallbackMessage = uCallbackMessage;

            SHAppBarMessage((int)ABMsg.ABM_NEW, ref abd);
            fIsAppBarRegistered = true;

            SetAppBarPos();
        }

        private void UnregisterAppBar()
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = new WindowInteropHelper(this).Handle;

            SHAppBarMessage((int)ABMsg.ABM_REMOVE, ref abd);
            fIsAppBarRegistered = false;
        }

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_SHOWWINDOW = 0x0040;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int SW_SHOWNOACTIVATE = 4;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", EntryPoint = "ShowWindow")]
        static extern bool NativeShowWindow(IntPtr hWnd, int nCmdShow);

        private void DisableNoActivate()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE, exStyle & ~WS_EX_NOACTIVATE);
        }

        private void EnableNoActivate()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }
        
        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject([In] IntPtr hObject);

        #region Acrylic Blur
        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        internal enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        internal enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_ENABLE_HOSTBACKDROP = 5
        }

        internal struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        internal struct AccentPolicy
        {
            public AccentState AccentState;
            public uint AccentFlags;
            public uint GradientColor;
            public uint AnimationId;
        }

        private void EnableAcrylic()
        {
            // Acrylic disabled entirely — solid colors in both modes
        }
        #endregion

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private System.Windows.Threading.DispatcherTimer _fullScreenCheckTimer;
        private bool _isForcedHiddenByFullScreen = false;
        private IntPtr _lastFullScreenWindow = IntPtr.Zero;
        private uint _lastFullScreenProcessId = 0;
        private DateTime _lastFullScreenTime = DateTime.MinValue;

        private static void Log(string message) => Services.Logger.Write($"[Wnd] {message}");

        private double _lastScreenHeight = 0;

        private void SetAppBarPos()
        {
            Log("SetAppBarPos started");

            double baseHeight;
            double capsuleRadius;
            double capsuleMarginV;
            switch (_viewModel.BarSize)
            {
                case "Small": baseHeight = 32; capsuleRadius = 16; capsuleMarginV = 0; SetItemSizes(16, 2, 4, 2, 12, 28); break;
                case "Large": baseHeight = 50; capsuleRadius = 25; capsuleMarginV = 0; SetItemSizes(28, 5, 7, 2, 15, 38); break;
                case "Medium":
                default: baseHeight = 34; capsuleRadius = 17; capsuleMarginV = 0; SetItemSizes(18, 2, 4, 2, 13, 32); break;
            }

            Height = baseHeight;
            if (CapsuleBorder != null)
            {
                CapsuleBorder.CornerRadius = new System.Windows.CornerRadius(capsuleRadius);
                CapsuleBorder.Margin = new System.Windows.Thickness(0, capsuleMarginV, 0, capsuleMarginV);
            }

            var presentationSource = PresentationSource.FromVisual(this);
            double dpiFactor = presentationSource?.CompositionTarget.TransformToDevice.M22 ?? 1.0;
            int heightPx = (int)Math.Round(baseHeight * dpiFactor);

            var hwnd = new WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            MONITORINFO monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(monitorInfo);
            int screenWidthPx, screenHeightPx;
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                screenWidthPx = (int)(SystemParameters.PrimaryScreenWidth * dpiFactor);
                screenHeightPx = (int)(SystemParameters.PrimaryScreenHeight * dpiFactor);
            }
            else
            {
                screenWidthPx = monitorInfo.rcMonitor.right - monitorInfo.rcMonitor.left;
                screenHeightPx = monitorInfo.rcMonitor.bottom - monitorInfo.rcMonitor.top;
            }

            bool isTop = _viewModel.ShelfPosition == "Top";

            // Query the actual taskbar position so we stack above/below it correctly
            int taskbarEdgePx = -1;
            if (!isTop)
            {
                // Find taskbar window
                IntPtr taskbarHwnd = FindWindow("Shell_TrayWnd", null);
                if (taskbarHwnd != IntPtr.Zero)
                {
                    RECT taskbarRect;
                    if (GetWindowRect(taskbarHwnd, out taskbarRect))
                    {
                        taskbarEdgePx = taskbarRect.top;
                        Log($"Taskbar rect: T={taskbarRect.top}, B={taskbarRect.bottom}, height={taskbarRect.bottom - taskbarRect.top}");
                    }
                }
            }

            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = hwnd;
            abd.uEdge = isTop ? (int)ABEdge.ABE_TOP : (int)ABEdge.ABE_BOTTOM;
            abd.rc.left = 0;
            abd.rc.right = screenWidthPx;
            if (isTop)
            {
                abd.rc.top = 0;
                abd.rc.bottom = heightPx;
            }
            else
            {
                // Stack above the taskbar instead of at the screen edge
                if (taskbarEdgePx > 0)
                {
                    abd.rc.bottom = taskbarEdgePx;
                    abd.rc.top = taskbarEdgePx - heightPx;
                }
                else
                {
                    abd.rc.bottom = screenHeightPx;
                    abd.rc.top = screenHeightPx - heightPx;
                }
            }

            SHAppBarMessage((int)ABMsg.ABM_QUERYPOS, ref abd);

            // ABM_QUERYPOS adjusts our rect to avoid the taskbar and other AppBars.
            // Only enforce the HEIGHT — let Windows handle the Y position.
            int queriedHeight = abd.rc.bottom - abd.rc.top;
            if (queriedHeight < heightPx)
            {
                // ABM_QUERYPOS shrunk us — enforce our minimum height from the edge it gave us
                if (isTop)
                    abd.rc.bottom = abd.rc.top + heightPx;
                else
                    abd.rc.top = abd.rc.bottom - heightPx;
            }

            SHAppBarMessage((int)ABMsg.ABM_SETPOS, ref abd);
            Log($"AppBar Rect after SETPOS: L={abd.rc.left}, T={abd.rc.top}, R={abd.rc.right}, B={abd.rc.bottom}");

            // Log the actual work area Windows reserved
            RECT workArea = new RECT();
            SystemParametersInfo(SPI_GETWORKAREA, 0, ref workArea, 0);
            Log($"Work area after SETPOS: L={workArea.left}, T={workArea.top}, R={workArea.right}, B={workArea.bottom}, height={workArea.bottom - workArea.top}");

            int xPx = abd.rc.left;
            int wPx = abd.rc.right - abd.rc.left;
            int hPx = abd.rc.bottom - abd.rc.top;
            int yPx = abd.rc.top;

            uint flags = SWP_NOACTIVATE | (_isForcedHiddenByFullScreen ? 0u : SWP_SHOWWINDOW);
            SetWindowPos(hwnd, HWND_TOPMOST, xPx, yPx, wPx, hPx, flags);

            // Verify actual window rect after SetWindowPos
            RECT actualRect;
            GetWindowRect(hwnd, out actualRect);
            Log($"SetWindowPos requested: x={xPx}, y={yPx}, w={wPx}, h={hPx} | actual: L={actualRect.left}, T={actualRect.top}, R={actualRect.right}, B={actualRect.bottom}");

            Width = wPx / dpiFactor;
            Height = hPx / dpiFactor;
            Log($"Window placed: x={xPx}, y={yPx}, w={wPx}, h={hPx}");
        }
        #endregion

        private HwndSource _hwndSource;
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;

            // Add NOACTIVATE style so the shelf doesn't steal focus from target apps
            int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);

            // Apply acrylic glass effect
            EnableAcrylic();

            _hwndSource = HwndSource.FromHwnd(handle);
            _hwndSource.AddHook(HwndHandler);
            AddClipboardFormatListener(handle);

            // Re-register clipboard listener after wake from sleep/standby
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;

            // Also re-register on window activation — catches any case where the listener was lost
            this.Activated += OnWindowActivated;

            uCallbackMessage = RegisterWindowMessage("AppBarMessage");
            RegisterAppBar();

            // Initialize full-screen detection timer
            _fullScreenCheckTimer = new System.Windows.Threading.DispatcherTimer();
            _fullScreenCheckTimer.Interval = TimeSpan.FromMilliseconds(100);
            _fullScreenCheckTimer.Tick += FullScreenCheckTimer_Tick;
            _fullScreenCheckTimer.Start();
        }

        private uint _myProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        private System.Windows.Controls.ContextMenu _openContextMenu;
        private IntPtr _openContextMenuFgHwnd;

        private DateTime _lastContextMenuOpenTime = DateTime.MinValue;

        private object _savedItemToolTip;
        private System.Windows.FrameworkElement _toolTipTarget;
        private System.Windows.Controls.Border _highlightedItemBorder;
        private System.Windows.Media.Brush _savedItemBg;
        private System.Windows.Controls.Border _searchBoxBorder;
        private System.Windows.Controls.TextBox _searchTextBox;

        public void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var menu = sender as System.Windows.Controls.ContextMenu;
            _openContextMenu = menu;
            _openContextMenuFgHwnd = GetForegroundWindow();
            _lastContextMenuOpenTime = DateTime.Now;

            // Always clear highlight when menu closes, regardless of which menu or why
            if (menu != null)
            {
                menu.Closed += (s, args) => ClearItemHighlight();
            }

            if (menu?.PlacementTarget is System.Windows.Controls.Border border)
            {
                // Clear previous highlight before highlighting a new item
                if (_highlightedItemBorder != null && _highlightedItemBorder != border)
                {
                    _highlightedItemBorder.ClearValue(System.Windows.Controls.Border.BackgroundProperty);
                }

                // Find the child Grid for ToolTip handling
                var grid = border.Child as System.Windows.Controls.Grid;
                if (grid != null)
                {
                    _savedItemToolTip = grid.ToolTip;
                    _toolTipTarget = grid;
                    grid.ToolTip = null;
                }

                // Highlight the item background to show which item is being right-clicked
                _savedItemBg = border.Background;
                _highlightedItemBorder = border;
                border.Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["AccentColor"];

                // Center menu horizontally on the item
                border.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    var itemWidth = border.ActualWidth;
                    var menuWidth = menu.ActualWidth;
                    if (menuWidth > 0 && itemWidth > 0)
                        menu.HorizontalOffset = (itemWidth - menuWidth) / 2;
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        public void ContextMenu_Closed(object sender, RoutedEventArgs e)
        {
            var menu = sender as System.Windows.Controls.ContextMenu;
            if (_openContextMenu == menu) _openContextMenu = null;

            // Only restore if this menu belongs to the currently tracked item
            // (prevents race when a second right-click opens before first closes)
            if (menu?.PlacementTarget == _highlightedItemBorder)
            {
                // Restore ToolTip
                if (_toolTipTarget != null && _savedItemToolTip != null)
                {
                    _toolTipTarget.ToolTip = _savedItemToolTip;
                }
                _savedItemToolTip = null;
                _toolTipTarget = null;

                // Remove right-click highlight
                if (_savedItemBg != null)
                    _highlightedItemBorder.Background = _savedItemBg;
                else
                    _highlightedItemBorder.ClearValue(System.Windows.Controls.Border.BackgroundProperty);
                _highlightedItemBorder = null;
                _savedItemBg = null;
            }
        }

        private void FullScreenCheckTimer_Tick(object sender, EventArgs e)
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero) return;

            System.Text.StringBuilder className;
            IntPtr myHandle = new WindowInteropHelper(this).Handle;
            
            if (_openContextMenu != null && _openContextMenu.IsOpen)
            {
                return;
            }

            if (foregroundWindow == myHandle) return;

            // Simple check for Shell windows (Taskbar, Desktop, etc.)
            className = new System.Text.StringBuilder(256);
            GetClassName(foregroundWindow, className, className.Capacity);
            string shellCls = className.ToString();
            if (shellCls == "Shell_TrayWnd" || shellCls == "WorkerW" || shellCls == "Progman")
            {
                if (_isForcedHiddenByFullScreen)
                {
                    _isForcedHiddenByFullScreen = false;
                    if (_viewModel.IsShelfVisible) ShowWindow();
                }
                return;
            }

            RECT foregroundRect;
            if (GetWindowRect(foregroundWindow, out foregroundRect))
            {
                IntPtr monitor = MonitorFromWindow(foregroundWindow, MONITOR_DEFAULTTONEAREST);
                MONITORINFO monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = Marshal.SizeOf(monitorInfo);

                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    int monW = monitorInfo.rcMonitor.right - monitorInfo.rcMonitor.left;
                    int monH = monitorInfo.rcMonitor.bottom - monitorInfo.rcMonitor.top;
                    int winW = foregroundRect.right - foregroundRect.left;
                    int winH = foregroundRect.bottom - foregroundRect.top;

                    int style = GetWindowLong(foregroundWindow, -16); // GWL_STYLE
                    bool hasCaption = (style & 0x00C00000) == 0x00C00000; // WS_CAPTION = WS_BORDER | WS_DLGFRAME

                    // True fullscreen apps (games, video players, F11 browser) do NOT have WS_CAPTION.
                    // Standard maximized desktop apps with titlebars/tabs must NOT hide the shelf.
                    bool isFullScreen = !hasCaption && (
                        (foregroundRect.left <= monitorInfo.rcMonitor.left + 1 &&
                         foregroundRect.top <= monitorInfo.rcMonitor.top + 1 &&
                         foregroundRect.right >= monitorInfo.rcMonitor.right - 1 &&
                         foregroundRect.bottom >= monitorInfo.rcMonitor.bottom - 1)
                        ||
                        (winW >= monW * 0.95 && winH >= monH * 0.95)
                    );
                    
                    // Also check for browser fullscreen (Chrome/Edge/Firefox fullscreen videos)
                    // These often have no caption but might have thick frame
                    if (!isFullScreen && !hasCaption)
                    {
                        // Check if window covers almost entire monitor (98% coverage)
                        isFullScreen = (winW >= monW * 0.98 && winH >= monH * 0.98);
                    }

                    if (isFullScreen)
                    {
                        if (!_isForcedHiddenByFullScreen)
                        {
                            _isForcedHiddenByFullScreen = true;
                            _lastFullScreenWindow = foregroundWindow;
                            GetWindowThreadProcessId(foregroundWindow, out _lastFullScreenProcessId);
                            Visibility = Visibility.Collapsed;
                            UnregisterAppBar();
                        }
                    }
                    else
                    {
                        if (_isForcedHiddenByFullScreen)
                        {
                            // Check by process ID so child/popup windows (VLC OSD, tooltips)
                            // don't trigger a false "different window" event
                            uint foregroundProcessId = 0;
                            GetWindowThreadProcessId(foregroundWindow, out foregroundProcessId);
                            bool sameProcess = (_lastFullScreenProcessId == foregroundProcessId);

                            if (sameProcess)
                            {
                                // Same app — check if it actually exited fullscreen
                                // (has both caption AND thick frame, or is <50% screen width)
                                style = GetWindowLong(foregroundWindow, -16);
                                hasCaption = (style & 0x00C00000) != 0;
                                bool hasThickFrame = (style & 0x00040000) != 0;
                                bool isWindowed = (hasCaption && hasThickFrame) || winW < monW * 0.5;
                                if (isWindowed)
                                {
                                    // Truly exited fullscreen → show shelf
                                    _isForcedHiddenByFullScreen = false;
                                    _lastFullScreenWindow = IntPtr.Zero;
                                    _lastFullScreenProcessId = 0;
                                    if (_viewModel.IsShelfVisible)
                                    {
                                        Visibility = Visibility.Visible;
                                        Topmost = false;
                                        Topmost = true;
                                        RegisterAppBar();
                                        SetAppBarPos();
                                    }
                                }
                                else
                                {
                                    // Still in fullscreen/fullscreen-like mode → keep hidden
                                    _lastFullScreenTime = DateTime.UtcNow;
                                }
                            }
                            // Different app → show after a brief safety delay
                            else if ((DateTime.UtcNow - _lastFullScreenTime).TotalSeconds > 2)
                            {
                                _isForcedHiddenByFullScreen = false;
                                _lastFullScreenWindow = IntPtr.Zero;
                                _lastFullScreenProcessId = 0;
                                if (_viewModel.IsShelfVisible)
                                {
                                    Visibility = Visibility.Visible;
                                    Topmost = false;
                                    Topmost = true;
                                    RegisterAppBar();
                                    SetAppBarPos();
                                }
                            }
                        }
                    }
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        private const int WM_CLIPBOARDUPDATE = 0x031D;

        private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                _viewModel.ProcessClipboardChange();
            }
            else if (msg == uCallbackMessage)
            {
                int notify = wparam.ToInt32();
                switch (notify)
                {
                    case (int)ABNotify.ABN_FULLSCREENAPP:
                        // Fullscreen detection is handled by the FullScreenCheckTimer (when enabled).
                        // Handling ABN_FULLSCREENAPP directly here caused the shelf to collapse on
                        // startup because AppBar registration itself triggers this notification.
                        handled = true;
                        break;

                    case (int)ABNotify.ABN_POSCHANGED:
                    case (int)ABMsg.ABM_WINDOWPOSCHANGED:
                        SetAppBarPos();
                        handled = true;
                        break;
                }
            }
            return IntPtr.Zero;
        }

        private void RootGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_openContextMenu != null && _openContextMenu.IsOpen)
            {
                _openContextMenu.IsOpen = false;
                _openContextMenu = null;
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Setup Tray Icon now that window HWND exists
            SetupTrayIcon();

            // Show the shelf immediately on launch
            _viewModel.IsShelfVisible = true;
            ShowWindow();

            // Apply always-on-top setting on startup
            Topmost = _viewModel.SettingsViewModel?.AlwaysOnTop ?? true;

            // Mouse hook to detect clicks outside the app (AppBars don't receive deactivation msgs)
            InstallMouseHook();

            // Force initial item layout after data is loaded
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                new System.Action(() => ItemsHost?.InvalidateMeasure()),
                System.Windows.Threading.DispatcherPriority.Background);

            // Ctrl+F for search (works when shelf is focused by click)
            this.PreviewKeyDown += (s, ke) =>
            {
                if (ke.Key == Key.F && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
                {
                    if (_searchBoxBorder?.Visibility == System.Windows.Visibility.Visible)
                    {
                        CloseSearch();
                    }
                    else
                    {
                        SearchToggleButton_Click(null, null);
                    }
                    ke.Handled = true;
                }
            };

            // Apply HideClipboard layout on startup if enabled
            UpdateLayoutForHideClipboard();

            // Load plugins
            LoadPlugins();
        }

        private async void LoadPlugins()
        {
            try
            {
                var pluginManager = App.GetService<PluginManager>();
                if (pluginManager == null) return;

                var settingsService = App.GetService<Services.ISettingsService>();
                if (settingsService == null) return;

                var settings = new PluginsSettings { ShowPlugins = settingsService.ShowPlugins };
                await pluginManager.LoadPluginsAsync(settings);

                // Add plugin views to the PluginContainer
                if (PluginContainer != null)
                {
                    PluginContainer.Children.Clear();
                    var views = pluginManager.GetAllViews();
                    foreach (var view in views)
                    {
                        PluginContainer.Children.Add(view);
                    }
                }
            }
            catch (Exception ex)
            {
                Services.Logger.Write($"[MainWindow] Failed to load plugins: {ex.Message}");
            }
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsShelfVisible))
            {
                if (_viewModel.IsShelfVisible)
                {
                    if (!_isForcedHiddenByFullScreen)
                        ShowWindow();
                }
                else
                {
                    _isForcedHiddenByFullScreen = false;
                    HideWindow();
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.ShelfPosition) || e.PropertyName == nameof(MainViewModel.BarSize))
            {
                SetAppBarPos();
            }
            else if (e.PropertyName == nameof(MainViewModel.SettingsViewModel))
            {
                if (_viewModel.SettingsViewModel != null)
                {
                    _viewModel.SettingsViewModel.PropertyChanged += ViewModel_SettingsPropertyChanged;
                }
                UpdateTheme();
            }
            else if (e.PropertyName == nameof(MainViewModel.DebugStatus))
            {
                UpdateSearchButtonVisibilityForStatus();
            }
        }

        private void UpdateSearchButtonVisibilityForStatus()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(UpdateSearchButtonVisibilityForStatus);
                return;
            }
            if (_viewModel.HideClipboard)
            {
                SearchToggleButton.Visibility = System.Windows.Visibility.Collapsed;
                if (StatusToastPopup != null) StatusToastPopup.IsOpen = false;
                return;
            }
            if (_searchBoxBorder != null) return;
            var status = _viewModel.DebugStatus;
            bool active = !string.IsNullOrEmpty(status) && status != "Ready";
            SearchToggleButton.Visibility = active ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

            // Show/hide status toast popup with animation
            if (StatusToastPopup != null && StatusToastBorder != null)
            {
                StatusToastPopup.PlacementTarget = null;
                if (active)
                {
                    StatusToastPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                    StatusToastPopup.IsOpen = true;
                    // Defer centering so ActualWidth is available after first layout
                    Dispatcher.BeginInvoke(() =>
                    {
                        double shelfCenter = ActualWidth / 2.0;
                        StatusToastPopup.HorizontalOffset = shelfCenter - StatusToastBorder.ActualWidth / 2.0;
                    }, System.Windows.Threading.DispatcherPriority.Loaded);
                    AnimateToastIn();
                }
                else
                {
                    AnimateToastOut(() => StatusToastPopup.IsOpen = false);
                }
            }
        }

        private void UpdateLayoutForHideClipboard()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(UpdateLayoutForHideClipboard);
                return;
            }

            bool hidden = _viewModel.HideClipboard;

            if (hidden)
            {
                // Collapse the * column so grid sizes to content
                if (ItemsColumn != null) ItemsColumn.Width = new GridLength(0);
                // Move left monitors into col 3 (between collapsed items cols and right items)
                if (LeftMonitorsPanel != null) System.Windows.Controls.Grid.SetColumn(LeftMonitorsPanel, 3);
                // Center the grid — with all Auto cols and no *, it sizes to content and centers
                ShelfGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            }
            else
            {
                // Restore * column
                if (ItemsColumn != null) ItemsColumn.Width = new GridLength(1, GridUnitType.Star);
                // Restore left monitors to col 0
                if (LeftMonitorsPanel != null) System.Windows.Controls.Grid.SetColumn(LeftMonitorsPanel, 0);
                // Stretch grid back
                ShelfGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            }
        }

        private void AnimateToastIn()
        {
            var tg = StatusToastBorder.RenderTransform as System.Windows.Media.TransformGroup;
            if (tg == null) return;
            var scale = tg.Children[0] as System.Windows.Media.ScaleTransform;
            var translate = tg.Children[1] as System.Windows.Media.TranslateTransform;

            var scaleX = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(250))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
            var scaleY = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(250))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
            var slideY = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(250))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
            var opacity = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(200));

            StatusToastBorder.Opacity = 0;
            StatusToastBorder.BeginAnimation(OpacityProperty, opacity);
            if (scale != null) { scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleX); scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleY); }
            if (translate != null) translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideY);
        }

        private void AnimateToastOut(Action onComplete)
        {
            var tg = StatusToastBorder.RenderTransform as System.Windows.Media.TransformGroup;
            if (tg == null) { onComplete?.Invoke(); return; }
            var scale = tg.Children[0] as System.Windows.Media.ScaleTransform;
            var translate = tg.Children[1] as System.Windows.Media.TranslateTransform;

            var scaleX = new System.Windows.Media.Animation.DoubleAnimation(0.8, TimeSpan.FromMilliseconds(180))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } };
            var scaleY = new System.Windows.Media.Animation.DoubleAnimation(0.8, TimeSpan.FromMilliseconds(180))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } };
            var slideY = new System.Windows.Media.Animation.DoubleAnimation(10, TimeSpan.FromMilliseconds(180))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } };
            var opacity = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(150));

            StatusToastBorder.BeginAnimation(OpacityProperty, opacity);
            if (scale != null) { scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleX); scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleY); }
            if (translate != null)
            {
                var anim = slideY;
                anim.Completed += (s, e) => onComplete?.Invoke();
                translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, anim);
            }
            else
            {
                onComplete?.Invoke();
            }
        }

        private void UpdateTheme()
        {
            if (_viewModel?.SettingsViewModel == null) return;

            var theme = _viewModel.SettingsViewModel.Theme;

            void SetResource(string key, object value)
            {
                System.Windows.Application.Current.Resources[key] = value;
                this.Resources[key] = value;
            }

            if (RootGrid != null)
                RootGrid.Background = System.Windows.Media.Brushes.Transparent;

            // ── Transparent Mode ──────────────────────────────────────────────
            if (theme == "Transparent")
            {
                SetResource("AppBackground", System.Windows.Media.Brushes.Transparent);
                SetResource("TextColor", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White));
                SetResource("IconColor", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White));
                SetResource("CardBg", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)));
                SetResource("WidgetBg", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)));
                SetResource("ControlBg", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)));
                SetResource("BorderColor", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)));
                SetResource("AccentColor", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x60, 0xCD, 0xFF)));
                SetResource("AccentColorDim", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0x60, 0xCD, 0xFF)));
                SetResource("MenuBg", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE6, 0x18, 0x18, 0x18)));
                SetResource("ToolTipBg", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE6, 0x10, 0x10, 0x10)));
                SetResource("WindowBg", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x00, 0x00, 0x00, 0x00)));
                SetResource("ShadowOpacity", 0.0d);   // No shadow — bar is invisible
                SetResource("ShadowColor", System.Windows.Media.Colors.Black);

                // Selected gradient for transparent mode
                var transparentGradient = new System.Windows.Media.LinearGradientBrush(
                    System.Windows.Media.Colors.Transparent, System.Windows.Media.Colors.Transparent,
                    new System.Windows.Point(0, 0), new System.Windows.Point(1, 0));
                transparentGradient.GradientStops.Clear();
                transparentGradient.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0xB3, 0x60, 0xCD, 0xFF), 0.0));
                transparentGradient.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0x40, 0x60, 0xCD, 0xFF), 0.6));
                transparentGradient.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 1.0));
                SetResource("SelectedItemGradient", transparentGradient);
            }
            else
            {
                // ── Light / Dark / System ─────────────────────────────────────
                bool isLightTheme = theme == "Light";
                if (theme == "System")
                {
                    var registryValue = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
                    isLightTheme = registryValue != null && (int)registryValue == 1;
                }

                // Solid colors — no acrylic/blur in either mode
                // Dark mode uses a subtle vertical gradient for depth instead of flat #141414
                System.Windows.Media.Brush shelfBrush;
                if (isLightTheme)
                {
                    shelfBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));
                }
                else
                {
                    var darkGrad = new System.Windows.Media.LinearGradientBrush(
                        System.Windows.Media.Color.FromRgb(0x12, 0x12, 0x12),
                        System.Windows.Media.Color.FromRgb(0x06, 0x06, 0x06),
                        new System.Windows.Point(0, 0),
                        new System.Windows.Point(0, 1));
                    shelfBrush = darkGrad;
                }

                var textColor = new System.Windows.Media.SolidColorBrush(isLightTheme ? System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x22) : System.Windows.Media.Colors.White);
                var iconColor = new System.Windows.Media.SolidColorBrush(isLightTheme ? System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x22) : System.Windows.Media.Colors.White);

                // Stronger card contrast in dark mode for better visual hierarchy
                // Card is nearly invisible — blends with shelf for ultra-minimal look
                var cardBg = new System.Windows.Media.SolidColorBrush(isLightTheme
                    ? System.Windows.Media.Color.FromArgb(0x10, 0x00, 0x00, 0x00)
                    : System.Windows.Media.Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF));
                var controlBg = new System.Windows.Media.SolidColorBrush(isLightTheme
                    ? System.Windows.Media.Color.FromArgb(0x33, 0x00, 0x00, 0x00)
                    : System.Windows.Media.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
                var borderColor = new System.Windows.Media.SolidColorBrush(isLightTheme
                    ? System.Windows.Media.Color.FromArgb(30, 0, 0, 0)
                    : System.Windows.Media.Color.FromArgb(40, 255, 255, 255));
                var menuBg = new System.Windows.Media.SolidColorBrush(isLightTheme
                    ? System.Windows.Media.Color.FromRgb(0xFA, 0xFA, 0xFA)
                    : System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E));
                var tooltipBg = new System.Windows.Media.SolidColorBrush(isLightTheme
                    ? System.Windows.Media.Color.FromArgb(245, 250, 250, 250)
                    : System.Windows.Media.Color.FromArgb(245, 30, 30, 30));
                var accentColor = new System.Windows.Media.SolidColorBrush(isLightTheme
                    ? System.Windows.Media.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4)
                    : System.Windows.Media.Color.FromArgb(0xFF, 0x60, 0xCD, 0xFF));
                var accentColorDim = new System.Windows.Media.SolidColorBrush(isLightTheme
                    ? System.Windows.Media.Color.FromArgb(0x40, 0x00, 0x78, 0xD4)
                    : System.Windows.Media.Color.FromArgb(0x40, 0x60, 0xCD, 0xFF));

                SetResource("AppBackground", shelfBrush);
                SetResource("TextColor", textColor);
                SetResource("IconColor", iconColor);
                SetResource("CardBg", cardBg);
                SetResource("WidgetBg", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                    isLightTheme ? (byte)0x08 : (byte)0x05, 0xFF, 0xFF, 0xFF)));
                SetResource("ControlBg", controlBg);
                SetResource("BorderColor", borderColor);
                SetResource("MenuBg", menuBg);
                SetResource("ToolTipBg", tooltipBg);
                SetResource("AccentColor", accentColor);
                SetResource("AccentColorDim", accentColorDim);

                // Selected item gradient: left = accent (lighter), right = card/shelf color (blends in)
                System.Windows.Media.Color accentRgb = ((System.Windows.Media.SolidColorBrush)accentColor).Color;
                var selectedGradient = new System.Windows.Media.LinearGradientBrush(
                    System.Windows.Media.Colors.Transparent,  // placeholder
                    System.Windows.Media.Colors.Transparent,
                    new System.Windows.Point(0, 0),
                    new System.Windows.Point(1, 0));
                selectedGradient.GradientStops.Clear();
                // Left side: accent color at ~70% opacity
                var accentAtStop = System.Windows.Media.Color.FromArgb(0xB3, accentRgb.R, accentRgb.G, accentRgb.B);
                selectedGradient.GradientStops.Add(new System.Windows.Media.GradientStop(accentAtStop, 0.0));
                // Mid: fade
                var accentFade = System.Windows.Media.Color.FromArgb(0x40, accentRgb.R, accentRgb.G, accentRgb.B);
                selectedGradient.GradientStops.Add(new System.Windows.Media.GradientStop(accentFade, 0.6));
                // Right side: blends into card background (no color change)
                var cardRgb = ((System.Windows.Media.SolidColorBrush)cardBg).Color;
                selectedGradient.GradientStops.Add(new System.Windows.Media.GradientStop(cardRgb, 1.0));
                SetResource("SelectedItemGradient", selectedGradient);

                SetResource("WindowBg", new System.Windows.Media.SolidColorBrush(isLightTheme
                    ? System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)
                    : System.Windows.Media.Color.FromRgb(0x14, 0x14, 0x14)));
                SetResource("ShadowOpacity", isLightTheme ? 0.2d : 0.45d);
                SetResource("ShadowColor", System.Windows.Media.Colors.Black);
            }

            if (ItemsHost != null)
            {
                ItemsHost.Items.Refresh();
            }

            // Force visual tree to re-evaluate DynamicResource references
            ForceRefreshVisualTree(this);

            Log("Theme updated.");
        }

        private void ForceRefreshVisualTree(DependencyObject parent)
        {
            try
            {
                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                    if (child is System.Windows.Controls.Border border)
                    {
                        border.InvalidateProperty(System.Windows.Controls.Border.BackgroundProperty);
                        border.InvalidateProperty(System.Windows.Controls.Border.BorderBrushProperty);
                    }
                    if (child is System.Windows.Controls.Button btn)
                    {
                        btn.InvalidateProperty(System.Windows.Controls.Button.BackgroundProperty);
                        btn.InvalidateProperty(System.Windows.Controls.Button.ForegroundProperty);
                        btn.InvalidateProperty(System.Windows.Controls.Button.BorderBrushProperty);
                    }
                    if (child is System.Windows.Controls.TextBlock tb)
                    {
                        tb.InvalidateProperty(System.Windows.Controls.TextBlock.ForegroundProperty);
                    }
                    if (child is System.Windows.Shapes.Shape shape)
                    {
                        shape.InvalidateProperty(System.Windows.Shapes.Shape.FillProperty);
                        shape.InvalidateProperty(System.Windows.Shapes.Shape.StrokeProperty);
                    }
                    if (child is System.Windows.Controls.ContentControl cc)
                    {
                        cc.InvalidateProperty(System.Windows.Controls.ContentControl.ForegroundProperty);
                    }
                    ForceRefreshVisualTree(child);
                }
            }
            catch { }
        }

        private void ApplyCustomTheme(SettingsViewModel svm)
        {
            try
            {
                var bgColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomWindowBgColor);
                var shelfBrush = new System.Windows.Media.SolidColorBrush(bgColor);
                if (RootGrid != null)
                    RootGrid.Background = System.Windows.Media.Brushes.Transparent;

                void SetResource(string key, object value)
                {
                    System.Windows.Application.Current.Resources[key] = value;
                    this.Resources[key] = value;
                }

                SetResource("AppBackground", shelfBrush);
                SetResource("CardBg", new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomCardColor)));
                SetResource("WidgetBg", new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomCardColor)));
                SetResource("ControlBg", new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomControlBgColor)));
                SetResource("AccentColor", new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomAccentColor)));
                SetResource("WindowBg", new System.Windows.Media.SolidColorBrush(bgColor));
                SetResource("BorderColor", new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomBorderColor)));
                SetResource("TextColor", new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomTextColor)));
                SetResource("IconColor", new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomIconColor)));

                // Build custom selected gradient
                var accentC = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomAccentColor);
                var cardC = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomCardColor);
                var customGrad = new System.Windows.Media.LinearGradientBrush(
                    System.Windows.Media.Colors.Transparent, System.Windows.Media.Colors.Transparent,
                    new System.Windows.Point(0, 0), new System.Windows.Point(1, 0));
                customGrad.GradientStops.Clear();
                customGrad.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0xB3, accentC.R, accentC.G, accentC.B), 0.0));
                customGrad.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0x40, accentC.R, accentC.G, accentC.B), 0.6));
                customGrad.GradientStops.Add(new System.Windows.Media.GradientStop(cardC, 1.0));
                SetResource("SelectedItemGradient", customGrad);

                if (ItemsHost != null)
                    ItemsHost.Items.Refresh();
            }
            catch { }
        }

        private void ShowWindow()
        {
            Log("ShowWindow invoked");
            // Use Visibility instead of Show()/Hide() to keep the HWND alive.
            // Show() recreates the HWND but the AppBar and clipboard listener are
            // registered against the original HWND from OnSourceInitialized.
            Visibility = Visibility.Visible;
            RegisterAppBar();
            SetAppBarPos();
        }

        private void HideWindow()
        {
            Log("HideWindow invoked");
            UnregisterAppBar();
            // Use Collapsed rather than Hide() — Hide() destroys the Win32 HWND,
            // breaking the AppBar registration and clipboard listener on re-show.
            Visibility = Visibility.Collapsed;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Log("CloseButton_Click invoked");
            _viewModel.IsShelfVisible = false;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            Log("OnClosing invoked — cancelling and hiding");
            // Just cancel the close and hide the shelf instead
            e.Cancel = true;
            _viewModel.IsShelfVisible = false;
        }

        protected override void OnClosed(EventArgs e)
        {
            Log($"OnClosed invoked. StackTrace:\n{Environment.StackTrace}");
            base.OnClosed(e);
        }

        private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            {
                EnsureClipboardListener();
            }
        }

        private void OnWindowActivated(object? sender, EventArgs e)
        {
            EnsureClipboardListener();
        }

        public void EnsureClipboardListener()
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero)
                {
                    var ok = AddClipboardFormatListener(handle);
                    Log($"Clipboard listener ensured. handle={handle} ok={ok}");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to ensure clipboard listener: {ex.Message}");
            }
        }

        private System.Windows.Window _deleteBubble;
        private bool _dismissingBubble;
        private bool _isPotentialClick;
        private ClipboardItem _clickedItem;
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static IntPtr _hookId = IntPtr.Zero;
        private static LowLevelMouseProc _hookProc;
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x201;
        private const int WM_RBUTTONDOWN = 0x204;
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out System.Drawing.Point lpPoint);

        private void InstallMouseHook()
        {
            if (_hookId != IntPtr.Zero) return;
            _hookProc = MouseHookCallback;
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                _hookId = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private void UninstallMouseHook()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                _hookProc = null;
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam;
                if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN)
                {
                    if (_openContextMenu != null && _openContextMenu.IsOpen)
                    {
                        GetCursorPos(out System.Drawing.Point pt);
                        IntPtr hwndAtClick = WindowFromPoint(pt);
                        uint clickPid;
                        GetWindowThreadProcessId(hwndAtClick, out clickPid);
                        if (clickPid != _myProcessId)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (_openContextMenu != null)
                                {
                                    _openContextMenu.IsOpen = false;
                                    _openContextMenu = null;
                                }
                                CloseDeleteBubble();
                            }));
                        }
                    }

                    if (_deleteBubble != null)
                    {
                        GetCursorPos(out System.Drawing.Point pt);
                        var bubbleRect = new System.Drawing.Rectangle(
                            (int)_deleteBubble.Left, (int)_deleteBubble.Top,
                            (int)_deleteBubble.ActualWidth, (int)_deleteBubble.ActualHeight);
                        var mainRect = new System.Drawing.Rectangle(
                            (int)this.Left, (int)this.Top,
                            (int)this.ActualWidth, (int)this.ActualHeight);
                        if (!bubbleRect.Contains(pt) && !mainRect.Contains(pt))
                        {
                            Dispatcher.BeginInvoke(new Action(CloseDeleteBubble));
                        }
                    }

                    // Close search on any click outside our window
                    if (_searchBoxBorder != null && _searchBoxBorder.Visibility == System.Windows.Visibility.Visible)
                    {
                        GetCursorPos(out System.Drawing.Point pt);
                        IntPtr hwndAtClick = WindowFromPoint(pt);
                        uint clickPid;
                        GetWindowThreadProcessId(hwndAtClick, out clickPid);
                        if (clickPid != _myProcessId)
                        {
                            Dispatcher.BeginInvoke(new Action(CloseSearch));
                        }
                    }
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void CloseDeleteBubble()
        {
            if (_deleteBubble != null)
            {
                _dismissingBubble = true;
                var w = _deleteBubble;
                _deleteBubble = null;
                try { w.Close(); }
                catch { }
                _dismissingBubble = false;
            }
        }

        private void ShowDeleteBubble(FrameworkElement source, ClipboardItem item)
        {
            CloseDeleteBubble();

            var cardBg = System.Windows.Application.Current.Resources["CardBg"] as System.Windows.Media.SolidColorBrush;
            var baseCardColor = cardBg != null ? cardBg.Color : System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x28);

            var ft = new System.Windows.Media.FormattedText(
                "✕  Remove this item",
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface(System.Windows.SystemFonts.MessageFontFamily,
                    System.Windows.FontStyles.Normal, System.Windows.FontWeights.Normal,
                    System.Windows.FontStretches.Normal),
                12,
                System.Windows.Media.Brushes.White,
                System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double bw = Math.Ceiling(ft.Width) + 20;
            double bh = Math.Ceiling(ft.Height) + 10;

            _deleteBubble = new System.Windows.Window();
            _deleteBubble.WindowStyle = System.Windows.WindowStyle.None;
            _deleteBubble.AllowsTransparency = false;
            _deleteBubble.Background = new System.Windows.Media.SolidColorBrush(baseCardColor);
            _deleteBubble.Topmost = true;
            _deleteBubble.MinWidth = bw; _deleteBubble.MaxWidth = bw;
            _deleteBubble.MinHeight = bh; _deleteBubble.MaxHeight = bh;
            _deleteBubble.ShowInTaskbar = false;
            _deleteBubble.ShowActivated = false;
            _deleteBubble.ResizeMode = System.Windows.ResizeMode.NoResize;
            _deleteBubble.Content = new System.Windows.Controls.TextBlock
            {
                Text = "✕  Remove this item",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            _deleteBubble.Loaded += (s, args) =>
            {
                var itemCenter = source.PointToScreen(new System.Windows.Point(source.ActualWidth / 2, 0));
                _deleteBubble.Left = itemCenter.X - bw / 2;
                _deleteBubble.Top = this.Top + this.Height + 4;
            };

            _deleteBubble.MouseLeftButtonDown += (s, args) =>
            {
                CloseDeleteBubble();
                _ = _viewModel.DeleteItemCommand.ExecuteAsync(item);
            };
            _deleteBubble.Show();
        }

        // Context menu dismiss on any click outside
        private void ClearItemHighlight()
        {
            if (_highlightedItemBorder != null)
            {
                if (_savedItemBg != null)
                    _highlightedItemBorder.Background = _savedItemBg;
                else
                    _highlightedItemBorder.ClearValue(System.Windows.Controls.Border.BackgroundProperty);
                _highlightedItemBorder = null;
                _savedItemBg = null;

                // Restore tooltip before clearing
                if (_toolTipTarget != null && _savedItemToolTip != null)
                {
                    _toolTipTarget.ToolTip = _savedItemToolTip;
                }
                _savedItemToolTip = null;
                _toolTipTarget = null;
            }
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Close search if clicking outside the search box
            if (_searchBoxBorder != null && _searchBoxBorder.Visibility == System.Windows.Visibility.Visible)
            {
                var pos = e.GetPosition(_searchBoxBorder);
                if (pos.X < 0 || pos.Y < 0 || pos.X > _searchBoxBorder.ActualWidth || pos.Y > _searchBoxBorder.ActualHeight)
                {
                    CloseSearch();
                }
                else
                {
                    // Click inside search box - prevent stale click-to-paste from previous item
                    _isPotentialClick = false;
                    _clickedItem = null;
                }
                return;
            }

            CloseDeleteBubble();
            if (_openContextMenu != null && _openContextMenu.IsOpen)
            {
                _openContextMenu.IsOpen = false;
                _openContextMenu = null;
            }
            else
            {
                ClearItemHighlight();
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                // Only register potential click if it originated inside ItemsHost
                var source = e.OriginalSource as System.Windows.DependencyObject;
                bool inItemsHost = false;
                var walker = source;
                while (walker != null)
                {
                    if (walker == ItemsHost) { inItemsHost = true; break; }
                    walker = System.Windows.Media.VisualTreeHelper.GetParent(walker);
                }
                if (!inItemsHost)
                {
                    _isPotentialClick = false;
                    _clickedItem = null;
                    return;
                }

                _isPotentialClick = true;
                _clickedItem = null;
                var pos = e.GetPosition(ItemsHost);
                var hit = ItemsHost.InputHitTest(pos) as System.Windows.DependencyObject;
                while (hit != null)
                {
                    if (hit is System.Windows.FrameworkElement fe && fe.DataContext is ClipboardItem item)
                    {
                        _clickedItem = item;
                        break;
                    }
                    hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
                }
            }
        }

        private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && _isPotentialClick && _clickedItem != null)
            {
                _isPotentialClick = false;
                if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                    _ = _viewModel.DeleteItemCommand.ExecuteAsync(_clickedItem);
                else if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                {
                    // Shift+click always pastes, even in multi-paste mode
                    _viewModel.IsMultiPasteMode = false;
                    _viewModel.ItemClickedCommand.Execute(_clickedItem);
                }
                else
                    _viewModel.ItemClickedCommand.Execute(_clickedItem);
            }
        }

        // Drag & Drop Handling
        private void Window_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent("ClipDropItemDelete"))
                e.Effects = System.Windows.DragDropEffects.Move;
        }

        private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            // Ctrl+drag from shelf dropped back onto shelf — cancel deletion
            if (e.Data.GetDataPresent("ClipDropItemDelete"))
            {
                _ctrlDragCancelled = true;
                e.Handled = true;
                return;
            }

            // Regular drag from shelf dropped back onto shelf — ignore (would duplicate)
            if (e.Data.GetDataPresent("ClipDropShelfOrigin"))
            {
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                await _viewModel.HandleDroppedFilesAsync(files);
            }
            else if (e.Data.GetDataPresent(System.Windows.DataFormats.Text))
            {
                string text = (string)e.Data.GetData(System.Windows.DataFormats.Text);
                await _viewModel.HandleDroppedTextAsync(text);
            }
        }

        // P/Invoke for sending input
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        const int INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;

        private async void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is ClipboardItem item)
            {
                await _viewModel.DeleteItemCommand.ExecuteAsync(item);
            }
        }

        private void Item_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            Log($"PreviewMouseMove: LeftButton={e.LeftButton}");
            if (e.LeftButton == MouseButtonState.Pressed && _dragStartPoint != default)
            {
                System.Windows.Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint - mousePos;
                Log($"PreviewMouseMove: diff={diff.Length:F1} start={_dragStartPoint} pos={mousePos}");

                if (Math.Abs(diff.X) > 5 || Math.Abs(diff.Y) > 5)
                {
                    _isPotentialClick = false;
                    if (sender is FrameworkElement element && element.DataContext is ClipboardItem item)
                    {
                        // Ctrl + drag to delete
                        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                        {
                            _ctrlDragCancelled = false;
                            element.Opacity = 0.3;
                            ShowDragPopup(element, item);
                            var deleteData = new System.Windows.DataObject("ClipDropItemDelete", item);
                            System.Windows.DragDrop.AddQueryContinueDragHandler(element, QueryContinueDragHandler);
                            System.Windows.DragDrop.AddGiveFeedbackHandler(element, GiveFeedbackHandler);
                            System.Windows.DragDrop.DoDragDrop(element, deleteData, System.Windows.DragDropEffects.Move);
                            System.Windows.DragDrop.RemoveGiveFeedbackHandler(element, GiveFeedbackHandler);
                            System.Windows.DragDrop.RemoveQueryContinueDragHandler(element, QueryContinueDragHandler);

                            CloseDragPopup();
                            element.Opacity = 1.0;
                            if (!_ctrlDragCancelled)
                            {
                                // Check if dropped back on shelf — if so, cancel deletion
                                var pt = Win32GetCursorPos();
                                var presentationSource = PresentationSource.FromVisual(this);
                                double dpiFactor = presentationSource?.CompositionTarget.TransformToDevice.M22 ?? 1.0;
                                var shelfRect = new System.Drawing.Rectangle(
                                    (int)(this.Left * dpiFactor), (int)(this.Top * dpiFactor),
                                    (int)Math.Max(this.ActualWidth * dpiFactor, 1), (int)Math.Max(this.ActualHeight * dpiFactor, 1));
                                if (!shelfRect.Contains(new System.Drawing.Point((int)pt.X, (int)pt.Y)))
                                    _ = _viewModel.DeleteItemCommand.ExecuteAsync(item);
                            }
                            return;
                        }

                        string tempFilePath = null;
                        Log("DragStart: preparing drag data");

                        if (item.IsImage && !string.IsNullOrEmpty(item.FilePath) && System.IO.File.Exists(item.FilePath))
                        {
                            // Image drag: create temp copy with original filename + DIB bitmap for preview
                            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TotthodharaDrag");
                            try { if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
                            System.IO.Directory.CreateDirectory(tempDir);
                            var imgFileName = !string.IsNullOrEmpty(item.FileName) ? item.FileName : System.IO.Path.GetFileName(item.FilePath);
                            var imgExt = System.IO.Path.GetExtension(item.FilePath);
                            if (!string.IsNullOrEmpty(imgExt) && !imgFileName.EndsWith(imgExt, System.StringComparison.OrdinalIgnoreCase))
                                imgFileName += imgExt;
                            tempFilePath = System.IO.Path.Combine(tempDir, imgFileName);
                            System.IO.File.Copy(item.FilePath, tempFilePath, overwrite: true);
                            var data = new System.Windows.DataObject();
                            data.SetData("ClipDropShelfOrigin", item);
                            data.SetFileDropList(new System.Collections.Specialized.StringCollection { tempFilePath });
                            // DIB format for live preview in image-aware targets (WhatsApp, etc.)
                            using (var bmp = new System.Drawing.Bitmap(item.FilePath))
                            using (var ms = new System.IO.MemoryStream())
                            {
                                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                                var dib = new byte[ms.Length - 14];
                                System.Buffer.BlockCopy(ms.GetBuffer(), 14, dib, 0, dib.Length);
                                data.SetData("DeviceIndependentBitmap", new System.IO.MemoryStream(dib));
                            }
                            Log($"DragImage: {imgFileName} + DIB");
                            var result = System.Windows.DragDrop.DoDragDrop(element, data, System.Windows.DragDropEffects.Copy);
                            Log($"DragEnd: result={result}");
                        }
                        else if (item.IsFile && !string.IsNullOrEmpty(item.FilePath) && System.IO.File.Exists(item.FilePath))
                        {
                            // Non-image file drag: create temp copy with original filename for consistency with click-to-paste
                            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TotthodharaDrag");
                            try { if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
                            System.IO.Directory.CreateDirectory(tempDir);
                            var fileName = !string.IsNullOrEmpty(item.FileName) ? item.FileName : System.IO.Path.GetFileName(item.FilePath);
                            var sourceExt = System.IO.Path.GetExtension(item.FilePath);
                            if (!string.IsNullOrEmpty(sourceExt) && !fileName.EndsWith(sourceExt, System.StringComparison.OrdinalIgnoreCase))
                                fileName += sourceExt;
                            tempFilePath = System.IO.Path.Combine(tempDir, fileName);
                            System.IO.File.Copy(item.FilePath, tempFilePath, overwrite: true);
                            var data = new System.Windows.DataObject();
                            data.SetData("ClipDropShelfOrigin", item);
                            data.SetFileDropList(new System.Collections.Specialized.StringCollection { tempFilePath });
                            Log($"DragFile: {fileName}");
                            var result = System.Windows.DragDrop.DoDragDrop(element, data, System.Windows.DragDropEffects.Copy);
                            Log($"DragEnd: result={result}");
                        }
                        else if (!string.IsNullOrEmpty(item.TextContent))
                        {
                            // Text drag: only set text formats
                            var data = new System.Windows.DataObject();
                            data.SetData("ClipDropShelfOrigin", item);
                            data.SetData(System.Windows.DataFormats.UnicodeText, item.TextContent);
                            data.SetText(item.TextContent);
                            Log($"DragText: {item.TextContent.Substring(0, Math.Min(40, item.TextContent.Length))}");
                            var result = System.Windows.DragDrop.DoDragDrop(element, data, System.Windows.DragDropEffects.Copy);
                            Log($"DragEnd: result={result}");
                        }
                    }
                }
            }
            else
            {
                _dragStartPoint = e.GetPosition(null);
            }
        }

        private bool _ctrlDragCancelled;
        private System.Windows.Window _dragPopup;
        private void ShowDragPopup(FrameworkElement sourceElement, ClipboardItem item)
        {
            double circleSize = (double)Resources["ItemCircleSize"];
            double fontSize = (double)Resources["ItemFontSize"];
            double padH = ((System.Windows.Thickness)Resources["ItemPadding"]).Left;
            double padV = ((System.Windows.Thickness)Resources["ItemPadding"]).Top;

            var text = (string.IsNullOrEmpty(item.DisplayText) ? item.FileName : item.DisplayText) ?? "";
            double itemH = circleSize + padV * 2;
            double textW = Math.Min(text.Length, 8) * fontSize * 0.55;
            double itemW = padH + circleSize + 4 + textW + 6 + padH + 4;
            itemW = Math.Max(itemW, 50);

            var stack = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            stack.Children.Add(new Border
            {
                Width = circleSize,
                Height = circleSize,
                CornerRadius = new System.Windows.CornerRadius(circleSize / 2),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 255, 255, 255)),
                Child = new System.Windows.Controls.TextBlock
                {
                    Text = item.Index.ToString(),
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = fontSize,
                    FontWeight = System.Windows.FontWeights.Bold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                }
            });
            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = text,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = fontSize,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new System.Windows.Thickness(4, 0, 6, 0)
            });

            _dragPopup = new System.Windows.Window();
            _dragPopup.WindowStyle = System.Windows.WindowStyle.None;
            _dragPopup.AllowsTransparency = true;
            _dragPopup.Background = null;
            _dragPopup.Topmost = true;
            _dragPopup.MinWidth = itemW; _dragPopup.MaxWidth = itemW;
            _dragPopup.MinHeight = itemH; _dragPopup.MaxHeight = itemH;
            _dragPopup.ShowInTaskbar = false;
            _dragPopup.ShowActivated = false;
            _dragPopup.ResizeMode = System.Windows.ResizeMode.NoResize;
            _dragPopup.IsHitTestVisible = false;
            var dragBorder = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0x22, 0x22, 0x28)),
                CornerRadius = new System.Windows.CornerRadius(6),
                Padding = new System.Windows.Thickness(padH, padV, padH, padV),
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new System.Windows.Media.ScaleTransform(0.95, 0.95),
                Opacity = 0.85,
                Child = stack
            };
            _dragPopup.Content = dragBorder;
            _dragPopup.Show();
        }

        private void CloseDragPopup()
        {
            if (_dragPopup != null)
            {
                _dragPopup.Close();
                _dragPopup = null;
            }
        }

        private void UpdateDragPopupPosition()
        {
            if (_dragPopup != null)
            {
                var point = Win32GetCursorPos();
                _dragPopup.Left = point.X - 60;
                _dragPopup.Top = point.Y - 16;
            }
        }

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);

        private struct POINT { public int X; public int Y; }

        private static System.Windows.Point Win32GetCursorPos()
        {
            GetCursorPos(out POINT pt);
            return new System.Windows.Point(pt.X, pt.Y);
        }

        private void GiveFeedbackHandler(object sender, System.Windows.GiveFeedbackEventArgs e)
        {
            UpdateDragPopupPosition();
            e.UseDefaultCursors = true;
            e.Handled = true;
        }

        private void QueryContinueDragHandler(object sender, System.Windows.QueryContinueDragEventArgs e)
        {
            if (e.EscapePressed)
            {
                _ctrlDragCancelled = true;
                e.Action = System.Windows.DragAction.Cancel;
            }
        }

        private System.Windows.Point _dragStartPoint;
        private void ListView_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Just selection tracking
        }

        private bool _isInternalButtonClick = false;
        private void Button_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isInternalButtonClick = true;
            Console.WriteLine("Button PreviewDown detected.");
        }

        private void ListView_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                // If we detected a button press, definitely don't start a drag
                if (_isInternalButtonClick) return;

                System.Windows.Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance * 2 ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance * 2)
                {
                    if (sender is System.Windows.Controls.ListView listView && listView.SelectedItem is ClipboardItem item)
                    {
                        string tempFilePath = null;
                        var data = new System.Windows.DataObject();
                        if (item.IsFile && !string.IsNullOrEmpty(item.FilePath) && System.IO.File.Exists(item.FilePath))
                        {
                            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TotthodharaDrag");
                            try { if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
                            System.IO.Directory.CreateDirectory(tempDir);
                            var fileName = !string.IsNullOrEmpty(item.FileName) ? item.FileName : System.IO.Path.GetFileName(item.FilePath);
                            tempFilePath = System.IO.Path.Combine(tempDir, fileName);
                            System.IO.File.Copy(item.FilePath, tempFilePath, overwrite: true);
                            var files = new System.Collections.Specialized.StringCollection { tempFilePath };
                            data.SetFileDropList(files);
                            data.SetText(tempFilePath);
                        }
                        else if (!string.IsNullOrEmpty(item.TextContent))
                        {
                            data.SetText(item.TextContent);
                        }

                        System.Windows.DragDrop.DoDragDrop(listView, data, System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move);
                        // After drag drops, reset.
                        _isInternalButtonClick = false;
                    }
                }
            }
            else
            {
                _isInternalButtonClick = false;
                _dragStartPoint = e.GetPosition(null);
            }
        }

        private void AnimateScroll(double from, double to)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double duration = 250;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (s, e) =>
            {
                double elapsed = sw.ElapsedMilliseconds;
                double t = Math.Min(elapsed / duration, 1.0);
                // Cubic ease-out
                t = 1.0 - Math.Pow(1.0 - t, 3);
                ShelfScrollViewer.ScrollToHorizontalOffset(from + (to - from) * t);
                if (elapsed >= duration) timer.Stop();
            };
            timer.Start();
        }

        private void ScrollLeft_Click(object sender, RoutedEventArgs e)
        {
            AnimateScroll(ShelfScrollViewer.HorizontalOffset, ShelfScrollViewer.HorizontalOffset - 250);
        }

        private void ScrollRight_Click(object sender, RoutedEventArgs e)
        {
            AnimateScroll(ShelfScrollViewer.HorizontalOffset, ShelfScrollViewer.HorizontalOffset + 250);
        }

        private void ShelfScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (ShelfScrollViewer != null)
            {
                ShelfScrollViewer.ScrollToHorizontalOffset(ShelfScrollViewer.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void TaskbarIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel.TrayIconAction == "Open Settings")
            {
                OpenSettings_Click(sender, e);
            }
            else
            {
                _viewModel.IsShelfVisible = !_viewModel.IsShelfVisible;
            }
        }

        private void ExitApp_Click(object sender, RoutedEventArgs e)
        {
            TrayIcon.Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            var updateService = App.GetService<ClipDropPro.Services.IUpdateService>();
            if (updateService == null) return;

            var info = await updateService.CheckForUpdateAsync();

            if (info.IsUpdateAvailable)
            {
                var result = ThemedMessageBox.Show(
                    this,
                    "Update Available",
                    $"A new version v{info.LatestVersion} is available!\n\nCurrent: v{updateService.GetCurrentVersion()}\n\nDownload and install now?",
                    ThemedMessageBox.Buttons.YesNo,
                    ThemedMessageBox.IconType.Info);

                if (result == ThemedMessageBox.Result.Yes && !string.IsNullOrEmpty(info.DownloadUrl))
                {
                    await RunUpdateWithProgressAsync(updateService, info);
                }
            }
            else if (!string.IsNullOrEmpty(info.ErrorMessage))
            {
                ThemedMessageBox.Show(
                    this,
                    "Update Check Failed",
                    $"Could not check for updates.\n\nError: {info.ErrorMessage}",
                    ThemedMessageBox.Buttons.OK,
                    ThemedMessageBox.IconType.Warning);
            }
            else
            {
                string detail;
                if (!string.IsNullOrEmpty(info.LatestVersion) && info.LatestVersion != updateService.GetCurrentVersion())
                    detail = $"Your version: v{updateService.GetCurrentVersion()} (newer than published)\nPublished: v{info.LatestVersion}";
                else
                    detail = $"Totthodhara v{updateService.GetCurrentVersion()} is the latest version.";
                if (info.PublishedAt.HasValue)
                    detail += $"\nLast checked: {info.PublishedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
                ThemedMessageBox.Show(
                    this,
                    "You're up to date",
                    detail,
                    ThemedMessageBox.Buttons.OK,
                    ThemedMessageBox.IconType.Info);
            }
        }

        private async Task RunUpdateWithProgressAsync(ClipDropPro.Services.IUpdateService updateService, ClipDropPro.Services.UpdateInfo info)
        {
            var accent = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["AccentColor"];
            var textColor = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextColor"] ?? System.Windows.Media.Brushes.Black;
            var windowBg = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["WindowBg"] ?? System.Windows.Media.Brushes.White;
            var borderColor = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BorderColor"] ?? System.Windows.Media.Brushes.Gray;

            // Outer grid: title bar + content
            var outerGrid = new System.Windows.Controls.Grid();
            outerGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(32) });
            outerGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Custom draggable title bar
            var titleBar = new System.Windows.Controls.Border
            {
                Background = windowBg,
                BorderBrush = borderColor,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var titleBarGrid = new System.Windows.Controls.Grid();
            titleBarGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBarGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
            titleBarGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
            titleBarGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
            titleBarGrid.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"Updating Totthodhara",
                Foreground = textColor,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            });
            // Minimize button
            var btnMin = new System.Windows.Controls.Button
            {
                Content = "\uE921",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = textColor,
                BorderThickness = new Thickness(0),
                Width = 32, Height = 32,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Minimize"
            };
            System.Windows.Controls.Grid.SetColumn(btnMin, 1);
            // Close button
            var btnClose = new System.Windows.Controls.Button
            {
                Content = "\uE8BB",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = textColor,
                BorderThickness = new Thickness(0),
                Width = 32, Height = 32,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Close (cancels download)"
            };
            System.Windows.Controls.Grid.SetColumn(btnClose, 2);
            titleBarGrid.Children.Add(btnMin);
            titleBarGrid.Children.Add(btnClose);
            titleBar.Child = titleBarGrid;
            System.Windows.Controls.Grid.SetRow(titleBar, 0);
            outerGrid.Children.Add(titleBar);

            // Body
            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            System.Windows.Controls.Grid.SetRow(panel, 1);

            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"Downloading v{info.LatestVersion}...",
                FontWeight = FontWeights.SemiBold,
                Foreground = textColor,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var statusText = new System.Windows.Controls.TextBlock
            {
                Text = "Downloading 0%...",
                Foreground = textColor,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(statusText);

            var progressBar = new System.Windows.Controls.ProgressBar
            {
                Minimum = 0, Maximum = 100, Height = 14,
                IsIndeterminate = false,
                Value = 0
            };
            panel.Children.Add(progressBar);

            // Buttons row
            var btnRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            // Cancel button
            var cancelBtn = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = textColor,
                BorderBrush = borderColor,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                MinWidth = 70
            };
            System.Windows.Controls.ControlTemplate secondaryTpl = null;
            {
                var tpl = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
                var bd = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
                bd.SetValue(System.Windows.Controls.Border.BackgroundProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Button.BackgroundProperty));
                bd.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Button.BorderBrushProperty));
                bd.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Button.BorderThicknessProperty));
                bd.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new System.Windows.CornerRadius(6));
                bd.SetValue(System.Windows.Controls.Border.PaddingProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Button.PaddingProperty));
                var cp = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
                cp.SetValue(System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
                cp.SetValue(System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
                bd.AppendChild(cp);
                tpl.VisualTree = bd;
                secondaryTpl = tpl;
            }
            cancelBtn.Template = secondaryTpl;

            // Pause/Resume button
            var pauseBtn = new System.Windows.Controls.Button
            {
                Content = "Pause",
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = textColor,
                BorderBrush = borderColor,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                MinWidth = 70
            };
            pauseBtn.Template = secondaryTpl;

            btnRow.Children.Add(pauseBtn);
            btnRow.Children.Add(cancelBtn);
            panel.Children.Add(btnRow);

            outerGrid.Children.Add(panel);

            var win = new System.Windows.Window
            {
                Title = "Updating Totthodhara",
                Width = 440,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = true,
                WindowStyle = WindowStyle.None,
                Background = windowBg,
                BorderBrush = borderColor,
                BorderThickness = new Thickness(1),
                Content = outerGrid
            };

            // Make title bar draggable
            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 1) win.DragMove();
            };

            // Cancel token source
            var cts = new System.Threading.CancellationTokenSource();
            System.Threading.CancellationTokenSource ctsRef = cts;
            bool isPaused = false;

            // Pause/Resume handler
            pauseBtn.Click += (s, e) =>
            {
                isPaused = !isPaused;
                if (isPaused)
                {
                    ctsRef.Cancel();
                    pauseBtn.Content = "Resume";
                    statusText.Text = "Paused";
                }
                else
                {
                    var newCts = new System.Threading.CancellationTokenSource();
                    ctsRef.Dispose();
                    ctsRef = newCts;
                    pauseBtn.Content = "Pause";
                    statusText.Text = "Resuming...";
                }
            };

            // Close handlers (title bar X + cancel button + window close)
            void DoCancel()
            {
                cts.Cancel();
                if (!win.Dispatcher.CheckAccess()) return;
                win.Close();
            }

            btnClose.Click += (s, e) => DoCancel();
            cancelBtn.Click += (s, e) => DoCancel();
            win.Closing += (s, e) =>
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
            };

            var progress = new Progress<double>(pct =>
            {
                win.Dispatcher.Invoke(() =>
                {
                    progressBar.Value = pct;
                    if (!isPaused)
                        statusText.Text = pct >= 100 ? "Download complete. Preparing update..." : $"Downloading {pct:F0}%...";
                });
            });

            var statusProgress = new Progress<string>(msg =>
            {
                win.Dispatcher.Invoke(() =>
                {
                    if (!isPaused)
                        statusText.Text = msg;
                });
            });

            btnMin.Click += (s, e) => win.WindowState = WindowState.Minimized;

            win.Show();
            win.Activate();
            await Task.Delay(300);

            bool ok = false;
            string downloadError = "";
            try
            {
                ok = await updateService.DownloadAndInstallAsync(info, progress, cts.Token, statusProgress);
                if (cts.IsCancellationRequested)
                {
                    downloadError = "Download cancelled.";
                    ok = false;
                }
            }
            catch (System.OperationCanceledException)
            {
                downloadError = "Download cancelled by user.";
                ok = false;
            }
            catch (Exception ex)
            {
                downloadError = ex.Message;
                ClipDropPro.Services.Logger.Write($"[MainWindow] Update error: {ex}");
            }

            win.Close();

            if (ok)
            {
                ThemedMessageBox.Show(
                    this,
                    "Update Ready",
                    "Update downloaded successfully. The app will now restart to apply the update.",
                    ThemedMessageBox.Buttons.OK,
                    ThemedMessageBox.IconType.Info);
                try { TrayIcon?.Dispose(); } catch { }
                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                string errorDetail = string.IsNullOrEmpty(downloadError) ? "Could not download or extract update." : downloadError;
                string openUrl = !string.IsNullOrEmpty(info.ReleasePageUrl)
                    ? info.ReleasePageUrl
                    : $"https://github.com/hungry-detective/Totthodhara/releases/tag/v{info.LatestVersion}";
                var fallback = ThemedMessageBox.Show(
                    this,
                    "Update Failed",
                    $"{errorDetail}\n\nWould you like to open the GitHub releases page to download manually?",
                    ThemedMessageBox.Buttons.YesNo,
                    ThemedMessageBox.IconType.Warning);
                if (fallback == ThemedMessageBox.Result.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = openUrl,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }
        }

        internal void ShowUpdateNotification(ClipDropPro.Services.UpdateInfo updateInfo)
        {
            try
            {
                var result = ThemedMessageBox.Show(
                    this,
                    "Update Available",
                    $"A new version v{updateInfo.LatestVersion} is available!\n\nCurrent: v{App.GetService<ClipDropPro.Services.IUpdateService>()?.GetCurrentVersion() ?? "?"}",
                    ThemedMessageBox.Buttons.YesNo,
                    ThemedMessageBox.IconType.Info);

                if (result == ThemedMessageBox.Result.Yes && !string.IsNullOrEmpty(updateInfo.DownloadUrl))
                {
                    var updateService = App.GetService<ClipDropPro.Services.IUpdateService>();
                    if (updateService != null)
                        _ = RunUpdateWithProgressAsync(updateService, updateInfo);
                }
            }
            catch (Exception ex)
            {
                ClipDropPro.Services.Logger.Write($"[MainWindow] ShowUpdateNotification error: {ex.Message}");
            }
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (MoreButton.ContextMenu != null)
            {
                if (MoreButton.ContextMenu.IsOpen)
                {
                    MoreButton.ContextMenu.IsOpen = false;
                }
                else
                {
                    // Window has WS_EX_NOACTIVATE which prevents activation on click.
                    // ContextMenu can't show on a non-activated window — user has to click
                    // multiple times. Temporarily remove the flag so the window activates,
                    // then restore on close.
                    DisableNoActivate();
                    SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                    this.Activate();
                    this.Focus();

                    System.Windows.Controls.Primitives.Popup popup = null;
                    System.Windows.RoutedEventHandler closedHandler = null;
                    closedHandler = (s2, e2) =>
                    {
                        MoreButton.ContextMenu.Closed -= closedHandler;
                        EnableNoActivate();
                    };
                    MoreButton.ContextMenu.Closed += closedHandler;

                    MoreButton.ContextMenu.PlacementTarget = MoreButton;
                    MoreButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                    MoreButton.ContextMenu.IsOpen = true;
                }
            }
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.OpenSettingsCommand.Execute(null);
        }

        private void WhatsNew_Click(object sender, RoutedEventArgs e)
        {
            var whatsNew = new WhatsNewWindow();
            whatsNew.Owner = this; // Set owner so it stays on top of our app if needed
            whatsNew.ShowDialog();
        }

        private async void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.ClearAllItemsCommand.ExecuteAsync(null);
        }

        #region Search, MultiPaste

        private void SearchToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.HideClipboard) return;
            // Prevent stale click-to-paste from previous item
            _isPotentialClick = false;
            _clickedItem = null;
            _viewModel.SearchText = string.Empty;
            SearchToggleButton.Visibility = System.Windows.Visibility.Collapsed;

            var cardBg = System.Windows.Application.Current.Resources["CardBg"] as System.Windows.Media.SolidColorBrush;
            var textColor = System.Windows.Application.Current.Resources["TextColor"] as System.Windows.Media.SolidColorBrush;
            var cardColor = cardBg?.Color ?? System.Windows.Media.Color.FromRgb(0x28, 0x28, 0x28);
            var txtColor = textColor?.Color ?? System.Windows.Media.Colors.White;

            // Minimal template with centered ScrollViewer
            var scrollViewerFactory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ScrollViewer));
            scrollViewerFactory.Name = "PART_ContentHost";
            scrollViewerFactory.SetValue(System.Windows.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty, System.Windows.Controls.ScrollBarVisibility.Hidden);
            scrollViewerFactory.SetValue(System.Windows.Controls.ScrollViewer.VerticalScrollBarVisibilityProperty, System.Windows.Controls.ScrollBarVisibility.Hidden);
            scrollViewerFactory.SetValue(System.Windows.FrameworkElement.MarginProperty, new System.Windows.Thickness(0));
            scrollViewerFactory.SetValue(System.Windows.FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            var textBoxTemplate = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.TextBox));
            textBoxTemplate.VisualTree = scrollViewerFactory;

            var tb = new System.Windows.Controls.TextBox
            {
                Width = 90,
                Height = 22,
                Margin = new System.Windows.Thickness(0),
                Padding = new System.Windows.Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = new System.Windows.Media.SolidColorBrush(txtColor),
                CaretBrush = new System.Windows.Media.SolidColorBrush(txtColor),
                BorderThickness = new System.Windows.Thickness(0),
                FontSize = 11,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                TextAlignment = System.Windows.TextAlignment.Center,
                Text = string.Empty,
                Template = textBoxTemplate
            };

            tb.TextChanged += SearchBox_TextChanged;
            tb.PreviewKeyDown += SearchBox_PreviewKeyDown;
            tb.LostFocus += SearchBox_LostFocus;

            var border = new System.Windows.Controls.Border
            {
                CornerRadius = new System.Windows.CornerRadius(10),
                Background = new System.Windows.Media.SolidColorBrush(cardColor),
                Height = 22,
                MinHeight = 22,
                MaxHeight = 22,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Child = tb
            };

            // Replace the button with the search box in the parent Grid
            var parentGrid = SearchToggleButton.Parent as System.Windows.Controls.Grid;
            if (parentGrid != null)
            {
                var col = System.Windows.Controls.Grid.GetColumn(SearchToggleButton);
                var row = System.Windows.Controls.Grid.GetRow(SearchToggleButton);
                SearchToggleButton.Visibility = System.Windows.Visibility.Collapsed;
                border.SetValue(System.Windows.Controls.Grid.ColumnProperty, col);
                border.SetValue(System.Windows.Controls.Grid.RowProperty, row);
                parentGrid.Children.Add(border);
                // Remove WS_EX_NOACTIVATE so keyboard input reaches the TextBox
                DisableNoActivate();
                try
                {
                    // Bring window to foreground so it receives keyboard input
                    SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                    // Focus after a short delay to ensure visual tree is ready
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        new System.Action(() =>
                        {
                            tb.Focus();
                            System.Windows.Input.Keyboard.Focus(tb);
                        }),
                        System.Windows.Threading.DispatcherPriority.Input);
                    _searchBoxBorder = border;
                    _searchTextBox = tb;
                }
                finally
                {
                    // If focus failed, re-enable NOACTIVATE
                    if (_searchTextBox == null || !_searchTextBox.IsFocused)
                    {
                        EnableNoActivate();
                    }
                }
            }
        }

        private void CloseSearch()
        {
            _viewModel.SearchText = string.Empty;
            if (_searchBoxBorder != null)
            {
                var parentGrid = _searchBoxBorder.Parent as System.Windows.Controls.Grid;
                parentGrid?.Children.Remove(_searchBoxBorder);
                _searchBoxBorder = null;
                _searchTextBox = null;
                EnableNoActivate();
            }
            SearchToggleButton.Visibility = System.Windows.Visibility.Visible;
            UpdateSearchButtonVisibilityForStatus();
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _viewModel.SearchText = (_searchTextBox ?? sender as System.Windows.Controls.TextBox)?.Text ?? string.Empty;
        }

        private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape || e.Key == System.Windows.Input.Key.Enter)
            {
                CloseSearch();
                e.Handled = true;
            }
        }

        private void SearchBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            // Delay to allow click events to process first
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                new System.Action(() =>
                {
                    if (_searchBoxBorder != null &&
                        !_searchTextBox.IsFocused && !_searchTextBox.IsKeyboardFocusWithin)
                    {
                        CloseSearch();
                    }
                }),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        private async void PasteAllButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.PasteAllCommand.ExecuteAsync(null);
        }

        private void MainWindow_Deactivated(object sender, EventArgs e)
        {
        }

        private async void OpenUrl_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is FrameworkElement element && element.DataContext is ClipboardItem item && !string.IsNullOrEmpty(item.TextContent))
            {
                try
                {
                    var uri = item.TextContent.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? item.TextContent
                        : "https://" + item.TextContent;
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
                    _viewModel.DebugStatus = "Opening URL...";
                    await Task.Delay(1000);
                    _viewModel.DebugStatus = "Ready";
                }
                catch { }
            }
        }

        private void OpenUrl_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
        }
        #endregion
    }
}
