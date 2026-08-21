using System;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ClipDropPro.Services
{
    public static class Logger
    {
        private static readonly Channel<string> _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private static readonly string _logPath;
        private static bool _initialized;

        static Logger()
        {
            var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            _logPath = Path.Combine(dataDir, "app_debug.txt");
            _ = Task.Run(ProcessQueue);
            _initialized = true;
        }

        public static void Write(string message)
        {
            if (!_initialized) return;
            _channel.Writer.TryWrite($"{DateTime.Now:HH:mm:ss.fff}: {message}\n");
        }

        private static async Task ProcessQueue()
        {
            await using var stream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, true);
            await using var writer = new StreamWriter(stream);

            await foreach (var line in _channel.Reader.ReadAllAsync())
            {
                await writer.WriteAsync(line);
                await writer.FlushAsync();
            }
        }
    }
}
