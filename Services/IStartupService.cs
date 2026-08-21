namespace ClipDropPro.Services
{
    public interface IStartupService
    {
        bool IsStartupEnabled();
        void SetStartup(bool enable);
    }
}
