using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClipDropPro.Services;

namespace ClipDropPro.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private readonly IHotkeyService _hotkeyService;
        private readonly IStartupService _startupService;

        public SettingsViewModel(ISettingsService settingsService, IHotkeyService hotkeyService, IStartupService startupService)
        {
            _settingsService = settingsService;
            _hotkeyService = hotkeyService;
            _startupService = startupService;
            
            // Sync initial state
            _startWithWindows = _settingsService.StartWithWindows;
            _customCardColor = _settingsService.CustomCardColor;
            _customControlBgColor = _settingsService.CustomControlBgColor;
            _customAccentColor = _settingsService.CustomAccentColor;
            _customWindowBgColor = _settingsService.CustomWindowBgColor;
            _customBorderColor = _settingsService.CustomBorderColor;
            _customTextColor = _settingsService.CustomTextColor;
            _customIconColor = _settingsService.CustomIconColor;
            _showSystemMonitor = _settingsService.ShowSystemMonitor;
            _showNetworkMonitor = _settingsService.ShowNetworkMonitor;
            _showCpuRamMonitor = _settingsService.ShowCpuRamMonitor;
            _showPlugins = _settingsService.ShowPlugins;
            _showWorldClock = _settingsService.ShowWorldClock;
            _worldClockTimeZone = _settingsService.WorldClockTimeZone;
            _monitorsOnLeft = _settingsService.MonitorsOnLeft;
            _hardwareOnLeft = _settingsService.HardwareOnLeft;
            _compactClock = _settingsService.CompactClock;
            _autoCheckUpdates = _settingsService.AutoCheckUpdates;
            _silentAutoUpdate = _settingsService.SilentAutoUpdate;
            _hideClipboard = _settingsService.HideClipboard;
            // Build pinned zone items (BD + AZ pinned by default)
            var pinned = _settingsService.PinnedWorldClockZones ?? new System.Collections.Generic.List<string>();
            WorldClockPinItems = new ObservableCollection<WorldClockZonePinItem>(
                WorldClockZoneOptions.Select(kv => new WorldClockZonePinItem(kv.Key, kv.Value, pinned.Contains(kv.Value), OnZonePinChanged))
            );
        }

        [ObservableProperty]
        private string _currentSection = "General"; // General, Personalization, Privacy

        public string[] Themes => new[] { "Light", "Dark", "Transparent", "System" };
        public string[] BarSizes => new[] { "Small", "Medium", "Large" };
        public string[] Alignments => new[] { "Left", "Center", "Right" };

        public bool AlwaysOnTop
        {
            get => _settingsService.AlwaysOnTop;
            set
            {
                _settingsService.AlwaysOnTop = value;
                OnPropertyChanged();
            }
        }

        public int AutoCleanHours
        {
            get => _settingsService.AutoCleanHours;
            set
            {
                _settingsService.AutoCleanHours = value;
                OnPropertyChanged();
            }
        }

        public string HotkeyString
        {
            get => _settingsService.HotkeyString;
            set
            {
                _settingsService.HotkeyString = value;
                OnPropertyChanged();
            }
        }

        public string Theme
        {
            get => _settingsService.Theme;
            set
            {
                _settingsService.Theme = value;
                OnPropertyChanged();
                // Theme colors are handled by MainWindow.UpdateTheme() via DynamicResource overrides.
                // No ApplicationThemeManager.Apply() call needed — base theme is set once in App.xaml.cs.
            }
        }

        public string BarSize
        {
            get => _settingsService.BarSize;
            set
            {
                _settingsService.BarSize = value;
                OnPropertyChanged();
            }
        }

        public string Alignment
        {
            get => _settingsService.Alignment;
            set
            {
                _settingsService.Alignment = value;
                OnPropertyChanged();
            }
        }

        public string ShelfPosition
        {
            get => _settingsService.ShelfPosition;
            set
            {
                _settingsService.ShelfPosition = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsBottomChecked));
                OnPropertyChanged(nameof(IsTopChecked));
            }
        }

        public bool IsBottomChecked
        {
            get => ShelfPosition == "Bottom";
            set { if (value) ShelfPosition = "Bottom"; }
        }

        public bool IsTopChecked
        {
            get => ShelfPosition == "Top";
            set { if (value) ShelfPosition = "Top"; }
        }

        [ObservableProperty]
        private bool _startWithWindows;

        partial void OnStartWithWindowsChanged(bool value)
        {
            _settingsService.StartWithWindows = value;
            _startupService.SetStartup(value);
        }

        public bool CopyItemsToDestination
        {
            get => _settingsService.CopyItemsToDestination;
            set
            {
                _settingsService.CopyItemsToDestination = value;
                OnPropertyChanged();
            }
        }

        public string TrayIconAction
        {
            get => _settingsService.TrayIconAction;
            set
            {
                _settingsService.TrayIconAction = value;
                OnPropertyChanged();
            }
        }

        public string[] TrayIconActions => new[] { "Show/Hide Shelf", "Open Settings" };

        public int MaxFileSizeMB
        {
            get => _settingsService.MaxFileSizeMB;
            set
            {
                _settingsService.MaxFileSizeMB = value;
                OnPropertyChanged();
            }
        }

        public int MaxHistoryItems
        {
            get => _settingsService.MaxHistoryItems;
            set
            {
                _settingsService.MaxHistoryItems = value;
                OnPropertyChanged();
            }
        }

        [ObservableProperty]
        private string _customCardColor;
        [ObservableProperty]
        private string _customControlBgColor;
        [ObservableProperty]
        private string _customAccentColor;
        [ObservableProperty]
        private string _customWindowBgColor;
        [ObservableProperty]
        private string _customBorderColor;
        [ObservableProperty]
        private string _customTextColor;
        [ObservableProperty]
        private string _customIconColor;

        [ObservableProperty]
        private bool _showSystemMonitor;

        [ObservableProperty]
        private bool _showNetworkMonitor;

        [ObservableProperty]
        private bool _showCpuRamMonitor;

        [ObservableProperty]
        private bool _showPlugins;

        [ObservableProperty]
        private bool _showWorldClock;

        [ObservableProperty]
        private bool _monitorsOnLeft;

        [ObservableProperty]
        private bool _hardwareOnLeft;

        [ObservableProperty]
        private bool _compactClock;

        private string _worldClockTimeZone;
        public string WorldClockTimeZone
        {
            get => _worldClockTimeZone ??= _settingsService.WorldClockTimeZone;
            set
            {
                if (_worldClockTimeZone != value)
                {
                    _worldClockTimeZone = value;
                    _settingsService.WorldClockTimeZone = value;
                    OnPropertyChanged();
                }
            }
        }

        private System.Collections.Generic.KeyValuePair<string, string>[] _worldClockZoneOptionsCache;
        public System.Collections.Generic.KeyValuePair<string, string>[] WorldClockZoneOptions
        {
            get
            {
                if (_worldClockZoneOptionsCache != null) return _worldClockZoneOptionsCache;
                var friendly = new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string>("Arizona (Phoenix) MST UTC-7 no DST", "US Mountain Standard Time"),
                    new System.Collections.Generic.KeyValuePair<string, string>("Bangladesh (Dhaka) BDT UTC+6", "Bangladesh Standard Time"),
                    new System.Collections.Generic.KeyValuePair<string, string>("Los Angeles (PST/PDT) UTC-8", "Pacific Standard Time"),
                    new System.Collections.Generic.KeyValuePair<string, string>("Chicago (CST/CDT) UTC-6", "Central Standard Time"),
                    new System.Collections.Generic.KeyValuePair<string, string>("New York (EST/EDT) UTC-5", "Eastern Standard Time"),
                    new System.Collections.Generic.KeyValuePair<string, string>("UTC", "UTC"),
                    new System.Collections.Generic.KeyValuePair<string, string>("Local System Time", "Local"),
                };
                try
                {
                    var all = TimeZoneInfo.GetSystemTimeZones()
                        .Where(tz => !friendly.Any(f => f.Value == tz.Id))
                        .Select(tz => new System.Collections.Generic.KeyValuePair<string, string>(tz.DisplayName, tz.Id))
                        .OrderBy(kv => kv.Key)
                        .ToArray();
                    _worldClockZoneOptionsCache = friendly.Concat(all).ToArray();
                }
                catch
                {
                    _worldClockZoneOptionsCache = friendly;
                }
                return _worldClockZoneOptionsCache;
            }
        }

        partial void OnShowWorldClockChanged(bool value)
        {
            _settingsService.ShowWorldClock = value;
        }

        partial void OnMonitorsOnLeftChanged(bool value)
        {
            _settingsService.MonitorsOnLeft = value;
        }

        partial void OnHardwareOnLeftChanged(bool value)
        {
            _settingsService.HardwareOnLeft = value;
        }

        partial void OnCompactClockChanged(bool value)
        {
            _settingsService.CompactClock = value;
        }

        [ObservableProperty]
        private bool _autoCheckUpdates = false;

        partial void OnAutoCheckUpdatesChanged(bool value)
        {
            _settingsService.AutoCheckUpdates = value;
        }

        [ObservableProperty]
        private bool _silentAutoUpdate = false;

        partial void OnSilentAutoUpdateChanged(bool value)
        {
            _settingsService.SilentAutoUpdate = value;
        }

        [ObservableProperty]
        private bool _hideClipboard = false;

        partial void OnHideClipboardChanged(bool value)
        {
            _settingsService.HideClipboard = value;
        }

        [ObservableProperty]
        private ObservableCollection<WorldClockZonePinItem> _worldClockPinItems;

        private void OnZonePinChanged(string zoneId, bool isPinned)
        {
            try
            {
                var list = new System.Collections.Generic.List<string>(_settingsService.PinnedWorldClockZones ?? new System.Collections.Generic.List<string>());
                if (isPinned)
                {
                    if (!list.Contains(zoneId)) list.Add(zoneId);
                }
                else
                {
                    list.Remove(zoneId);
                }
                var ordered = WorldClockZoneOptions.Where(kv => list.Contains(kv.Value)).Select(kv => kv.Value).ToList();
                _settingsService.PinnedWorldClockZones = ordered;
                // Defer UI updates to avoid re-entrancy crash when CheckBox is still handling click
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new System.Action(() =>
                {
                    OnPropertyChanged(nameof(PinnedCountText));
                    OnPropertyChanged(nameof(PinnedDisplayItems));
                    OnPropertyChanged(nameof(AvailableZonesToAdd));
                    OnPropertyChanged(nameof(HasAvailableZones));
                    IsZoneDropdownOpen = !string.IsNullOrWhiteSpace(ZoneSearchText) && AvailableZonesToAdd.Any();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (System.Exception ex)
            {
                ClipDropPro.Services.Logger.Write($"[Settings] OnZonePinChanged error {zoneId}: {ex.Message}");
            }
        }

        public string PinnedCountText => $"{WorldClockPinItems?.Count(x => x.IsPinned) ?? 0} pinned";

        public System.Collections.Generic.IEnumerable<WorldClockZonePinItem> PinnedDisplayItems => WorldClockPinItems?.Where(x => x.IsPinned) ?? System.Linq.Enumerable.Empty<WorldClockZonePinItem>();

        public System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string,string>> AvailableZonesToAdd => GetFilteredAvailableZones();

        private System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string,string>> GetFilteredAvailableZones()
        {
            var baseList = WorldClockZoneOptions.Where(kv => !(_settingsService.PinnedWorldClockZones?.Contains(kv.Value) ?? false));
            if (string.IsNullOrWhiteSpace(ZoneSearchText)) return baseList;
            var q = ZoneSearchText.Trim().ToLowerInvariant();
            return baseList.Where(kv => kv.Key.ToLowerInvariant().Contains(q) || kv.Value.ToLowerInvariant().Contains(q));
        }

        public bool HasAvailableZones => AvailableZonesToAdd.Any();

        [ObservableProperty]
        private System.Collections.Generic.KeyValuePair<string,string> _selectedZoneToAdd;

        [ObservableProperty]
        private string _zoneSearchText = "";

        [ObservableProperty]
        private bool _isZoneDropdownOpen = false;

        private System.Windows.Threading.DispatcherTimer _zoneSearchDebounceTimer;

        partial void OnZoneSearchTextChanged(string value)
        {
            // Debounce to avoid lag and cursor jump on every keystroke
            if (_zoneSearchDebounceTimer == null)
            {
                _zoneSearchDebounceTimer = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(180) };
                _zoneSearchDebounceTimer.Tick += (s, e) =>
                {
                    _zoneSearchDebounceTimer.Stop();
                    OnPropertyChanged(nameof(AvailableZonesToAdd));
                    OnPropertyChanged(nameof(HasAvailableZones));
                    IsZoneDropdownOpen = !string.IsNullOrWhiteSpace(ZoneSearchText) && AvailableZonesToAdd.Any();
                };
            }
            _zoneSearchDebounceTimer.Stop();
            _zoneSearchDebounceTimer.Start();
            // Immediately update count text but not the full list to keep UI responsive
            OnPropertyChanged(nameof(HasAvailableZones));
        }

        [RelayCommand]
        private void AddPinnedZone()
        {
            if (string.IsNullOrEmpty(SelectedZoneToAdd.Value)) return;
            var id = SelectedZoneToAdd.Value;
            var item = WorldClockPinItems.FirstOrDefault(x => x.ZoneId == id);
            if (item != null && !item.IsPinned)
            {
                item.IsPinned = true;
            }
            SelectedZoneToAdd = default;
            ZoneSearchText = "";
        }

        partial void OnCustomCardColorChanged(string value) => UpdateCustomResource("CardBg", value, _settingsService.CustomCardColor = value);
        partial void OnCustomControlBgColorChanged(string value) => UpdateCustomResource("ControlBg", value, _settingsService.CustomControlBgColor = value);
        partial void OnCustomAccentColorChanged(string value) => UpdateCustomResource("AccentColor", value, _settingsService.CustomAccentColor = value);
        partial void OnCustomWindowBgColorChanged(string value) => UpdateCustomResource("WindowBg", value, _settingsService.CustomWindowBgColor = value);
        partial void OnCustomBorderColorChanged(string value) => UpdateCustomResource("BorderColor", value, _settingsService.CustomBorderColor = value);
        partial void OnCustomTextColorChanged(string value) => UpdateCustomResource("TextColor", value, _settingsService.CustomTextColor = value);
        partial void OnCustomIconColorChanged(string value) => UpdateCustomResource("IconColor", value, _settingsService.CustomIconColor = value);

        partial void OnShowSystemMonitorChanged(bool value)
        {
            _settingsService.ShowSystemMonitor = value;
        }

        partial void OnShowNetworkMonitorChanged(bool value)
        {
            _settingsService.ShowNetworkMonitor = value;
        }

        partial void OnShowCpuRamMonitorChanged(bool value)
        {
            _settingsService.ShowCpuRamMonitor = value;
        }

        partial void OnShowPluginsChanged(bool value)
        {
            _settingsService.ShowPlugins = value;
        }

        private static void UpdateCustomResource(string resourceKey, string hex, string _)
        {
            if (System.Windows.Application.Current.Resources[resourceKey] is System.Windows.Media.SolidColorBrush)
            {
                try
                {
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                    System.Windows.Application.Current.Resources[resourceKey] = new System.Windows.Media.SolidColorBrush(color);
                }
                catch { }
            }
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void Navigate(string section)
        {
            CurrentSection = section;
        }
    }

    public partial class WorldClockZonePinItem : ObservableObject
    {
        public string Display { get; }
        public string ZoneId { get; }
        private readonly System.Action<string, bool> _onChanged;
        public WorldClockZonePinItem(string display, string zoneId, bool isPinned, System.Action<string, bool> onChanged)
        {
            Display = display;
            ZoneId = zoneId;
            _isPinned = isPinned;
            _onChanged = onChanged;
        }
        [ObservableProperty] private bool _isPinned;
        partial void OnIsPinnedChanged(bool value)
        {
            // Defer to DispatcherPriority.Background so the CheckBox finishes its click
            // event before we collapse its container via DataTrigger — avoids crash.
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(
                new System.Action(() => _onChanged?.Invoke(ZoneId, value)),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
