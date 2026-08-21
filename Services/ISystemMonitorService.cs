using System;

namespace ClipDropPro.Services
{
    public interface ISystemMonitorService : IDisposable
    {
        int CpuUsage { get; }
        int MemoryUsage { get; }
        long UsedMemoryMB { get; }
        long TotalMemoryMB { get; }
        double NetworkUpKBs { get; }
        double NetworkDownKBs { get; }
        bool IsRunning { get; }
        event Action Updated;
        void Start();
        void Stop();
        void SetInterval(int intervalMs);
    }
}
