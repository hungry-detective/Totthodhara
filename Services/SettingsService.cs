using System;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace ClipDropPro.Services
{
    public class Settings
    {
        public bool AlwaysOnTop { get; set; } = true;
        public int AutoCleanHours { get; set; } = 2;
        public string HotkeyString { get; set; } = "OemTilde";
        public ModifierKeys HotkeyModifiers { get; set; } = ModifierKeys.Control;
        public string Theme { get; set; } = "System";
        public string BarSize { get; set; } = "Small";
        public string Alignment { get; set; } = "Center";
        public string ShelfPosition { get; set; } = "Bottom";
        public bool StartWithWindows { get; set; } = false;
        public bool CopyItemsToDestination { get; set; } = true;
        public string TrayIconAction { get; set; } = "Show/Hide Shelf";
        public int MaxFileSizeMB { get; set; } = 50;
        public int MaxHistoryItems { get; set; } = 30;
        public string CustomCardColor { get; set; } = "#FFFFFF";
        public string CustomControlBgColor { get; set; } = "#E5E5E5";
        public string CustomAccentColor { get; set; } = "#0078D4";
        public string CustomWindowBgColor { get; set; } = "#F3F3F3";
        public string CustomBorderColor { get; set; } = "#CCCCCC";
        public string CustomTextColor { get; set; } = "#000000";
        public string CustomIconColor { get; set; } = "#323232";
        public bool ShowSystemMonitor { get; set; } = true;
        public bool ShowNetworkMonitor { get; set; } = true;
        public bool ShowCpuRamMonitor { get; set; } = true;
        public bool ShowPlugins { get; set; } = true;
        public bool ShowWorldClock { get; set; } = false;
        public string WorldClockTimeZone { get; set; } = "US Mountain Standard Time";
        public System.Collections.Generic.List<string> PinnedWorldClockZones { get; set; } = new() { "Bangladesh Standard Time", "US Mountain Standard Time" };
        public bool MonitorsOnLeft { get; set; } = false;
        public bool HardwareOnLeft { get; set; } = true;
        public bool CompactClock { get; set; } = false;
        public bool AutoCheckUpdates { get; set; } = true;
        public bool SilentAutoUpdate { get; set; } = false;
        public bool HideClipboard { get; set; } = false;
        public bool IncludeDIBInDrag { get; set; } = false;
    }

    public class SettingsService : ISettingsService
    {
        private readonly string _settingsFilePath;
        private Settings _settings;

        public SettingsService()
        {
            var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            _settingsFilePath = Path.Combine(dataDir, "settings.json");
            Load();
        }

        private void Load()
        {
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    _settings = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
                    if (_settings.BarSize == "Default")
                        _settings.BarSize = "Small";
                    // Migrate old single zone to pinned list
                    if (_settings.PinnedWorldClockZones == null || _settings.PinnedWorldClockZones.Count == 0)
                    {
                        if (!string.IsNullOrEmpty(_settings.WorldClockTimeZone))
                            _settings.PinnedWorldClockZones = new() { _settings.WorldClockTimeZone };
                        else
                            _settings.PinnedWorldClockZones = new() { "Bangladesh Standard Time", "US Mountain Standard Time" };
                    }
                    // Ensure at least BD+AZ are discoverable (don't force, just keep user's pins)
                }
                catch
                {
                    _settings = new Settings();
                }
            }
            else
            {
                _settings = new Settings();
            }
        }

        public void Save()
        {
            var json = JsonSerializer.Serialize(_settings);
            File.WriteAllText(_settingsFilePath, json);
        }

        public bool AlwaysOnTop
        {
            get => _settings.AlwaysOnTop;
            set { _settings.AlwaysOnTop = value; Save(); }
        }

        public int AutoCleanHours
        {
            get => _settings.AutoCleanHours;
            set { _settings.AutoCleanHours = value; Save(); }
        }

        public string HotkeyString
        {
            get => _settings.HotkeyString;
            set { _settings.HotkeyString = value; Save(); }
        }

        public ModifierKeys HotkeyModifiers
        {
            get => _settings.HotkeyModifiers;
            set { _settings.HotkeyModifiers = value; Save(); }
        }

        public string Theme
        {
            get => _settings.Theme;
            set { _settings.Theme = value; Save(); }
        }

        public string BarSize
        {
            get => _settings.BarSize;
            set { _settings.BarSize = value; Save(); }
        }

        public string Alignment
        {
            get => _settings.Alignment;
            set { _settings.Alignment = value; Save(); }
        }

        public string ShelfPosition
        {
            get => _settings.ShelfPosition;
            set { _settings.ShelfPosition = value; Save(); }
        }

        public bool StartWithWindows
        {
            get => _settings.StartWithWindows;
            set { _settings.StartWithWindows = value; Save(); }
        }

        public bool CopyItemsToDestination
        {
            get => _settings.CopyItemsToDestination;
            set { _settings.CopyItemsToDestination = value; Save(); }
        }

        public string TrayIconAction
        {
            get => _settings.TrayIconAction;
            set { _settings.TrayIconAction = value; Save(); }
        }

        public int MaxFileSizeMB
        {
            get => _settings.MaxFileSizeMB;
            set { _settings.MaxFileSizeMB = value; Save(); }
        }

        public int MaxHistoryItems
        {
            get => _settings.MaxHistoryItems;
            set { _settings.MaxHistoryItems = value; Save(); }
        }

        public string CustomCardColor
        {
            get => _settings.CustomCardColor;
            set { _settings.CustomCardColor = value; Save(); }
        }

        public string CustomControlBgColor
        {
            get => _settings.CustomControlBgColor;
            set { _settings.CustomControlBgColor = value; Save(); }
        }

        public string CustomAccentColor
        {
            get => _settings.CustomAccentColor;
            set { _settings.CustomAccentColor = value; Save(); }
        }

        public string CustomWindowBgColor
        {
            get => _settings.CustomWindowBgColor;
            set { _settings.CustomWindowBgColor = value; Save(); }
        }

        public string CustomBorderColor
        {
            get => _settings.CustomBorderColor;
            set { _settings.CustomBorderColor = value; Save(); }
        }

        public string CustomTextColor
        {
            get => _settings.CustomTextColor;
            set { _settings.CustomTextColor = value; Save(); }
        }

        public string CustomIconColor
        {
            get => _settings.CustomIconColor;
            set { _settings.CustomIconColor = value; Save(); }
        }

        public bool ShowSystemMonitor
        {
            get => _settings.ShowSystemMonitor;
            set { _settings.ShowSystemMonitor = value; Save(); }
        }

        public bool ShowNetworkMonitor
        {
            get => _settings.ShowNetworkMonitor;
            set { _settings.ShowNetworkMonitor = value; Save(); }
        }

        public bool ShowCpuRamMonitor
        {
            get => _settings.ShowCpuRamMonitor;
            set { _settings.ShowCpuRamMonitor = value; Save(); }
        }

        public bool ShowPlugins
        {
            get => _settings.ShowPlugins;
            set { _settings.ShowPlugins = value; Save(); }
        }

        public bool ShowWorldClock
        {
            get => _settings.ShowWorldClock;
            set { _settings.ShowWorldClock = value; Save(); }
        }

        public string WorldClockTimeZone
        {
            get => _settings.WorldClockTimeZone;
            set { _settings.WorldClockTimeZone = value; Save(); }
        }

        public System.Collections.Generic.List<string> PinnedWorldClockZones
        {
            get => _settings.PinnedWorldClockZones;
            set { _settings.PinnedWorldClockZones = value; Save(); }
        }

        public bool MonitorsOnLeft
        {
            get => _settings.MonitorsOnLeft;
            set { _settings.MonitorsOnLeft = value; Save(); }
        }

        public bool HardwareOnLeft
        {
            get => _settings.HardwareOnLeft;
            set { _settings.HardwareOnLeft = value; Save(); }
        }

        public bool CompactClock
        {
            get => _settings.CompactClock;
            set { _settings.CompactClock = value; Save(); }
        }

        public bool AutoCheckUpdates
        {
            get => _settings.AutoCheckUpdates;
            set { _settings.AutoCheckUpdates = value; Save(); }
        }

        public bool SilentAutoUpdate
        {
            get => _settings.SilentAutoUpdate;
            set { _settings.SilentAutoUpdate = value; Save(); }
        }

        public bool HideClipboard
        {
            get => _settings.HideClipboard;
            set { _settings.HideClipboard = value; Save(); }
        }

        public bool IncludeDIBInDrag
        {
            get => _settings.IncludeDIBInDrag;
            set { _settings.IncludeDIBInDrag = value; Save(); }
        }
    }
}
