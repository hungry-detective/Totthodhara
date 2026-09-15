using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ClipDropPro.Services
{
    public class FileStorageService : IFileStorageService
    {
        private readonly string _storageFolder;

        // Shared connection pool: a new HttpClient per download burns a socket
        // per call and defeats keep-alive. One static instance reuses connections.
        // PooledConnectionLifetime recycles connections every 2 minutes so DNS
        // changes are picked up — this removes the only downside of a static
        // client (stale DNS), making it strictly better than per-call clients.
        private static readonly HttpClient _sharedHttpClient = new HttpClient(
            new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2)
            })
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        public FileStorageService()
        {
            _storageFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "Storage");
            if (!Directory.Exists(_storageFolder))
            {
                Directory.CreateDirectory(_storageFolder);
            }
        }

        public async Task<string> SaveFileAsync(string sourcePath)
        {
            var ext = Path.GetExtension(sourcePath);
            var fileName = $"{Guid.NewGuid()}{ext}";
            var destPath = Path.Combine(_storageFolder, fileName);

            using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
            using (var destStream = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await sourceStream.CopyToAsync(destStream);
            }
            return destPath;
        }

        public async Task<string> SaveTextAsync(string text)
        {
            var fileName = $"{Guid.NewGuid()}.txt";
            var destPath = Path.Combine(_storageFolder, fileName);
            await File.WriteAllTextAsync(destPath, text);
            return destPath;
        }

        public async Task<string> SaveBitmapAsync(BitmapSource bitmap)
        {
            var fileName = $"{Guid.NewGuid()}.png";
            var destPath = Path.Combine(_storageFolder, fileName);

            // PNG-encoding a fullscreen screenshot can take 100-500ms — keep it
            // off the clipboard/UI thread. Freeze first so the pool thread may
            // safely read the pixels (no-op if already frozen). On failure the
            // partial file is deleted so no corrupt PNG ever reaches storage.
            if (bitmap.CanFreeze && !bitmap.IsFrozen)
                bitmap.Freeze();
            try
            {
                await Task.Run(() =>
                {
                    using (var fileStream = new FileStream(destPath, FileMode.Create))
                    {
                        BitmapEncoder encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        encoder.Save(fileStream);
                    }
                });
            }
            catch
            {
                try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
                throw;
            }

            return destPath;
        }

        public async Task<string> DownloadImageAsync(string url)
        {
            var response = await _sharedHttpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var extension = ".png";
            try 
            {
                var uri = new Uri(url);
                extension = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrEmpty(extension)) extension = ".png";
            } catch { }

            var fileName = $"{Guid.NewGuid()}{extension}";
            var destPath = Path.Combine(_storageFolder, fileName);

            using (var fileStream = new FileStream(destPath, FileMode.Create))
            {
                await response.Content.CopyToAsync(fileStream);
            }

            return destPath;
        }

        public Task DeleteFileAsync(string filePath)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch { }
            }
            return Task.CompletedTask;
        }

        public Task CleanOldFilesAsync(int hoursToKeep, IEnumerable<string> pinnedFiles)
        {
            var cutoffDate = DateTime.Now.AddHours(-hoursToKeep);
            var allFiles = Directory.GetFiles(_storageFolder);
            var pinnedSet = new HashSet<string>(pinnedFiles, StringComparer.OrdinalIgnoreCase);

            foreach (var file in allFiles)
            {
                if (!pinnedSet.Contains(file))
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        try
                        {
                            fileInfo.Delete();
                        }
                        catch
                        {
                            // ignore errors (file might be in use)
                        }
                    }
                }
            }
            return Task.CompletedTask;
        }
    }
}
