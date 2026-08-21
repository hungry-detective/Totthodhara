using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClipDropPro.Models;
using ClipDropPro.Services;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using Microsoft.WindowsAPICodePack.Shell;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Net.Http;

namespace ClipDropPro.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public int type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
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

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        const int INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const ushort VK_CONTROL = 0x11;
        const ushort VK_V = 0x56;

        private void SimulatePaste()
        {
            INPUT[] inputs = new INPUT[4];

            // Ctrl Down
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = VK_CONTROL;

            // V Down
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = VK_V;

            // V Up
            inputs[2].type = INPUT_KEYBOARD;
            inputs[2].u.ki.wVk = VK_V;
            inputs[2].u.ki.dwFlags = KEYEVENTF_KEYUP;

            // Ctrl Up
            inputs[3].type = INPUT_KEYBOARD;
            inputs[3].u.ki.wVk = VK_CONTROL;
            inputs[3].u.ki.dwFlags = KEYEVENTF_KEYUP;

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private readonly IDataService _dataService;
        private readonly IFileStorageService _fileStorageService;
        private readonly ISettingsService _settingsService;
        private readonly IHotkeyService _hotkeyService;
        private readonly IGestureService _gestureService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ISystemMonitorService _systemMonitorService;

        [ObservableProperty]
        private ObservableCollection<ClipboardItem> _clipboardItems = new();
        
        private DateTime _lastInternalChangeTime = DateTime.MinValue;
        public bool IsInternalChange
        {
            get => (DateTime.Now - _lastInternalChangeTime).TotalMilliseconds < 500;
            set { if (value) _lastInternalChangeTime = DateTime.Now; }
        }

        [ObservableProperty]
        private bool _isShelfVisible = false;

        [ObservableProperty]
        private bool _isWindowPinned = false;

        [ObservableProperty]
        private SettingsViewModel _settingsViewModel;

        [ObservableProperty]
        private string _debugStatus = "Ready";

        [ObservableProperty]
        private string _barSize = "Medium"; // Small, Medium, Large

        [ObservableProperty]
        private string _alignment = "Centered"; // Centered, Left, Right

        [ObservableProperty]
        private string _shelfPosition = "Bottom"; // Bottom, Top

        private string _lastCapturedContent = string.Empty;
        private DateTime _lastCaptureTime = DateTime.MinValue;
        private readonly TimeSpan _dedupeWindow = TimeSpan.FromMilliseconds(500);

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                    RefreshSearchFilter();
            }
        }

        [ObservableProperty]
        private bool _isSearchActive = false;

        [ObservableProperty]
        private int _searchResultCount = 0;

        [ObservableProperty]
        private bool _isMultiPasteMode = false;

        [ObservableProperty]
        private bool _showSystemMonitor = true;

        [ObservableProperty]
        private bool _showNetworkMonitor = true;

        [ObservableProperty]
        private bool _showCpuRamMonitor = true;

        [ObservableProperty]
        private bool _showPlugins = true;

        [ObservableProperty]
        private int _cpuUsage = 0;

        [ObservableProperty]
        private int _memoryUsage = 0;

        [ObservableProperty]
        private double _networkUpKBs = 0;

        [ObservableProperty]
        private double _networkDownKBs = 0;

        [ObservableProperty]
        private string _networkDownText = "0 B/s";

        [ObservableProperty]
        private string _networkUpText = "0 B/s";

        private readonly List<ClipboardItem> _selectedItems = new();
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        static MainViewModel()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Totthodhara/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
        }

        public bool HasSelectedItems => _selectedItems.Count > 0;

        public MainViewModel(
            IDataService dataService,
            IFileStorageService fileStorageService,
            ISettingsService settingsService,
            IHotkeyService hotkeyService,
            IGestureService gestureService,
            SettingsViewModel settingsViewModel,
            IServiceProvider serviceProvider,
            ISystemMonitorService systemMonitorService)
        {
            _dataService = dataService;
            _fileStorageService = fileStorageService;
            _settingsService = settingsService;
            _hotkeyService = hotkeyService;
            _gestureService = gestureService;
            _systemMonitorService = systemMonitorService;
            this.SettingsViewModel = settingsViewModel;
            _serviceProvider = serviceProvider;

            // Sync settings to properties
            SyncSettings();

            // Initialize system monitor
            _systemMonitorService.Updated += OnSystemMonitorUpdated;
            ShowSystemMonitor = _settingsService.ShowSystemMonitor;
            if (ShowSystemMonitor)
                _systemMonitorService.Start();

            // React when settings change from SettingsWindow
            settingsViewModel.PropertyChanged += (s, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(SettingsViewModel.BarSize):
                        BarSize = settingsViewModel.BarSize;
                        break;
                    case nameof(SettingsViewModel.ShelfPosition):
                        ShelfPosition = settingsViewModel.ShelfPosition;
                        break;
                    case nameof(SettingsViewModel.Alignment):
                        Alignment = settingsViewModel.Alignment;
                        break;
                    case nameof(SettingsViewModel.ShowSystemMonitor):
                        ShowSystemMonitor = settingsViewModel.ShowSystemMonitor;
                        break;
                    case nameof(SettingsViewModel.ShowNetworkMonitor):
                        ShowNetworkMonitor = settingsViewModel.ShowNetworkMonitor;
                        break;
                    case nameof(SettingsViewModel.ShowCpuRamMonitor):
                        ShowCpuRamMonitor = settingsViewModel.ShowCpuRamMonitor;
                        break;
                }
            };

            InitializeAsync();
            RegisterGlobalHotkey();

            // Gesture service no longer used; kept for potential future gestures
        }

        private async void InitializeAsync()
        {
            Log("InitializeAsync started.");
            try 
            {
                await _dataService.InitializeAsync();
                Log("DataService initialized.");
                await LoadItemsAsync();
                Log("LoadItemsAsync completed.");
                
                // Background cleanup task
                var items = await _dataService.GetItemsAsync();
                var pinnedPaths = items.Where(x => x.IsPinned).Select(x => x.FilePath).ToList();
                await Task.Run(async () => {
                    Log($"Starting CleanOldFilesAsync for {pinnedPaths.Count} pinned files...");
                    await _fileStorageService.CleanOldFilesAsync(_settingsService.AutoCleanHours, pinnedPaths);
                    Log("Background cleanup task completed.");

                    // Delete DB items whose files no longer exist on disk
                    var allItems = _dataService.GetItemsAsync().Result;
                    foreach (var dbItem in allItems.Where(x => x.IsFile && !string.IsNullOrEmpty(x.FilePath) && !System.IO.File.Exists(x.FilePath)))
                    {
                        Log($"Removing orphaned DB entry: {dbItem.Id} ({dbItem.FilePath})");
                        _dataService.DeleteItemAsync(dbItem).Wait();
                    }
                });
                // Trim history to configured limit on startup
                await TrimHistoryAsync();
                Log("TrimHistoryAsync completed on startup.");

                Log("InitializeAsync logic finished.");
            }
            catch (Exception ex)
            {
                Log($"FATAL in InitializeAsync: {ex.Message}");
            }
        }

        private void RegisterGlobalHotkey()
        {
            _hotkeyService.RegisterHotkey(_settingsService.HotkeyString, _settingsService.HotkeyModifiers, () => 
            {
                IsShelfVisible = !IsShelfVisible;
            });
        }

        public async Task LoadItemsAsync(bool forceReload = false)
        {
            Log("LoadItemsAsync started.");
            int maxItems = Math.Max(_settingsService.MaxHistoryItems, 10);
            int fetchLimit = Math.Max(maxItems, 500);
            var items = await _dataService.GetItemsAsync(fetchLimit);

            // Sort: snippets first, then pinned, then by date added descending
            items = items.OrderByDescending(x => x.IsSnippet)
                         .ThenByDescending(x => x.IsPinned)
                         .ThenByDescending(x => x.DateAdded)
                         .Take(maxItems)
                         .ToList();

            Log($"Found {items.Count} items.");

            // Mark new items with flash animation
            int? prevTopId = ClipboardItems.Count > 0 ? ClipboardItems[0].Id : null;

            // Mark first non-pinned/non-snippet item for separator (only if pinned items exist)
            bool separatorMarked = false;
            bool hasPinnedOrSnippet = false;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                item.Index = i + 1;
                item.IsFirstUnpinned = false;
                PopulateTextMetadata(item);
                PopulateIcon(item);
                if (item.IsSnippet || item.IsPinned)
                    hasPinnedOrSnippet = true;
                if (!separatorMarked && hasPinnedOrSnippet && !item.IsSnippet && !item.IsPinned)
                {
                    item.IsFirstUnpinned = true;
                    separatorMarked = true;
                }
                // New item flash: if this is the new top item (or added in a batch)
                if (i == 0 && prevTopId.HasValue && item.Id != prevTopId.Value)
                    item.IsNew = true;
            }

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!forceReload && ClipboardItems.Count == items.Count)
                {
                    bool changed = false;
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (ClipboardItems[i].Id != items[i].Id ||
                            ClipboardItems[i].DateAdded != items[i].DateAdded)
                        {
                            changed = true;
                            break;
                        }
                    }
                    if (!changed) return;
                }

                ClipboardItems.Clear();
                foreach (var item in items)
                    ClipboardItems.Add(item);
                Log("ClipboardItems updated on UI thread.");
                SetupSearchFilter();
            });

            // Load thumbnails asynchronously after items display
            _ = LoadThumbnailsAsync(items);
            _ = LoadUrlFaviconsAsync(items);
        }

        private async Task LoadThumbnailsAsync(List<ClipboardItem> items)
        {
            foreach (var item in items.Where(x => x.IsImage || x.IsVideo))
            {
                try
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (item.IsImage) PopulateImageMetadata(item);
                        else if (item.IsVideo) PopulateVideoMetadata(item);
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    Log($"Thumbnail error {item.Id}: {ex.Message}");
                }
            }
        }

        private async Task LoadUrlFaviconsAsync(List<ClipboardItem> items)
        {
            string favDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TotthodharaFavicons");
            try
            {
                // Clear old favicon cache to force fresh fetches
                if (System.IO.Directory.Exists(favDir))
                    System.IO.Directory.Delete(favDir, true);
                System.IO.Directory.CreateDirectory(favDir);
            }
            catch { }

            foreach (var item in items.Where(x => x.IsUrl))
            {
                try
                {
                    if (Uri.TryCreate(item.TextContent, UriKind.Absolute, out Uri uriResult))
                    {
                        byte[] bytes = null;
                        string host = uriResult.Host.ToLower();
                        string fullUrl = item.TextContent.ToLower();

                        // Special handling for known services with specific icons
                        string knownIconUrl = GetKnownFaviconUrl(host, fullUrl);
                        if (knownIconUrl != null)
                        {
                            try { bytes = await _httpClient.GetByteArrayAsync(knownIconUrl); } catch { }
                        }

                        // Fallback: Google S2 favicon service
                        if (bytes == null || bytes.Length <= 1)
                        {
                            try
                            {
                                bytes = await _httpClient.GetByteArrayAsync($"https://www.google.com/s2/favicons?domain={uriResult.Host}&sz=32");
                            }
                            catch { }
                        }

                        // Fallback: try direct favicon.ico from the domain
                        if (bytes == null || bytes.Length <= 1)
                        {
                            try
                            {
                                bytes = await _httpClient.GetByteArrayAsync($"{uriResult.Scheme}://{uriResult.Host}/favicon.ico");
                            }
                            catch { }
                        }

                        if (bytes != null && bytes.Length > 1)
                        {
                            string safeName = System.Text.RegularExpressions.Regex.Replace(host, "[^a-zA-Z0-9]", "_");
                            string tempFile = System.IO.Path.Combine(favDir, $"{safeName}.png");
                            await System.IO.File.WriteAllBytesAsync(tempFile, bytes);
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                try
                                {
                                    var bitmap = new BitmapImage();
                                    bitmap.BeginInit();
                                    bitmap.UriSource = new Uri(tempFile);
                                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                    bitmap.EndInit();
                                    bitmap.Freeze();
                                    item.IconSource = bitmap;
                                }
                                catch { }
                            }, System.Windows.Threading.DispatcherPriority.Normal);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Favicon error {item.Id}: {ex.Message}");
                }
            }
        }

        private string GetKnownFaviconUrl(string host, string fullUrl)
        {
            // Google Forms (forms.gle short links) → use docs.google.com for favicon
            if (host == "forms.gle" || host.EndsWith(".forms.gle"))
                return "https://www.google.com/s2/favicons?domain=docs.google.com&sz=32";

            // YouTube
            if (host.EndsWith("youtube.com") || host == "youtu.be")
                return "https://www.google.com/s2/favicons?domain=youtube.com&sz=32";

            // GitHub
            if (host == "github.com" || host.EndsWith(".github.com"))
                return "https://www.google.com/s2/favicons?domain=github.com&sz=32";

            // Twitter / X
            if (host == "twitter.com" || host == "x.com")
                return "https://www.google.com/s2/favicons?domain=x.com&sz=32";

            // Reddit
            if (host == "reddit.com" || host.EndsWith(".reddit.com"))
                return "https://www.google.com/s2/favicons?domain=reddit.com&sz=32";

            // Discord
            if (host == "discord.com" || host == "discord.gg" || host.EndsWith(".discord.com"))
                return "https://www.google.com/s2/favicons?domain=discord.com&sz=32";

            // Stack Overflow
            if (host == "stackoverflow.com" || host.EndsWith(".stackoverflow.com"))
                return "https://www.google.com/s2/favicons?domain=stackoverflow.com&sz=32";

            return null;
        }

        private void PopulateIcon(ClipboardItem item)
        {
            if (item.IsUrl)
            {
                item.IconGlyph = "🌐";
                return;
            }

            if (item.IsFile)
            {
                string ext = Path.GetExtension(item.FilePath).ToLower();
                if (item.IsVideo) item.IconGlyph = "🎞️";
                else if (item.IsImage || ext == ".ico") item.IconGlyph = "🖼️";
                else if (ext == ".pdf") item.IconGlyph = "📄";
                else if (ext == ".zip" || ext == ".rar" || ext == ".7z" || ext == ".tar" || ext == ".gz") item.IconGlyph = "📦";
                else if (ext == ".mp3" || ext == ".wav" || ext == ".wma" || ext == ".m4a" || ext == ".ogg" || ext == ".flac" || ext == ".aac") item.IconGlyph = "🎵";
                else item.IconGlyph = "📝";
            }
            else
            {
                item.IconGlyph = string.Empty;
            }
        }

        private void PopulateImageMetadata(ClipboardItem item)
        {
            if (!item.IsImage || string.IsNullOrEmpty(item.FilePath) || !System.IO.File.Exists(item.FilePath))
                return;

            try
            {
                // Create a thumbnail to keep memory footprint low
                var thumb = new System.Windows.Media.Imaging.BitmapImage();
                thumb.BeginInit();
                thumb.UriSource = new Uri(item.FilePath);
                thumb.DecodePixelHeight = 200; 
                thumb.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                thumb.EndInit();
                thumb.Freeze();
                item.ThumbnailSource = thumb;

                // Read exact pixel dimensions without fully decoding the massive image into memory
                using (var stream = System.IO.File.OpenRead(item.FilePath))
                {
                    var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, System.Windows.Media.Imaging.BitmapCacheOption.None);
                    if (decoder.Frames.Count > 0)
                    {
                        item.ResolutionText = $"{decoder.Frames[0].PixelWidth} x {decoder.Frames[0].PixelHeight}";
                    }
                }
            }
            catch (Exception ex) { Log($"DEBUG: PopulateImageMetadata Exception: {ex.Message}"); }
        }

        private void PopulateVideoMetadata(ClipboardItem item)
        {
            if (!item.IsVideo || string.IsNullOrEmpty(item.FilePath) || !System.IO.File.Exists(item.FilePath))
                return;

            try
            {
                using (var shellFile = ShellFile.FromFilePath(item.FilePath))
                {
                    // Use ExtraLarge (256x256) for high quality previews
                    var bmp = shellFile.Thumbnail.ExtraLargeBitmap;
                    if (bmp != null)
                    {
                        IntPtr hBitmap = bmp.GetHbitmap();
                        try 
                        {
                            var thumb = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                hBitmap,
                                IntPtr.Zero,
                                System.Windows.Int32Rect.Empty,
                                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                                
                            thumb.Freeze();
                            item.ThumbnailSource = thumb;
                            item.ResolutionText = "Video";
                        }
                        finally
                        {
                            MainWindow.DeleteObject(hBitmap);
                        }
                    }
                }
            }
            catch (Exception ex) { Log($"DEBUG: PopulateVideoMetadata Exception: {ex.Message}"); }
        }

        private void PopulateTextMetadata(ClipboardItem item)
        {
            if (item == null || item.IsFile || string.IsNullOrEmpty(item.TextContent))
                return;

            // Check if it's a valid URL
            if (Uri.TryCreate(item.TextContent, UriKind.Absolute, out Uri uriResult) && 
                (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
            {
                item.IsUrl = true;
                // Extract Domain for clean display
                item.DisplayTitle = uriResult.Host;
            }
            else
            {
                // Smart 4-word truncation for standard text items
                var words = item.TextContent.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 4)
                {
                    item.DisplayTitle = string.Join(" ", words.Take(4)) + "...";
                }
                else
                {
                    item.DisplayTitle = item.TextContent;
                }
            }
        }

        public string TrayIconAction => _settingsService.TrayIconAction;

        private void SyncSettings()
        {
            BarSize = _settingsService.BarSize;
            Alignment = _settingsService.Alignment;
            ShelfPosition = _settingsService.ShelfPosition;
            ShowSystemMonitor = _settingsService.ShowSystemMonitor;
            ShowNetworkMonitor = _settingsService.ShowNetworkMonitor;
            ShowCpuRamMonitor = _settingsService.ShowCpuRamMonitor;
            ShowPlugins = _settingsService.ShowPlugins;
        }

        private void OnSystemMonitorUpdated()
        {
            CpuUsage = _systemMonitorService.CpuUsage;
            MemoryUsage = _systemMonitorService.MemoryUsage;
            NetworkUpKBs = _systemMonitorService.NetworkUpKBs;
            NetworkDownKBs = _systemMonitorService.NetworkDownKBs;
            NetworkDownText = FormatSpeed(NetworkDownKBs);
            NetworkUpText = FormatSpeed(NetworkUpKBs);
        }

        private static string FormatSpeed(double kbPerSec)
        {
            if (kbPerSec >= 1024)
                return $"{kbPerSec / 1024.0,4:F1} MB/s";
            return $"{kbPerSec,4:F1} KB/s";
        }

        partial void OnShowSystemMonitorChanged(bool value)
        {
            _settingsService.ShowSystemMonitor = value;
            if (value)
                _systemMonitorService.Start();
            else
                _systemMonitorService.Stop();
        }

        partial void OnShowPluginsChanged(bool value)
        {
            _settingsService.ShowPlugins = value;
        }

        partial void OnBarSizeChanged(string value)
        {
            _settingsService.BarSize = value;
            _settingsService.Save();
        }

        partial void OnAlignmentChanged(string value)
        {
            _settingsService.Alignment = value;
            _settingsService.Save();
        }

        partial void OnShelfPositionChanged(string value)
        {
            _settingsService.ShelfPosition = value;
            _settingsService.Save();
        }

        [RelayCommand]
        private void ToggleShelf()
        {
            IsShelfVisible = !IsShelfVisible;
        }

        private Views.SettingsWindow _settingsWindow;

        [RelayCommand]
        private void OpenSettings()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                try 
                {
                    Log("OpenSettings triggered.");

                    // Reuse existing settings window if already open
                    if (_settingsWindow != null && _settingsWindow.IsVisible)
                    {
                        _settingsWindow.Activate();
                        _settingsWindow.Focus();
                        Log("Settings window already open, brought to front.");
                        return;
                    }

                    _settingsWindow = new Views.SettingsWindow(SettingsViewModel, _settingsService);
                    _settingsWindow.Closed += (s, args) => _settingsWindow = null;
                    
                    var activeWindow = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? System.Windows.Application.Current.MainWindow;
                    if (activeWindow != null && activeWindow != _settingsWindow)
                    {
                        _settingsWindow.Owner = activeWindow;
                        _settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    }

                    Log("Calling Show()...");
                    _settingsWindow.Show(); 
                    _settingsWindow.Activate();
                    _settingsWindow.Focus();
                    Log("Show returned.");
                }
                catch (Exception ex)
                {
                    Log($"EXCEPTION in OpenSettings: {ex.Message}\n{ex.StackTrace}");
                    System.Windows.MessageBox.Show($"Could not open settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        [RelayCommand]
        private async Task ItemClicked(ClipboardItem item)
        {
            if (item == null) return;
            Log($"ItemClicked: STRICT COPY - {item.DisplayText}");

            // Multi-paste mode: toggle selection instead of copy/paste
            if (IsMultiPasteMode)
            {
                item.IsSelected = !item.IsSelected;
                if (item.IsSelected)
                    _selectedItems.Add(item);
                else
                    _selectedItems.Remove(item);
                NotifySelectedItemsChanged();
                DebugStatus = item.IsSelected ? "Selected" : "Deselected";
                _ = Task.Delay(800).ContinueWith(_ => DebugStatus = "Ready");
                return;
            }

            if (!_settingsService.CopyItemsToDestination)
            {
                Log("CopyItemsToDestination is disabled. Skipping auto-copy/paste.");
                return;
            }

            try
            {
                IsInternalChange = true;
                if (item.IsFile && !string.IsNullOrEmpty(item.FilePath))
                {
                    Log("Copying file to clipboard...");
                    // For images, set BOTH bitmap + file drop on clipboard in one operation
                    // so image editors see CF_DIB/CF_BITMAP, and file explorer/desktop see CF_HDROP
                    if (item.IsImage && System.IO.File.Exists(item.FilePath))
                    {
                        try
                        {
                            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TotthodharaDrag");
                            try { if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
                            System.IO.Directory.CreateDirectory(tempDir);
                            var fileName = !string.IsNullOrEmpty(item.FileName) ? item.FileName : System.IO.Path.GetFileName(item.FilePath);
                            var sourceExt = System.IO.Path.GetExtension(item.FilePath);
                            if (!string.IsNullOrEmpty(sourceExt) && !fileName.EndsWith(sourceExt, System.StringComparison.OrdinalIgnoreCase))
                                fileName += sourceExt;
                            var tempFilePath = System.IO.Path.Combine(tempDir, fileName);
                            System.IO.File.Copy(item.FilePath, tempFilePath, overwrite: true);

                            var winFormData = new System.Windows.Forms.DataObject();
                            winFormData.SetFileDropList(new System.Collections.Specialized.StringCollection { tempFilePath });
                            using (var bmp = new System.Drawing.Bitmap(item.FilePath))
                            {
                                winFormData.SetImage(bmp);
                            }
                            System.Windows.Forms.Clipboard.SetDataObject(winFormData, true);
                            DebugStatus = "Image + File Copied!";
                        }
                        catch (Exception iex)
                        {
                            Log($"Image+file copy failed: {iex.Message}");
                            await CopyAsFileDrop(item);
                        }
                    }
                    else
                    {
                        await CopyAsFileDrop(item);
                    }
                }
                else
                {
                    Log("Copying text to clipboard...");
                    System.Windows.Clipboard.SetText(item.TextContent ?? item.DisplayText);
                    DebugStatus = "Copied!";
                }

                // Auto-paste logic
                await Task.Delay(100);
                Log("Simulating Paste...");
                SendKeys.SendWait("^v");

                _ = Task.Delay(1000).ContinueWith(_ => DebugStatus = "Ready");
            }
            catch (Exception ex)
            {
                Log($"Error copying item: {ex.Message}");
            }
        }

        private async Task CopyAsFileDrop(ClipboardItem item)
        {
            try
            {
                var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TotthodharaDrag");
                try { if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
                System.IO.Directory.CreateDirectory(tempDir);
                var fileName = !string.IsNullOrEmpty(item.FileName) ? item.FileName : System.IO.Path.GetFileName(item.FilePath);
                var sourceExt = System.IO.Path.GetExtension(item.FilePath);
                if (!string.IsNullOrEmpty(sourceExt) && !fileName.EndsWith(sourceExt, System.StringComparison.OrdinalIgnoreCase))
                    fileName += sourceExt;
                var tempFilePath = System.IO.Path.Combine(tempDir, fileName);
                System.IO.File.Copy(item.FilePath, tempFilePath, overwrite: true);
                var fileCollection = new System.Collections.Specialized.StringCollection { tempFilePath };
                System.Windows.Clipboard.SetFileDropList(fileCollection);
                DebugStatus = "File Copied!";
            }
            catch (Exception ex)
            {
                Log($"CopyAsFileDrop error: {ex.Message}");
            }
        }

        [RelayCommand]
        private void OpenUrl(ClipboardItem item)
        {
            if (item == null || !item.IsUrl) return;
            string url = item.TextContent;
            if (string.IsNullOrEmpty(url)) return;

            try
            {
                Log($"OpenUrl: {url}");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log($"Error opening URL: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ToggleWindowPin()
        {
            IsWindowPinned = !IsWindowPinned;
            DebugStatus = IsWindowPinned ? "PINNED" : "UNPINNED";
            Task.Delay(1000).ContinueWith(_ => DebugStatus = "Ready");
        }

        [RelayCommand]
        private async Task ClearAllItems()
        {
            Log("ClearAllItems started. Cleaning physical files first...");
            try
            {
                var items = await _dataService.GetItemsAsync();
                var itemsToDelete = items.Where(x => !x.IsPinned && !x.IsSnippet).ToList();

                // Animate items fading out before deletion
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var item in itemsToDelete)
                    {
                        var inCollection = ClipboardItems.FirstOrDefault(x => x.Id == item.Id);
                        if (inCollection != null)
                            inCollection.IsRemoving = true;
                    }
                });
                await Task.Delay(400);

                // Delete all unpinned files in parallel for speed
                var deleteTasks = itemsToDelete
                    .Where(x => x.IsFile && !string.IsNullOrEmpty(x.FilePath))
                    .Select(x => _fileStorageService.DeleteFileAsync(x.FilePath));
                await Task.WhenAll(deleteTasks);

                // Clean up temp drag files from %TEMP%\TotthodharaDrag
                try
                {
                    var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TotthodharaDrag");
                    if (System.IO.Directory.Exists(tempDir))
                        System.IO.Directory.Delete(tempDir, recursive: true);
                }
                catch { }

                await _dataService.DeleteAllExceptPinnedAsync();
                await LoadItemsAsync();
                DebugStatus = "History Cleared!";
                _ = Task.Delay(1000).ContinueWith(_ => DebugStatus = "Ready");
            }
            catch (Exception ex)
            {
                Log($"Error in ClearAllItems: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task DeleteItem(ClipboardItem item)
        {
            if (item == null) return;
            Log($"Attempting to delete item: {item.Id} - {item.FileName}");
            
            try 
            {
                // Trigger removal animation
                item.IsRemoving = true;
                await Task.Delay(300); // Match storyboard duration

                if (item.IsFile && !string.IsNullOrEmpty(item.FilePath))
                {
                    await _fileStorageService.DeleteFileAsync(item.FilePath);
                    // Clean up temp drag copy if exists
                    try
                    {
                        var fileName = !string.IsNullOrEmpty(item.FileName) ? item.FileName : System.IO.Path.GetFileName(item.FilePath);
                        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TotthodharaDrag", fileName);
                        if (System.IO.File.Exists(tempPath))
                            System.IO.File.Delete(tempPath);
                    }
                    catch { }
                }
                await _dataService.DeleteItemAsync(item);
                
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    bool removed = ClipboardItems.Remove(item);
                    if (!removed)
                    {
                        // Fallback: finding by ID if reference is different
                        var toRemove = ClipboardItems.FirstOrDefault(x => x.Id == item.Id);
                        if (toRemove != null)
                        {
                            ClipboardItems.Remove(toRemove);
                            Log("Removed item by ID fallback.");
                        }
                    }
                    else
                    {
                        Log("Removed item by reference.");
                    }

                    // Re-index remaining items
                    for (int i = 0; i < ClipboardItems.Count; i++)
                    {
                        ClipboardItems[i].Index = i + 1;
                    }
                    Log("Items re-indexed after removal.");
                    DebugStatus = "Deleted!";
                    _ = Task.Delay(1000).ContinueWith(_ => DebugStatus = "Ready");
                });
            }
            catch (Exception ex)
            {
                Log($"Error deleting item: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task TogglePin(ClipboardItem item)
        {
            if (item == null) return;
            item.IsPinned = !item.IsPinned;
            await _dataService.UpdateItemAsync(item);
            await LoadItemsAsync(forceReload: true);
        }

        [RelayCommand]
        private async Task ToggleSnippet(ClipboardItem item)
        {
            if (item == null) return;
            item.IsSnippet = !item.IsSnippet;
            await _dataService.UpdateItemAsync(item);
            await LoadItemsAsync(forceReload: true);
        }

        partial void OnIsMultiPasteModeChanged(bool value)
        {
            if (!value)
            {
                foreach (var item in _selectedItems)
                    item.IsSelected = false;
                _selectedItems.Clear();
                OnPropertyChanged(nameof(HasSelectedItems));
            }
        }

        private void SetupSearchFilter()
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(ClipboardItems);
            if (view != null)
            {
                view.Filter = (obj) =>
                {
                    if (string.IsNullOrWhiteSpace(_searchText)) return true;
                    if (obj is ClipboardItem item)
                    {
                        string q = _searchText.Trim().ToLower();
                        if (!string.IsNullOrEmpty(item.TextContent) && item.TextContent.ToLower().Contains(q)) return true;
                        if (!string.IsNullOrEmpty(item.FileName) && item.FileName.ToLower().Contains(q)) return true;
                        if (!string.IsNullOrEmpty(item.DisplayTitle) && item.DisplayTitle.ToLower().Contains(q)) return true;
                        if (!string.IsNullOrEmpty(item.DisplayText) && item.DisplayText.ToLower().Contains(q)) return true;
                    }
                    return false;
                };
            }
        }

        private void RefreshSearchFilter()
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(ClipboardItems);
            if (view != null)
            {
                view.Refresh();
                int count = 0;
                foreach (var obj in ClipboardItems)
                {
                    if (view.Filter == null || view.Filter(obj))
                        count++;
                }
                SearchResultCount = count;
            }
        }

        [RelayCommand]
        private void ToggleMultiPasteMode()
        {
            IsMultiPasteMode = !IsMultiPasteMode;
        }

        [RelayCommand]
        private async Task PasteAll()
        {
            if (_selectedItems.Count == 0) return;

            foreach (var item in _selectedItems.ToList())
            {
                try
                {
                    IsInternalChange = true;
                    if (item.IsFile && !string.IsNullOrEmpty(item.FilePath))
                    {
                        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TotthodharaDrag");
                        try { if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
                        System.IO.Directory.CreateDirectory(tempDir);
                        var fileName = !string.IsNullOrEmpty(item.FileName) ? item.FileName : System.IO.Path.GetFileName(item.FilePath);
                        var sourceExt = System.IO.Path.GetExtension(item.FilePath);
                        if (!string.IsNullOrEmpty(sourceExt) && !fileName.EndsWith(sourceExt, System.StringComparison.OrdinalIgnoreCase))
                            fileName += sourceExt;
                        var tempFilePath = System.IO.Path.Combine(tempDir, fileName);
                        System.IO.File.Copy(item.FilePath, tempFilePath, overwrite: true);

                        if (item.IsImage)
                        {
                            var winFormData = new System.Windows.Forms.DataObject();
                            winFormData.SetFileDropList(new System.Collections.Specialized.StringCollection { tempFilePath });
                            using (var bmp = new System.Drawing.Bitmap(item.FilePath))
                            {
                                winFormData.SetImage(bmp);
                            }
                            System.Windows.Forms.Clipboard.SetDataObject(winFormData, true);
                        }
                        else
                        {
                            var fileCollection = new System.Collections.Specialized.StringCollection { tempFilePath };
                            System.Windows.Clipboard.SetFileDropList(fileCollection);
                        }
                    }
                    else
                    {
                        System.Windows.Clipboard.SetText(item.TextContent ?? item.DisplayText);
                    }
                    await Task.Delay(100);
                    SendKeys.SendWait("^v");
                    await Task.Delay(150);
                }
                catch (Exception ex)
                {
                    Log($"PasteAll error: {ex.Message}");
                }
            }

            foreach (var item in _selectedItems)
                item.IsSelected = false;
            _selectedItems.Clear();
            OnPropertyChanged(nameof(HasSelectedItems));
            IsMultiPasteMode = false;
            DebugStatus = "Pasted All!";
            _ = Task.Delay(1000).ContinueWith(_ => DebugStatus = "Ready");
        }

        private void NotifySelectedItemsChanged()
        {
            OnPropertyChanged(nameof(HasSelectedItems));
        }

        private async Task TrimHistoryAsync()
        {
            try
            {
                int maxItems = _settingsService.MaxHistoryItems;
                var allItems = await _dataService.GetAllItemsAsync();
                var unpinned = allItems.Where(x => !x.IsPinned && !x.IsSnippet)
                                       .OrderByDescending(x => x.DateAdded)
                                       .Skip(maxItems)
                                       .ToList();
                foreach (var item in unpinned)
                {
                    if (item.IsFile && !string.IsNullOrEmpty(item.FilePath) && System.IO.File.Exists(item.FilePath))
                    {
                        try { System.IO.File.Delete(item.FilePath); } catch { }
                    }
                    await _dataService.DeleteItemAsync(item);
                }
                if (unpinned.Count > 0)
                    Log($"Trimmed {unpinned.Count} old items.");
            }
            catch (Exception ex)
            {
                Log($"TrimHistory error: {ex.Message}");
            }
        }

        public async Task HandleDroppedFilesAsync(string[] files)
        {
            int maxSizeMB = _settingsService.MaxFileSizeMB;
            foreach (var file in files)
            {
                // Skip files larger than configured limit
                if (maxSizeMB > 0)
                {
                    try
                    {
                        var fileInfo = new System.IO.FileInfo(file);
                        if (fileInfo.Exists && fileInfo.Length > maxSizeMB * 1024L * 1024L)
                        {
                            Log($"Skipping large dropped file ({fileInfo.Length / 1024 / 1024}MB > {maxSizeMB}MB): {file}");
                            continue;
                        }
                    }
                    catch { }
                }

                // Basic deduplication: Check if this file was recently added as the top item
                if (ClipboardItems.Count > 0 && ClipboardItems[0].IsFile && 
                    ClipboardItems[0].FilePath.Equals(file, StringComparison.OrdinalIgnoreCase) && 
                    (DateTime.Now - ClipboardItems[0].DateAdded).TotalSeconds < 2)
                {
                    Log($"Skipping duplicate file: {file}");
                    continue;
                }

                var savedPath = await _fileStorageService.SaveFileAsync(file);
                var item = new ClipboardItem
                {
                    FileName = System.IO.Path.GetFileName(file),
                    FilePath = savedPath,
                    IsFile = true,
                    DateAdded = DateTime.Now,
                    Origin = "DragDrop"
                };
                await _dataService.AddItemAsync(item);
                Console.WriteLine($"Added File Item: {item.FileName} at {item.FilePath}");
            }
            await TrimHistoryAsync();
            await LoadItemsAsync();
            IsShelfVisible = true;
        }
        
        public async Task HandleDroppedTextAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Deduplication
            if (text == _lastCapturedContent && (DateTime.Now - _lastCaptureTime) < _dedupeWindow)
                return;
            _lastCapturedContent = text;
            _lastCaptureTime = DateTime.Now;

            // Check if it's an image URL
            bool isImageUrl = false;
            try
            {
                if (Uri.TryCreate(text, UriKind.Absolute, out Uri uri))
                {
                    var ext = System.IO.Path.GetExtension(uri.AbsolutePath).ToLower();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp")
                    {
                        var downloadedPath = await _fileStorageService.DownloadImageAsync(text);
                        var imageItem = new ClipboardItem
                        {
                            FileName = System.IO.Path.GetFileName(downloadedPath),
                            FilePath = downloadedPath,
                            IsFile = true,
                            DateAdded = DateTime.Now,
                            Origin = "WebDragDrop"
                        };
                        await _dataService.AddItemAsync(imageItem);
                        isImageUrl = true;
                    }
                }
            }
            catch { }

            if (!isImageUrl)
            {
                var item = new ClipboardItem
                {
                    TextContent = text,
                    IsFile = false,
                    DateAdded = DateTime.Now,
                    Origin = "DragDrop"
                };
                await _dataService.AddItemAsync(item);
                Console.WriteLine($"Added Text Item: {item.TextContent.Substring(0, Math.Min(item.TextContent.Length, 20))}...");
            }

            await TrimHistoryAsync();
            await LoadItemsAsync();
            IsShelfVisible = true;
        }

        private int _clipboardGuard;
        public async void ProcessClipboardChange()
        {
            if (Interlocked.Exchange(ref _clipboardGuard, 1) != 0)
            {
                Log("Clipboard: already processing, skipping.");
                return;
            }
            try
            {
                if (IsInternalChange) return;

                for (int retry = 0; retry < 5; retry++)
                {
                    try
                    {
                        if (System.Windows.Clipboard.ContainsImage())
                        {
                            var bitmap = System.Windows.Clipboard.GetImage();
                            if (bitmap == null) { await Task.Delay(30); continue; }

                            if ((DateTime.Now - _lastCaptureTime) < _dedupeWindow && _lastCapturedContent == "IMAGE_BITMAP")
                            {
                                Log("Skipping duplicate image (too soon).");
                                return;
                            }

                            var savedPath = await _fileStorageService.SaveBitmapAsync(bitmap);

                            if (ClipboardItems.Count > 0 && ClipboardItems[0].IsImage && (DateTime.Now - ClipboardItems[0].DateAdded).TotalSeconds < 2)
                            {
                                Log("Skipping duplicate image (top item is already a recent image).");
                                return;
                            }

                            var item = new ClipboardItem
                            {
                                FileName = "Captured Image",
                                FilePath = savedPath,
                                IsFile = true,
                                DateAdded = DateTime.Now,
                                Origin = "Clipboard"
                            };

                            _lastCapturedContent = "IMAGE_BITMAP";
                            _lastCaptureTime = DateTime.Now;

                            await _dataService.AddItemAsync(item);
                            await TrimHistoryAsync();
                            await LoadItemsAsync();
                            break;
                        }
                        else if (System.Windows.Clipboard.ContainsFileDropList())
                        {
                            var files = System.Windows.Clipboard.GetFileDropList();
                            if (files.Count == 0) { await Task.Delay(30); continue; }

                            string firstFile = files[0];
                            if (string.IsNullOrEmpty(firstFile)) { await Task.Delay(30); continue; }

                            if (firstFile == _lastCapturedContent && (DateTime.Now - _lastCaptureTime) < _dedupeWindow)
                            {
                                Log($"Skipping duplicate file drop (time): {firstFile}");
                                return;
                            }

                            if (ClipboardItems.Count > 0 && ClipboardItems[0].IsFile &&
                                ClipboardItems[0].FilePath.Equals(firstFile, StringComparison.OrdinalIgnoreCase) &&
                                (DateTime.Now - ClipboardItems[0].DateAdded).TotalSeconds < 2)
                            {
                                Log($"Skipping duplicate file drop (content): {firstFile}");
                                return;
                            }

                            int maxSizeMB = _settingsService.MaxFileSizeMB;
                            if (maxSizeMB > 0)
                            {
                                try
                                {
                                    var fileInfo = new System.IO.FileInfo(firstFile);
                                    if (fileInfo.Exists && fileInfo.Length > maxSizeMB * 1024L * 1024L)
                                    {
                                        Log($"Skipping large file ({fileInfo.Length / 1024 / 1024}MB > {maxSizeMB}MB): {firstFile}");
                                        DebugStatus = $"File too large ({maxSizeMB}MB max)";
                                        _ = Task.Delay(2000).ContinueWith(_ => DebugStatus = "Ready");
                                        return;
                                    }
                                }
                                catch { }
                            }

                            string[] fileArray = new string[files.Count];
                            files.CopyTo(fileArray, 0);

                            _lastCapturedContent = firstFile;
                            _lastCaptureTime = DateTime.Now;

                            await HandleDroppedFilesAsync(fileArray);
                            break;
                        }
                        else if (System.Windows.Clipboard.ContainsText())
                        {
                            string text = System.Windows.Clipboard.GetText();
                            if (string.IsNullOrEmpty(text)) { await Task.Delay(30); continue; }

                            if (text == _lastCapturedContent && (DateTime.Now - _lastCaptureTime) < _dedupeWindow)
                            {
                                Log("Skipping duplicate text (time).");
                                return;
                            }

                            if (ClipboardItems.Count > 0 && !ClipboardItems[0].IsFile && ClipboardItems[0].TextContent == text)
                            {
                                Log("Skipping duplicate text (content matches top).");
                                return;
                            }

                            var item = new ClipboardItem
                            {
                                TextContent = text,
                                IsFile = false,
                                DateAdded = DateTime.Now,
                                Origin = "Clipboard"
                            };

                            _lastCapturedContent = text;
                            _lastCaptureTime = DateTime.Now;

                            await _dataService.AddItemAsync(item);
                            await TrimHistoryAsync();
                            await LoadItemsAsync();
                            break;
                        }
                        await Task.Delay(30);
                    }
                    catch (System.Runtime.InteropServices.COMException) { await Task.Delay(50); }
                    catch (Exception ex) { Log($"Clipboard error: {ex.Message}"); break; }
                }
            }
            finally
            {
                _clipboardGuard = 0;
            }
        }
        private static void Log(string msg) => Services.Logger.Write($"[VM] {msg}");
    }
}
