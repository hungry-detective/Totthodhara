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
            double sysTextSize = Math.Max(10, fontSize + 2);
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



            Loaded += MainWindow_Loaded;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            if (_viewModel.SettingsViewModel != null)
            {
                _viewModel.SettingsViewModel.PropertyChanged += ViewModel_SettingsPropertyChanged;
            }
            
            // Initial theme application
            UpdateTheme();

            // Setup Tray Icon from embedded app.png (Enlarged & Cropped)
            try
            {
                var resourceStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.png"));
                if (resourceStream != null)
                {
                    using (var stream = resourceStream.Stream)
                    using (var originalBitmap = new System.Drawing.Bitmap(stream))
                    {
                        // Auto-crop transparency to make the icon fill the space better
                        using (var cropped = CropImageTransparency(originalBitmap))
                        {
                            // Calculate aspect ratio to prevent stretching
                            float scale = Math.Min(32f / cropped.Width, 32f / cropped.Height);
                            int newWidth = Math.Max(1, (int)(cropped.Width * scale));
                            int newHeight = Math.Max(1, (int)(cropped.Height * scale));
                            int posX = (32 - newWidth) / 2;
                            int posY = (32 - newHeight) / 2;

                            using (var resizedBitmap = new System.Drawing.Bitmap(32, 32))
                            using (var g = System.Drawing.Graphics.FromImage(resizedBitmap))
                            {
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                                
                                g.DrawImage(cropped, posX, posY, newWidth, newHeight);
                                
                                IntPtr hIcon = resizedBitmap.GetHicon();
                                _keepTrayIconAlive = System.Drawing.Icon.FromHandle(hIcon);
                                TrayIcon.Icon = _keepTrayIconAlive;

                                // Also set the Window (Taskbar) icon to match
                                try
                                {
                                    IntPtr hBitmap = resizedBitmap.GetHbitmap();
                                    this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                        hBitmap, IntPtr.Zero, Int32Rect.Empty, 
                                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch { }
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
                return source.Clone(new System.Drawing.Rectangle(minX, minY, width, height), source.PixelFormat);
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

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

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

            // Calculate window height in DIP
            double baseHeight = DesiredHeight;
            double capsuleRadius = 18;
            double capsuleMarginV = 0;
            switch (_viewModel.BarSize)
            {
                case "Small": baseHeight = 32; capsuleRadius = 10; capsuleMarginV = 0; SetItemSizes(14, 2, 3, 2, 11, 24); break;
                case "Large": baseHeight = 52; capsuleRadius = 18; capsuleMarginV = 0; SetItemSizes(28, 6, 8, 2, 16, 40); break;
                case "Medium": baseHeight = 40; capsuleRadius = 14; capsuleMarginV = 0; SetItemSizes(22, 4, 6, 2, 14, 34); break;
            }
            Height = baseHeight;
            if (CapsuleBorder != null)
            {
                CapsuleBorder.CornerRadius = new System.Windows.CornerRadius(capsuleRadius);
                CapsuleBorder.Margin = new System.Windows.Thickness(0, capsuleMarginV, 0, capsuleMarginV);
            }

            var presentationSource = PresentationSource.FromVisual(this);
            double dpiFactor = presentationSource?.CompositionTarget.TransformToDevice.M22 ?? 1.0;
            int heightPx = (int)(Height * dpiFactor);
            int screenWidthPx = (int)(SystemParameters.PrimaryScreenWidth * dpiFactor);
            int screenHeightPx = (int)(SystemParameters.PrimaryScreenHeight * dpiFactor);

            bool isTop = _viewModel.ShelfPosition == "Top";

            // Step 1: Set up the APPBARDATA and call ABM_QUERYPOS then ABM_SETPOS
            // against the actual screen edge (not work area) so Windows knows we're docking there.
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = new WindowInteropHelper(this).Handle;
            abd.uEdge = isTop ? (int)ABEdge.ABE_TOP : (int)ABEdge.ABE_BOTTOM;

            // Propose the full-width strip at the screen edge
            abd.rc.left = 0;
            abd.rc.right = screenWidthPx;
            if (isTop)
            {
                abd.rc.top = 0;
                abd.rc.bottom = heightPx;
            }
            else
            {
                abd.rc.bottom = screenHeightPx;
                abd.rc.top = screenHeightPx - heightPx;
            }

            // Let OS negotiate position (moves reserve around other appbars)
            SHAppBarMessage((int)ABMsg.ABM_QUERYPOS, ref abd);

            // Enforce our preferred edge size (don't let ABM_QUERYPOS shrink us)
            if (isTop)
            {
                abd.rc.bottom = abd.rc.top + heightPx;
            }
            else
            {
                abd.rc.top = abd.rc.bottom - heightPx;
            }

            SHAppBarMessage((int)ABMsg.ABM_SETPOS, ref abd);
            Log($"AppBar Rect after SETPOS: L={abd.rc.left}, T={abd.rc.top}, R={abd.rc.right}, B={abd.rc.bottom}");

            // Step 2: Position WPF window exactly at the AppBar-reserved rect
            int xPx = abd.rc.left;
            int wPx = abd.rc.right - abd.rc.left;
            int hPx = abd.rc.bottom - abd.rc.top;
            int yPx = abd.rc.top;

            var hwnd = new WindowInteropHelper(this).Handle;
            uint flags = SWP_NOACTIVATE | (_isForcedHiddenByFullScreen ? 0u : SWP_SHOWWINDOW);
            SetWindowPos(hwnd, HWND_TOPMOST, xPx, yPx, wPx, hPx, flags);

            Left = xPx / dpiFactor;
            Top = yPx / dpiFactor;
            Width = wPx / dpiFactor;
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
            // _fullScreenCheckTimer.Start(); // Disabled for debugging
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

                    // Check if foreground window covers the screen
                    bool isFullScreen = (foregroundRect.left <= monitorInfo.rcMonitor.left + 1 &&
                                         foregroundRect.top <= monitorInfo.rcMonitor.top + 1 &&
                                         foregroundRect.right >= monitorInfo.rcMonitor.right - 1 &&
                                         foregroundRect.bottom >= monitorInfo.rcMonitor.bottom - 1);

                    // Also detect fullscreen via window style (no caption, no thick frame)
                    // Catches VLC and other media players that use exclusive fullscreen mode
                    if (!isFullScreen)
                    {
                        int style = GetWindowLong(foregroundWindow, -16); // GWL_STYLE
                        bool hasCaption = (style & 0x00C00000) != 0; // WS_CAPTION
                        bool hasThickFrame = (style & 0x00040000) != 0; // WS_THICKFRAME
                        // Window covers > 95% of screen and has no window chrome = likely fullscreen
                        isFullScreen = !hasCaption && !hasThickFrame &&
                                       winW >= monW * 0.95 && winH >= monH * 0.95;
                    }

                    if (isFullScreen)
                    {
                        if (foregroundWindow != _lastFullScreenWindow)
                        {
                            // New full-screen window — start the timer delay
                            _lastFullScreenWindow = foregroundWindow;
                            _lastFullScreenProcessId = 0;
                            _lastFullScreenTime = DateTime.UtcNow;
                            GetWindowThreadProcessId(foregroundWindow, out _lastFullScreenProcessId);
                            // Don't hide yet — wait to confirm it's not a brief overlay
                            return;
                        }

                        // Same full-screen window still active — only hide after 1.5s
                        if (!_isForcedHiddenByFullScreen &&
                            (DateTime.UtcNow - _lastFullScreenTime).TotalSeconds < 1.5)
                        {
                            return;
                        }

                        if (!_isForcedHiddenByFullScreen)
                        {
                            _isForcedHiddenByFullScreen = true;
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
                                int style = GetWindowLong(foregroundWindow, -16);
                                bool hasCaption = (style & 0x00C00000) != 0;
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
                        if (lparam.ToInt32() != 0)
                        {
                            _isForcedHiddenByFullScreen = true;
                            var fullWnd = GetForegroundWindow();
                            _lastFullScreenWindow = fullWnd;
                            GetWindowThreadProcessId(fullWnd, out _lastFullScreenProcessId);
                            _lastFullScreenTime = DateTime.UtcNow;
                            // Must unregister AppBar so mouse proximity to screen edge
                            // doesn't re-activate the window
                            Visibility = Visibility.Collapsed;
                            UnregisterAppBar();
                        }
                        // Don't show on exit — let the timer handle it
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
            if (_searchBoxBorder != null) return;
            var status = _viewModel.DebugStatus;
            bool active = !string.IsNullOrEmpty(status) && status != "Ready";
            SearchToggleButton.Visibility = active ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }

        private void UpdateTheme()
        {
            if (_viewModel?.SettingsViewModel == null) return;

            var theme = _viewModel.SettingsViewModel.Theme;
            bool isLightTheme = theme == "Light";
            if (theme == "System")
            {
                var registryValue = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
                isLightTheme = registryValue != null && (int)registryValue == 1;
            }

            // Solid colors — no acrylic/blur in either mode
            var shelfBrush = new System.Windows.Media.SolidColorBrush(isLightTheme
                ? System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)
                : System.Windows.Media.Color.FromRgb(0x14, 0x14, 0x14));

            if (RootGrid != null)
                RootGrid.Background = System.Windows.Media.Brushes.Transparent;

            var textColor = new System.Windows.Media.SolidColorBrush(isLightTheme ? System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x22) : System.Windows.Media.Colors.White);
            var iconColor = new System.Windows.Media.SolidColorBrush(isLightTheme ? System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x22) : System.Windows.Media.Colors.White);

            // CardBg: semi-transparent overlay (glass-morphism surface)
            // Light: black 10% over white shelf → subtle elevation
            // Dark:  white 7% over dark shelf → subtle elevation
            var cardBg = new System.Windows.Media.SolidColorBrush(isLightTheme
                ? System.Windows.Media.Color.FromArgb(0x1A, 0x00, 0x00, 0x00)
                : System.Windows.Media.Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
            // ControlBg (hover): more opaque overlay for distinction
            System.Windows.Media.Color hoverColor = isLightTheme
                ? System.Windows.Media.Color.FromArgb(0x33, 0x00, 0x00, 0x00)
                : System.Windows.Media.Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);
            var controlBg = new System.Windows.Media.SolidColorBrush(hoverColor);

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

            void SetResource(string key, object value)
            {
                System.Windows.Application.Current.Resources[key] = value;
                this.Resources[key] = value;
            }

            SetResource("AppBackground", shelfBrush);
            SetResource("TextColor", textColor);
            SetResource("IconColor", iconColor);
            SetResource("CardBg", cardBg);
            SetResource("ControlBg", controlBg);
            SetResource("BorderColor", borderColor);
            SetResource("MenuBg", menuBg);
            SetResource("ToolTipBg", tooltipBg);
            SetResource("AccentColor", accentColor);
            SetResource("AccentColorDim", accentColorDim);
            SetResource("ShadowOpacity", isLightTheme ? 0.2d : 0.45d);
            SetResource("ShadowColor", System.Windows.Media.Colors.Black);

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
                    RootGrid.Background = shelfBrush;

                void SetResource(string key, object value)
                {
                    System.Windows.Application.Current.Resources[key] = value;
                    this.Resources[key] = value;
                }

                SetResource("AppBackground", shelfBrush);
                SetResource("CardBg", new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomCardColor)));
                SetResource("ControlBg", new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomControlBgColor)));
                SetResource("AccentColor", new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomAccentColor)));
                SetResource("WindowBg", new System.Windows.Media.SolidColorBrush(bgColor));
                SetResource("BorderColor", new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomBorderColor)));
                SetResource("TextColor", new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomTextColor)));
                SetResource("IconColor", new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(svm.CustomIconColor)));

                if (ItemsHost != null)
                    ItemsHost.Items.Refresh();
            }
            catch { }
        }

        private void ShowWindow()
        {
            Show();
            // We don't activate to avoid stealing focus, especially with WS_EX_NOACTIVATE
            // RegisterAppBar will handle the position and space reservation
            RegisterAppBar();
        }

        private void HideWindow()
        {
            UnregisterAppBar();
            Hide();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.IsShelfVisible = false;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            this.Activated -= OnWindowActivated;
            UnregisterAppBar();
            // Just hide the window instead of closing to stay in system tray
            e.Cancel = true;
            _viewModel.IsShelfVisible = false;
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

        private void EnsureClipboardListener()
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

                if (Math.Abs(diff.X) > 2 || Math.Abs(diff.Y) > 2)
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
                                var shelfRect = new System.Drawing.Rectangle(
                                    (int)this.Left, (int)this.Top,
                                    (int)Math.Max(this.ActualWidth, 1), (int)Math.Max(this.ActualHeight, 1));
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
                var result = System.Windows.MessageBox.Show(
                    $"A new version v{info.LatestVersion} is available!\n\nCurrent version: v{updateService.GetCurrentVersion()}\n\n{(string.IsNullOrEmpty(info.ReleaseNotes) ? "" : $"Release notes:\n{info.ReleaseNotes}\n\n")}Would you like to download it?",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes && !string.IsNullOrEmpty(info.DownloadUrl))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = info.DownloadUrl,
                        UseShellExecute = true
                    });
                }
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"You are running the latest version (v{updateService.GetCurrentVersion()}).",
                    "No Updates Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
                    this.Activate();
                    this.Focus();
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
        #endregion
    }
}
