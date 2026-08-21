using CommunityToolkit.Mvvm.ComponentModel;
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
        }

        [ObservableProperty]
        private string _currentSection = "General"; // General, Personalization, Privacy

        public string[] Themes => new[] { "Light", "Dark", "System" };
        public string[] BarSizes => new[] { "Small", "Medium", "Large" };
        public string[] Alignments => new[] { "Left", "Centered", "Right" };

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
}
