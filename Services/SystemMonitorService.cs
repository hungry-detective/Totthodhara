using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ClipDropPro.Services
{
    public class SystemMonitorService : ISystemMonitorService
    {
        private readonly DispatcherTimer _timer;
        private PerformanceCounter? _cpuCounter;
        private NetworkInterface[]? _interfaces;
        private long _lastBytesReceived;
        private long _lastBytesSent;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private bool _isRunning;

        public int CpuUsage { get; private set; }
        public int MemoryUsage { get; private set; }
        public long UsedMemoryMB { get; private set; }
        public long TotalMemoryMB { get; private set; }
        public double NetworkUpKBs { get; private set; }
        public double NetworkDownKBs { get; private set; }
        public bool IsRunning => _isRunning;
        public bool CpuEnabled { get; set; } = true;
        public bool NetworkEnabled { get; set; } = true;

        public event Action? Updated;

        public SystemMonitorService()
        {
            _timer = new DispatcherTimer();
            _timer.Tick += OnTick;
            _timer.Interval = TimeSpan.FromSeconds(2);

            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
            }
            catch
            {
                _cpuCounter = null;
            }

            _interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                    && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && !IsFilterDriver(ni))
                .ToArray();
            _stopwatch.Start();

            long totalBytes = 0, sentBytes = 0;
            foreach (var ni in _interfaces)
            {
                var stats = ni.GetIPv4Statistics();
                totalBytes += stats.BytesReceived;
                sentBytes += stats.BytesSent;
            }
            _lastBytesReceived = totalBytes;
            _lastBytesSent = sentBytes;
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            _timer.Start();
        }

        public void Stop()
        {
            _isRunning = false;
            _timer.Stop();
        }

        public void SetInterval(int intervalMs)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(500, intervalMs));
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (CpuEnabled)
            {
                UpdateCpu();
                UpdateMemory();
            }
            if (NetworkEnabled)
                UpdateNetwork();
            Updated?.Invoke();
        }

        private void UpdateCpu()
        {
            try
            {
                if (_cpuCounter != null)
                    CpuUsage = (int)_cpuCounter.NextValue();
            }
            catch
            {
                CpuUsage = 0;
            }
        }

        private void UpdateMemory()
        {
            try
            {
                // GlobalMemoryStatusEx is a single cheap P/Invoke (~microseconds).
                // The old WMI query (Win32_OperatingSystem) cost 50-200ms per tick
                // on the UI thread — do NOT revert to it.
                var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref mem))
                {
                    TotalMemoryMB = (long)(mem.ullTotalPhys / 1024 / 1024);
                    long availMB = (long)(mem.ullAvailPhys / 1024 / 1024);
                    UsedMemoryMB = TotalMemoryMB - availMB;
                    MemoryUsage = (int)mem.dwMemoryLoad;
                }
            }
            catch
            {
                MemoryUsage = 0;
            }
        }

        private void UpdateNetwork()
        {
            try
            {
                if (_interfaces == null)
                    _interfaces = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                            && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                            && !IsFilterDriver(ni))
                        .ToArray();

                var elapsed = _stopwatch.Elapsed.TotalSeconds;
                if (elapsed < 0.5) return;
                _stopwatch.Restart();

                long totalRecv = 0, totalSent = 0;
                foreach (var ni in _interfaces)
                {
                    var stats = ni.GetIPv4Statistics();
                    totalRecv += stats.BytesReceived;
                    totalSent += stats.BytesSent;
                }

                NetworkDownKBs = Math.Round((totalRecv - _lastBytesReceived) / 1024.0 / elapsed, 1);
                NetworkUpKBs = Math.Round((totalSent - _lastBytesSent) / 1024.0 / elapsed, 1);

                _lastBytesReceived = totalRecv;
                _lastBytesSent = totalSent;
            }
            catch
            {
                NetworkUpKBs = 0;
                NetworkDownKBs = 0;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public void Dispose()
        {
            _timer.Stop();
            _cpuCounter?.Dispose();
        }

        private static bool IsFilterDriver(NetworkInterface ni)
        {
            var desc = ni.Description ?? ni.Name ?? "";
            return desc.Contains("WFP", StringComparison.OrdinalIgnoreCase)
                || desc.Contains("Miniport", StringComparison.OrdinalIgnoreCase)
                || desc.Contains("Filter Driver", StringComparison.OrdinalIgnoreCase)
                || desc.Contains("LightWeight Filter", StringComparison.OrdinalIgnoreCase)
                || desc.Contains("QoS Packet Scheduler", StringComparison.OrdinalIgnoreCase)
                || desc.Contains("Virtual WiFi", StringComparison.OrdinalIgnoreCase);
        }
    }
}
