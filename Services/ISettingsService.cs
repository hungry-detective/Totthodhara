using System.Windows.Input;

namespace ClipDropPro.Services
{
    public interface ISettingsService
    {
        bool AlwaysOnTop { get; set; }
        int AutoCleanHours { get; set; }
        string HotkeyString { get; set; }
        ModifierKeys HotkeyModifiers { get; set; }
        string Theme { get; set; } // "Light", "Dark", "System"
        string BarSize { get; set; } // "Small", "Medium", "Large"
        string Alignment { get; set; } // "Left", "Centered", "Right"
        string ShelfPosition { get; set; } // "Top", "Bottom"
        bool StartWithWindows { get; set; }
        bool CopyItemsToDestination { get; set; }
        string TrayIconAction { get; set; } // "Show/Hide Shelf", "Open Settings"
        int MaxFileSizeMB { get; set; }
        int MaxHistoryItems { get; set; }
        string CustomCardColor { get; set; }
        string CustomControlBgColor { get; set; }
        string CustomAccentColor { get; set; }
        string CustomWindowBgColor { get; set; }
        string CustomBorderColor { get; set; }
        string CustomTextColor { get; set; }
        string CustomIconColor { get; set; }
        bool ShowSystemMonitor { get; set; }
        bool ShowNetworkMonitor { get; set; }
        bool ShowCpuRamMonitor { get; set; }
        bool ShowPlugins { get; set; }
        void Save();
    }
}
