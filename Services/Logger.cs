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

        public static void Write(string message)
        {
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss.fff}: {message}\r\n");
                }
            }
            catch { }
        }
    }
}
