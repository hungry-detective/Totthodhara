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
        // Sensor gates: set from MainViewModel based on which pills are visible.
        // Disabled sensors are skipped in OnTick so hidden widgets cost nothing.
        bool CpuEnabled { get; set; }
        bool NetworkEnabled { get; set; }
        event Action Updated;
        void Start();
        void Stop();
        void SetInterval(int intervalMs);
    }
}
