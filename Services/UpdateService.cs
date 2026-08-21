using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace ClipDropPro.Services
{
    public class UpdateService : IUpdateService
    {
        private const string GitHubOwner = "hungry-detective";
        private const string GitHubRepo = "Totthodhara";
        private const string ReleasesApiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly string _currentVersion;

        static UpdateService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Totthodhara-Updater");
            _currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        }

        public string GetCurrentVersion() => _currentVersion;

        public async Task<UpdateInfo> CheckForUpdateAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync(ReleasesApiUrl);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string latestVersion = root.TryGetProperty("tag_name", out var tagProp)
                    ? tagProp.GetString()?.TrimStart('v', 'V') ?? ""
                    : "";

                string releaseNotes = root.TryGetProperty("body", out var bodyProp)
                    ? bodyProp.GetString() ?? ""
                    : "";

                string downloadUrl = "";
                if (root.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                {
                    var firstAsset = assets[0];
                    if (firstAsset.TryGetProperty("browser_download_url", out var urlProp))
                    {
                        downloadUrl = urlProp.GetString() ?? "";
                    }
                }

                bool isUpdateAvailable = !string.IsNullOrEmpty(latestVersion) &&
                                         Version.TryParse(latestVersion, out var latest) &&
                                         Version.TryParse(_currentVersion, out var current) &&
                                         latest > current;

                return new UpdateInfo
                {
                    LatestVersion = latestVersion,
                    DownloadUrl = downloadUrl,
                    ReleaseNotes = releaseNotes,
                    IsUpdateAvailable = isUpdateAvailable
                };
            }
            catch (Exception ex)
            {
                Logger.Write($"[UpdateService] Check failed: {ex.Message}");
                return new UpdateInfo { IsUpdateAvailable = false };
            }
        }
    }
}
