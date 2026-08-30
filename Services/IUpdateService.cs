using System;
using System.Threading;

namespace ClipDropPro.Services
{
    public class UpdateInfo
    {
        public string LatestVersion { get; set; } = "";
        public string CurrentVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string[] AssetCandidates { get; set; } = System.Array.Empty<string>();
        public string ReleasePageUrl { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public bool IsUpdateAvailable { get; set; }
        public string ExpectedSha256 { get; set; } = "";
        public DateTime? PublishedAt { get; set; }
        public string ErrorMessage { get; set; } = "";
    }

    public interface IUpdateService
    {
        Task<UpdateInfo> CheckForUpdateAsync();
        string GetCurrentVersion();
        Task<bool> DownloadAndInstallAsync(UpdateInfo info, IProgress<double> progress = null, CancellationToken cancellationToken = default, IProgress<string> status = null);
        bool HasPendingUpdate { get; }
        void MarkUpdateSucceeded();
        void RollbackIfFailed();
    }
}
