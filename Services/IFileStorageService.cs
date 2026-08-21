using System.Collections.Generic;
using System.Threading.Tasks;

namespace ClipDropPro.Services
{
    public interface IFileStorageService
    {
        Task<string> SaveFileAsync(string sourcePath);
        Task<string> SaveTextAsync(string text);
        Task<string> SaveBitmapAsync(System.Windows.Media.Imaging.BitmapSource bitmap);
        Task<string> DownloadImageAsync(string url);
        Task DeleteFileAsync(string filePath);
        Task CleanOldFilesAsync(int hoursToKeep, IEnumerable<string> pinnedFiles);
    }
}
