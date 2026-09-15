using System;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ClipDropPro.Services
{
    public static class Logger
    {
        private static readonly string _logPath;

        static Logger()
        {
            var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            _logPath = Path.Combine(dataDir, "app_debug.txt");
        }

        private static readonly object _lock = new object();
        private static int _writeCount;
        private const long MaxLogBytes = 5L * 1024 * 1024;
        private const long KeepTailBytes = 1L * 1024 * 1024;

        public static void Write(string message)
        {
            try
            {
                lock (_lock)
                {
                    // Bullet-proofing: app_debug.txt grew unbounded (multi-MB).
                    // Check size every 500 writes (not every write — FileInfo is a
                    // syscall per call); over the cap, keep only the newest 1MB.
                    if (++_writeCount % 500 == 0)
                        RotateIfNeeded();
                    File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss.fff}: {message}\r\n");
                }
            }
            catch { }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                var info = new FileInfo(_logPath);
                if (!info.Exists || info.Length <= MaxLogBytes) return;
                using var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length <= MaxLogBytes) return;
                stream.Seek(-KeepTailBytes, SeekOrigin.End);
                using var reader = new StreamReader(stream);
                string tail = reader.ReadToEnd();
                int newline = tail.IndexOf('\n');
                if (newline >= 0) tail = tail.Substring(newline + 1);
                File.WriteAllText(_logPath, $"--- rotated {DateTime.Now:yyyy-MM-dd HH:mm} (kept newest 1MB) ---\r\n" + tail);
            }
            catch { }
        }
    }
}
