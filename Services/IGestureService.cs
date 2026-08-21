using System;

namespace ClipDropPro.Services
{
    public interface IGestureService
    {
        event EventHandler DoubleCtrlDetected;
        void Start();
        void Stop();
    }
}
