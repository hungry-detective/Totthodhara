namespace ClipDropPro.Services
{
    public class UpdateInfo
    {
        public string LatestVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public bool IsUpdateAvailable { get; set; }
    }

    public interface IUpdateService
    {
        Task<UpdateInfo> CheckForUpdateAsync();
        string GetCurrentVersion();
    }
}
