using SQLite;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipDropPro.Models
{
    public partial class ClipboardItem : ObservableObject
    {
        private int _id;
        [PrimaryKey, AutoIncrement]
        public int Id { get => _id; set => SetProperty(ref _id, value); }

        private string _fileName = string.Empty;
        public string FileName { get => _fileName; set => SetProperty(ref _fileName, value); }
        
        private string _filePath = string.Empty;
        public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }
        
        private string _textContent = string.Empty;
        public string TextContent { get => _textContent; set => SetProperty(ref _textContent, value); }

        private bool _isFile = false;
        public bool IsFile { get => _isFile; set => SetProperty(ref _isFile, value); }

        private bool _isPinned = false;
        public bool IsPinned { get => _isPinned; set => SetProperty(ref _isPinned, value); }

        private bool _isSnippet = false;
        public bool IsSnippet { get => _isSnippet; set => SetProperty(ref _isSnippet, value); }

        private DateTime _dateAdded = DateTime.Now;
        public DateTime DateAdded { get => _dateAdded; set => SetProperty(ref _dateAdded, value); }

        private string _displayTitle = string.Empty;
        public string DisplayTitle { get => _displayTitle; set => SetProperty(ref _displayTitle, value); }

        private string _iconGlyph = string.Empty;
        public string IconGlyph { get => _iconGlyph; set => SetProperty(ref _iconGlyph, value); }

        [Ignore]
        public bool IsColor => !string.IsNullOrEmpty(TextContent) &&
            TextContent.Length is 7 or 9 &&
            TextContent[0] == '#' &&
            int.TryParse(TextContent.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _);

        private string _origin = string.Empty;
        public string Origin { get => _origin; set => SetProperty(ref _origin, value); }

        [Ignore]
        public bool IsImage => IsFile && (FilePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || 
                                          FilePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                                          FilePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || 
                                          FilePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || 
                                          FilePath.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                                          FilePath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase));

        [Ignore]
        public bool IsVideo => IsFile && (FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                          FilePath.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                                          FilePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                                          FilePath.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
                                          FilePath.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase));

        [Ignore]
        public bool IsPdf => IsFile && FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

        [Ignore]
        public bool IsAudio => IsFile && (FilePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                           FilePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                                           FilePath.EndsWith(".wma", StringComparison.OrdinalIgnoreCase) ||
                                           FilePath.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
                                           FilePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                                           FilePath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
                                           FilePath.EndsWith(".aac", StringComparison.OrdinalIgnoreCase));

        private int _index;
        [Ignore]
        public int Index
        {
            get => _index;
            set
            {
                if (SetProperty(ref _index, value))
                    OnPropertyChanged(nameof(DisplayIndex));
            }
        }

        [Ignore]
        public string DisplayIndex => Index > 0 ? Index.ToString() : string.Empty;

        [Ignore]
        public string DisplayText 
        {
            get
            {
                string text = !string.IsNullOrEmpty(DisplayTitle) ? DisplayTitle : (IsFile ? FileName : TextContent);
                return text?.Replace("\r", " ").Replace("\n", " ").Trim() ?? string.Empty;
            }
        }

        private ImageSource? _thumbnailSource;
        [Ignore]
        public ImageSource? ThumbnailSource { get => _thumbnailSource; set => SetProperty(ref _thumbnailSource, value); }

        private string _resolutionText = string.Empty;
        [Ignore]
        public string ResolutionText { get => _resolutionText; set => SetProperty(ref _resolutionText, value); }

        private bool _isUrl;
        [Ignore]
        public bool IsUrl { get => _isUrl; set => SetProperty(ref _isUrl, value); }

        [Ignore]
        public bool HasIcon => IsFile || IsUrl;

        private ImageSource? _iconSource;
        [Ignore]
        public ImageSource? IconSource { get => _iconSource; set => SetProperty(ref _iconSource, value); }

        private bool _isFirstUnpinned;
        [Ignore]
        public bool IsFirstUnpinned { get => _isFirstUnpinned; set => SetProperty(ref _isFirstUnpinned, value); }

        [Ignore]
        public bool IsDoubleDigit => Index >= 10;

        private bool _isRemoving;
        [Ignore]
        public bool IsRemoving 
        { 
            get => _isRemoving; 
            set => SetProperty(ref _isRemoving, value); 
        }

        private bool _isNew;
        [Ignore]
        public bool IsNew
        {
            get => _isNew;
            set
            {
                if (SetProperty(ref _isNew, value) && value)
                    _ = ClearIsNewAsync();
            }
        }

        private async Task ClearIsNewAsync()
        {
            await Task.Delay(800);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => IsNew = false);
        }

        private bool _isSelected;
        [Ignore]
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        private string _statusText = string.Empty;
        [Ignore]
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }
    }
}
