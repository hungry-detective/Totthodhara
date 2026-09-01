using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace ClipDropPro.Converters
{
    public class FileToSymbolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Models.ClipboardItem item)
            {
                if (!item.IsFile)
                {
                    if (item.IsUrl)
                        return Wpf.Ui.Controls.SymbolRegular.Globe24;
                    return Wpf.Ui.Controls.SymbolRegular.TextDescription24;
                }

                if (item.IsImage)
                    return Wpf.Ui.Controls.SymbolRegular.Image24;
                if (item.IsVideo)
                    return Wpf.Ui.Controls.SymbolRegular.Video24;
                if (item.IsAudio)
                    return Wpf.Ui.Controls.SymbolRegular.MusicNote224;
                if (item.IsPdf)
                    return Wpf.Ui.Controls.SymbolRegular.DocumentPdf24;

                string ext = System.IO.Path.GetExtension(item.FilePath).ToLower();
                switch (ext)
                {
                    case ".docx":
                    case ".doc": return Wpf.Ui.Controls.SymbolRegular.DocumentText24;
                    case ".xlsx":
                    case ".xls":
                    case ".csv": return Wpf.Ui.Controls.SymbolRegular.Table24;
                    case ".zip":
                    case ".rar":
                    case ".7z":
                    case ".tar":
                    case ".gz": return Wpf.Ui.Controls.SymbolRegular.Archive24;
                    case ".txt":
                    case ".log":
                    case ".md": return Wpf.Ui.Controls.SymbolRegular.DocumentText24;
                    case ".exe":
                    case ".msi": return Wpf.Ui.Controls.SymbolRegular.Window24;
                    case ".dll": return Wpf.Ui.Controls.SymbolRegular.Code24;
                    case ".psd":
                    case ".ai": return Wpf.Ui.Controls.SymbolRegular.Image24;
                    case ".html":
                    case ".htm":
                    case ".css":
                    case ".js":
                    case ".ts":
                    case ".py":
                    case ".cs":
                    case ".cpp":
                    case ".h":
                    case ".java": return Wpf.Ui.Controls.SymbolRegular.Code24;
                    default: return Wpf.Ui.Controls.SymbolRegular.Document24;
                }
            }
            return Wpf.Ui.Controls.SymbolRegular.Document24;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class InvertedBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
    }

    public class BooleanToHiddenConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Hidden : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
    }

    public class PinnedToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPinned && isPinned)
            {
                return new SolidColorBrush(Colors.Gold);
            }
            return new SolidColorBrush(Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StringToHorizontalAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string alignment)
            {
                switch (alignment)
                {
                    case "Left": return System.Windows.HorizontalAlignment.Left;
                    case "Right": return System.Windows.HorizontalAlignment.Right;
                    default: return System.Windows.HorizontalAlignment.Center;
                }
            }
            return System.Windows.HorizontalAlignment.Center;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StringEqualityToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() == parameter?.ToString() ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PinnedToMenuItemConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPinned && isPinned)
                return "Unpin";
            return "Pin to top";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class SnippetToMenuItemConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSnippet && isSnippet)
                return "Remove Snippet";
            return "Save as Snippet";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class DebugStatusToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status && !string.IsNullOrEmpty(status) && status != "Ready")
                return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class DebugStatusToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status && !string.IsNullOrEmpty(status) && status != "Ready")
                return true;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count && count > 0)
                return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StringEqualityToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() == parameter?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return parameter?.ToString();
            return System.Windows.Data.Binding.DoNothing;
        }
    }

    public class StringNotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StringEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value?.ToString()) ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class HexToColorConverter : IValueConverter
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SolidColorBrush> _cache = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex && hex.Length is 7 or 9 && hex[0] == '#')
            {
                if (_cache.TryGetValue(hex, out var brush))
                    return brush;
                try
                {
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                    brush = new SolidColorBrush(color);
                    brush.Freeze();
                    _cache[hex] = brush;
                    return brush;
                }
                catch { }
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToStarConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? new GridLength(0, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class LeftMonitorsColumnConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is bool hideClipboard && values[1] is bool showLeft && hideClipboard && showLeft)
                return 7;
            return 0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
