using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClipDropPro.Services
{
    public class UpdateService : IUpdateService
    {
        private const string GitHubOwner = "hungry-detective";
        private const string GitHubRepo = "Totthodhara";
        private const string ReleasesApiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
        // Atom feed has no rate limit (public, no auth needed)
        private const string ReleasesAtomUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases.atom";
        private const int MaxRetries = 3;
        private const int BufferSize = 65536; // 64KB for faster downloads

        private static readonly HttpClient _httpClient;
        private static readonly string _currentVersion;
        private static readonly string _bakDir;
        private static readonly string _cacheDir;
        private static DateTime _lastCheckTime = DateTime.MinValue;
        private static UpdateInfo _cachedInfo;

        private string _pendingUpdateDir = "";
        private string _pendingExeName = "";

        static UpdateService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Totthodhara-Updater");
            _currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            _bakDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "update_backup");
            _cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "update_cache");
            Directory.CreateDirectory(_cacheDir);
            LoadCachedResult();
        }

        public string GetCurrentVersion() => _currentVersion;

        public bool HasPendingUpdate
        {
            get
            {
                var marker = Path.Combine(_bakDir, "pending_update.marker");
                return File.Exists(marker);
            }
        }

        public void MarkUpdateSucceeded()
        {
            try
            {
                var marker = Path.Combine(_bakDir, "pending_update.marker");
                if (File.Exists(marker)) File.Delete(marker);
                // Clean up backup after successful launch
                if (Directory.Exists(_bakDir))
                {
                    foreach (var f in Directory.GetFiles(_bakDir))
                        try { File.Delete(f); } catch { }
                    try { Directory.Delete(_bakDir); } catch { }
                }
                Logger.Write("[UpdateService] Update verified as successful, backup cleaned");
            }
            catch (Exception ex) { Logger.Write($"[UpdateService] MarkUpdateSucceeded cleanup error: {ex.Message}"); }
        }

        public void RollbackIfFailed()
        {
            try
            {
                var marker = Path.Combine(_bakDir, "pending_update.marker");
                if (!File.Exists(marker)) return;

                var destDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var bakFile = Path.Combine(_bakDir, "Totthodhara.exe.bak");
                var destExe = Path.Combine(destDir, "Totthodhara.exe");

                if (File.Exists(bakFile))
                {
                    File.Copy(bakFile, destExe, overwrite: true);
                    Logger.Write("[UpdateService] ROLLBACK: Restored Totthodhara.exe from backup");
                }

                // Clean up marker so we don't rollback again
                File.Delete(marker);
            }
            catch (Exception ex) { Logger.Write($"[UpdateService] Rollback failed: {ex.Message}"); }
        }

        public async Task<UpdateInfo> CheckForUpdateAsync()
        {
            // Return cached result if less than 1 hour old (avoids hitting network).
            // Only reuse the cache when it says an update IS available: a "no update"
            // result cached minutes ago can be obsolete the instant a new release lands,
            // so a cached "up to date" must always fall through to a fresh live check.
            if (_cachedInfo != null && (DateTime.UtcNow - _lastCheckTime) < TimeSpan.FromHours(1))
            {
                if (_cachedInfo.IsUpdateAvailable && IsNewerVersion(_cachedInfo.LatestVersion, _currentVersion))
                {
                    Logger.Write($"[UpdateService] Returning cached update info (age: {(DateTime.UtcNow - _lastCheckTime).TotalMinutes:F0} min)");
                    return _cachedInfo;
                }
                // Cache says "no update" or is stale (e.g. current caught up to cached latest):
                // re-verify live instead of trusting a potentially outdated cache.
                Logger.Write($"[UpdateService] Cached result weak/stale (latest={_cachedInfo.LatestVersion}, update={_cachedInfo.IsUpdateAvailable}) - re-checking live");
            }

            // Try GitHub Atom feed first (no rate limit), fall back to API
            try
            {
                var atomInfo = await TryFetchFromAtomAsync();
                if (atomInfo != null)
                {
                    _cachedInfo = atomInfo;
                    _lastCheckTime = DateTime.UtcNow;
                    SaveCachedResult(atomInfo);
                    return atomInfo;
                }
            }
            catch (Exception ex)
            {
                Logger.Write($"[UpdateService] Atom feed failed: {ex.Message}");
            }

            // Fallback to API
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var response = await _httpClient.GetAsync(ReleasesApiUrl, cts.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    Logger.Write("[UpdateService] GitHub API rate limit hit (403)");
                    // Never report a stale "no update" cache as "up to date" — surface an
                    // update-result if we actually have one cached, otherwise report failure.
                    if (_cachedInfo != null && _cachedInfo.IsUpdateAvailable && !string.IsNullOrEmpty(_cachedInfo.DownloadUrl))
                    {
                        _lastCheckTime = DateTime.UtcNow;
                        return _cachedInfo;
                    }
                    return new UpdateInfo
                    {
                        IsUpdateAvailable = false,
                        CurrentVersion = _currentVersion,
                        ErrorMessage = "GitHub API rate limit exceeded. Please wait an hour and try again."
                    };
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string latestVersionRaw = root.TryGetProperty("tag_name", out var tagProp)
                    ? tagProp.GetString()?.TrimStart('v', 'V') ?? ""
                    : "";

                string latestVersion = latestVersionRaw.Split('-', '+')[0].Trim();

                string releaseNotes = root.TryGetProperty("body", out var bodyProp)
                    ? bodyProp.GetString() ?? ""
                    : "";

                DateTime? publishedAt = null;
                if (root.TryGetProperty("published_at", out var pubProp) &&
                    DateTime.TryParse(pubProp.GetString(), out var pub))
                {
                    publishedAt = pub.ToUniversalTime();
                }

                string downloadUrl = SelectBestAsset(root);
                string sha256 = ExtractSha256FromNotes(releaseNotes);

                if (string.IsNullOrEmpty(sha256))
                    sha256 = await FindSha256Asset(root, downloadUrl, cts.Token);

                bool isUpdateAvailable = IsNewerVersion(latestVersion, _currentVersion);

                Logger.Write($"[UpdateService] API: Current: {_currentVersion}, Latest: {latestVersion}, Update available: {isUpdateAvailable}");

                var info = new UpdateInfo
                {
                    LatestVersion = latestVersion,
                    CurrentVersion = _currentVersion,
                    DownloadUrl = downloadUrl,
                    ReleaseNotes = releaseNotes,
                    IsUpdateAvailable = isUpdateAvailable,
                    ExpectedSha256 = sha256,
                    PublishedAt = publishedAt
                };

                _cachedInfo = info;
                _lastCheckTime = DateTime.UtcNow;
                SaveCachedResult(info);
                return info;
            }
            catch (Exception ex)
            {
                Logger.Write($"[UpdateService] Check failed: {ex.Message}");
                // Never silently report a stale "no update" cache as "up to date".
                // Only reuse the cache if it actually has an actionable update still pending.
                if (_cachedInfo != null && _cachedInfo.IsUpdateAvailable && !string.IsNullOrEmpty(_cachedInfo.DownloadUrl))
                {
                    Logger.Write("[UpdateService] Returning cached update result due to error");
                    return _cachedInfo;
                }
                return new UpdateInfo
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = _currentVersion,
                    ErrorMessage = string.IsNullOrWhiteSpace(ex.Message)
                        ? "Network error while checking for updates."
                        : ex.Message
                };
            }
        }

        private async Task<UpdateInfo> TryFetchFromAtomAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var response = await _httpClient.GetAsync(ReleasesAtomUrl, cts.Token);
                response.EnsureSuccessStatusCode();

                var xml = await response.Content.ReadAsStringAsync(cts.Token);
                if (string.IsNullOrWhiteSpace(xml)) return null;

                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xml);

                var nsmgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("atom", "http://www.w3.org/2005/Atom");

                // Get first <entry> which is the latest release
                var entry = doc.SelectSingleNode("//atom:entry", nsmgr);
                if (entry == null) return null;

                // Title format: "Release v1.2.0" — extract version
                var titleNode = entry.SelectSingleNode("atom:title", nsmgr);
                string title = titleNode?.InnerText ?? "";
                string latestVersion = "";
                var match = System.Text.RegularExpressions.Regex.Match(title, @"v?(\d+(?:\.\d+){0,3})");
                if (match.Success) latestVersion = match.Groups[1].Value;
                if (string.IsNullOrEmpty(latestVersion)) return null;

                // Published date
                DateTime? publishedAt = null;
                var updatedNode = entry.SelectSingleNode("atom:updated", nsmgr);
                if (updatedNode != null && DateTime.TryParse(updatedNode.InnerText, out var pub))
                    publishedAt = pub.ToUniversalTime();

                // Link to release page (for downloads if needed)
                string releasePageUrl = "";
                var linkNode = entry.SelectSingleNode("atom:link", nsmgr);
                if (linkNode != null)
                {
                    string href = linkNode.Attributes?["href"]?.Value ?? "";
                    // Convert relative URL to absolute
                    if (href.StartsWith("/"))
                        releasePageUrl = $"https://github.com{href}";
                    else if (href.StartsWith("http"))
                        releasePageUrl = href;
                    else
                        releasePageUrl = href;
                }

                // Use the tag-based asset URL pattern (GitHub provides direct download links)
                string downloadUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/v{latestVersion}/";
                string[] assetCandidates = Array.Empty<string>();

                // Try to fetch the release page HTML and extract actual asset download URLs
                if (!string.IsNullOrEmpty(releasePageUrl))
                {
                    try
                    {
                        Logger.Write($"[UpdateService] Fetching release page: {releasePageUrl}");
                        var pageResp = await _httpClient.GetAsync(releasePageUrl, cts.Token);
                        if (pageResp.IsSuccessStatusCode)
                        {
                            var html = await pageResp.Content.ReadAsStringAsync(cts.Token);
                            // Find asset download URLs in either absolute or relative form
                            var patterns = new[] {
                                @"https://github\.com/" + System.Text.RegularExpressions.Regex.Escape(GitHubOwner) + "/" + System.Text.RegularExpressions.Regex.Escape(GitHubRepo) + @"/releases/download/[^\s""]+",
                                @"href=""(/[^""]*releases/download/[^""]+)"""
                            };
                            var found = new System.Collections.Generic.List<string>();
                            foreach (var pattern in patterns)
                            {
                                var matches = System.Text.RegularExpressions.Regex.Matches(html, pattern);
                                foreach (System.Text.RegularExpressions.Match m in matches)
                                {
                                    string url2 = m.Value;
                                    // Extract just the URL from href="..." form
                                    var hrefMatch = System.Text.RegularExpressions.Regex.Match(url2, @"href=""([^""]+)""");
                                    if (hrefMatch.Success)
                                        url2 = "https://github.com" + hrefMatch.Groups[1].Value;
                                    url2 = url2.Replace("&amp;", "&");
                                    if (!found.Contains(url2) && !url2.Contains(".sig") && !url2.Contains(".sha256"))
                                        found.Add(url2);
                                }
                            }
                            if (found.Count > 0)
                            {
                                assetCandidates = found.ToArray();
                                downloadUrl = found[0];
                                Logger.Write($"[UpdateService] Found {found.Count} asset URLs from release page");
                            }
                            else
                            {
                                Logger.Write("[UpdateService] No asset URLs found in release page HTML");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Write($"[UpdateService] Release page fetch failed: {ex.Message}");
                    }
                }

                // Fallback asset candidates if HTML parsing failed
                if (assetCandidates.Length == 0)
                {
                    assetCandidates = new[] {
                        // Most common portable exe variant
                        $"Totthodhara-v{latestVersion}-portable.exe",
                        $"Totthodhara-v{latestVersion}-win-x86.exe",
                        $"Totthodhara-v{latestVersion}-win-x86.zip",
                        $"Totthodhara-{latestVersion}-portable.exe",
                        $"Totthodhara-{latestVersion}-win-x86.exe",
                        $"Totthodhara_{latestVersion}_portable.exe",
                        $"Totthodhara_{latestVersion}_win-x86.exe",
                        $"Totthodhara_{latestVersion}_win-x86.zip",
                        $"Totthodhara-v{latestVersion}.zip",
                        $"Totthodhara-{latestVersion}-win-x86.zip",
                        $"Totthodhara-{latestVersion}.zip",
                        $"Totthodhara.exe"
                    };
                    downloadUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/v{latestVersion}/{assetCandidates[0]}";
                }

                bool isUpdateAvailable = IsNewerVersion(latestVersion, _currentVersion);

                Logger.Write($"[UpdateService] Atom: Current: {_currentVersion}, Latest: {latestVersion}, Update available: {isUpdateAvailable}");

                return new UpdateInfo
                {
                    LatestVersion = latestVersion,
                    CurrentVersion = _currentVersion,
                    DownloadUrl = downloadUrl,
                    ReleaseNotes = $"Latest release: {title}\n\nDownload from: {releasePageUrl}",
                    IsUpdateAvailable = isUpdateAvailable,
                    PublishedAt = publishedAt,
                    AssetCandidates = assetCandidates,
                    ReleasePageUrl = releasePageUrl
                };
            }
            catch (Exception ex)
            {
                Logger.Write($"[UpdateService] Atom feed parse failed: {ex.Message}");
                return null;
            }
        }

        private static void LoadCachedResult()
        {
            try
            {
                var path = Path.Combine(_cacheDir, "last_check.json");
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return;
                _cachedInfo = System.Text.Json.JsonSerializer.Deserialize<UpdateInfo>(json);
                var tsPath = Path.Combine(_cacheDir, "last_check_time.txt");
                if (File.Exists(tsPath) && DateTime.TryParse(File.ReadAllText(tsPath), out var ts))
                    _lastCheckTime = ts.ToUniversalTime();
            }
            catch { }
        }

        private static void SaveCachedResult(UpdateInfo info)
        {
            try
            {
                var path = Path.Combine(_cacheDir, "last_check.json");
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(info));
                File.WriteAllText(Path.Combine(_cacheDir, "last_check_time.txt"), DateTime.UtcNow.ToString("O"));
            }
            catch { }
        }

        private static bool IsNewerVersion(string latest, string current)
        {
            if (string.IsNullOrEmpty(latest)) return false;

            // Parse with up to 4 segments (1.2 vs 1.2.0 vs 1.2.0.1)
            int[] latestParts = ParseVersion(latest);
            int[] currentParts = ParseVersion(current);

            for (int i = 0; i < 4; i++)
            {
                int l = i < latestParts.Length ? latestParts[i] : 0;
                int c = i < currentParts.Length ? currentParts[i] : 0;
                if (l > c) return true;
                if (l < c) return false;
            }
            return false; // Equal
        }

        private static int[] ParseVersion(string version)
        {
            var result = new System.Collections.Generic.List<int>();
            foreach (var part in version.Split('.'))
            {
                if (int.TryParse(part, out int n))
                    result.Add(n);
                else
                    result.Add(0);
            }
            return result.ToArray();
        }

        private static string ExtractSha256FromNotes(string notes)
        {
            if (string.IsNullOrEmpty(notes)) return "";
            // Look for SHA256: <hash> or sha256: <hash> in release notes
            var lines = notes.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                {
                    var hash = trimmed.Substring(trimmed.IndexOf(':') + 1).Trim();
                    // Strip markdown code-span backticks around the hash
                    hash = hash.Trim('`', '*', ' ', '\t');
                    if (hash.Length == 64 && hash.All("0123456789abcdefABCDEF".Contains))
                        return hash.ToLowerInvariant();
                }
            }
            return "";
        }

        private async Task<string> FindSha256Asset(JsonElement root, string downloadUrl, CancellationToken ct)
        {
            try
            {
                if (!root.TryGetProperty("assets", out var assets)) return "";
                var baseName = Path.GetFileNameWithoutExtension(downloadUrl);
                foreach (var asset in assets.EnumerateArray())
                {
                    if (!asset.TryGetProperty("browser_download_url", out var urlProp)) continue;
                    var url = urlProp.GetString() ?? "";
                    if (url.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) && url.Contains(baseName, StringComparison.OrdinalIgnoreCase))
                    {
                        var resp = await _httpClient.GetAsync(url, ct);
                        if (resp.IsSuccessStatusCode)
                        {
                            var content = (await resp.Content.ReadAsStringAsync(ct)).Trim();
                            var hash = content.Split(' ', '\t')[0].Trim();
                            if (hash.Length == 64) return hash.ToLowerInvariant();
                        }
                    }
                }
            }
            catch { }
            return "";
        }

        private static string SelectBestAsset(JsonElement root)
        {
            if (!root.TryGetProperty("assets", out var assets) || assets.GetArrayLength() == 0)
                return "";

            string firstUrl = "";
            string zipUrl = "";
            string exeUrl = "";
            string preferredZip = "";

            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("browser_download_url", out var urlProp)) continue;
                var url = urlProp.GetString() ?? "";
                if (string.IsNullOrEmpty(url)) continue;
                if (string.IsNullOrEmpty(firstUrl)) firstUrl = url;

                var lower = url.ToLowerInvariant();
                if (lower.EndsWith(".zip"))
                {
                    if (string.IsNullOrEmpty(zipUrl)) zipUrl = url;
                    if (lower.Contains("win-x86") || lower.Contains("win-x64") || lower.Contains("portable") || lower.Contains("windows"))
                    {
                        if (string.IsNullOrEmpty(preferredZip)) preferredZip = url;
                    }
                }
                else if (lower.EndsWith(".exe"))
                {
                    if (string.IsNullOrEmpty(exeUrl)) exeUrl = url;
                }
            }

            if (!string.IsNullOrEmpty(preferredZip)) return preferredZip;
            if (!string.IsNullOrEmpty(zipUrl)) return zipUrl;
            if (!string.IsNullOrEmpty(exeUrl)) return exeUrl;
            return firstUrl;
        }

        public async Task<bool> DownloadAndInstallAsync(UpdateInfo info, IProgress<double> progress = null, CancellationToken cancellationToken = default, IProgress<string> status = null)
        {
            void Report(string msg) { status?.Report(msg); Logger.Write($"[UpdateService] {msg}"); }
            if (info == null || string.IsNullOrEmpty(info.DownloadUrl))
            {
                Logger.Write("[UpdateService] DownloadAndInstall: no download URL");
                Report("No download URL available");
                return false;
            }

            string tempRoot = Path.Combine(Path.GetTempPath(), "TotthodharaUpdate", Guid.NewGuid().ToString("N"));
            string downloadPath = "";
            string extractedDir = "";
            try
            {
                Directory.CreateDirectory(tempRoot);

                // Try the primary URL first, then asset candidates
                var urlsToTry = new System.Collections.Generic.List<string> { info.DownloadUrl };
                if (info.AssetCandidates != null)
                {
                    foreach (var candidate in info.AssetCandidates)
                    {
                        // If candidate is a full URL (from HTML scrape), use directly
                        // Otherwise treat as asset name and prepend base URL
                        string fullUrl = candidate.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? candidate
                            : $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/v{info.LatestVersion}/{candidate}";
                        if (!urlsToTry.Contains(fullUrl))
                            urlsToTry.Add(fullUrl);
                    }
                }

                Report($"Trying {urlsToTry.Count} download source(s)...");
                Logger.Write($"[UpdateService] URLs to try ({urlsToTry.Count}):");
                foreach (var u in urlsToTry) Logger.Write($"  - {u}");

                bool downloaded = false;
                string fileName = "";
                foreach (var url in urlsToTry)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    fileName = GetFileNameFromUrl(url);
                    if (string.IsNullOrEmpty(fileName))
                        fileName = url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "Totthodhara.exe" : "update.zip";

                    downloadPath = Path.Combine(tempRoot, fileName);
                    Report($"Downloading: {fileName}");
                    Logger.Write($"[UpdateService] Trying download: {url}");
                    downloaded = await DownloadWithRetryAndResume(url, downloadPath, info.ExpectedSha256, progress, cancellationToken, status);
                    if (downloaded)
                    {
                        Report("Download complete");
                        break;
                    }
                    Report($"Source {fileName} unavailable, trying next...");
                }

                if (!downloaded)
                {
                    TryCleanup(tempRoot);
                    return false;
                }

                bool isZip = downloadPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                string sourcePath;

                if (isZip)
                {
                    extractedDir = Path.Combine(tempRoot, "extracted");
                    Directory.CreateDirectory(extractedDir);
                    ZipFile.ExtractToDirectory(downloadPath, extractedDir, overwriteFiles: true);
                    Logger.Write($"[UpdateService] Extracted to {extractedDir}");
                    sourcePath = ResolveExtractedSource(extractedDir);
                }
                else
                {
                    sourcePath = downloadPath;
                    extractedDir = null;
                }

                // Backup current EXE before update
                BackupCurrentExe();

                // Write rollback marker
                Directory.CreateDirectory(_bakDir);
                File.WriteAllText(Path.Combine(_bakDir, "pending_update.marker"), _currentVersion);

                string scriptPath = Path.Combine(tempRoot, "update.ps1");
                string destDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(destDir, "Totthodhara.exe");
                string currentExeName = Path.GetFileName(currentExePath);
                if (string.IsNullOrEmpty(currentExeName)) currentExeName = "Totthodhara.exe";
                int currentPid = Process.GetCurrentProcess().Id;

                string script = isZip
                    ? BuildZipUpdateScript(currentPid, sourcePath, destDir, currentExeName)
                    : BuildExeUpdateScript(currentPid, sourcePath, destDir, currentExeName);

                await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
                Logger.Write("[UpdateService] Updater launched with backup+rollback protection");

                await Task.Delay(500, cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                Logger.Write("[UpdateService] Download cancelled");
                TryCleanup(tempRoot);
                return false;
            }
            catch (Exception ex)
            {
                Logger.Write($"[UpdateService] DownloadAndInstall failed: {ex}");
                TryCleanup(tempRoot);
                return false;
            }
        }

        private async Task<bool> DownloadWithRetryAndResume(string url, string downloadPath, string expectedSha256, IProgress<double> progress, CancellationToken ct, IProgress<string> status = null)
        {
            void Report(string msg) { status?.Report(msg); Logger.Write($"[UpdateService] {msg}"); }
            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                if (ct.IsCancellationRequested) return false;

                if (attempt > 0)
                {
                    int delayMs = (int)Math.Pow(2, attempt) * 2000; // 2s, 4s, 8s
                    Logger.Write($"[UpdateService] Retry {attempt}/{MaxRetries} after {delayMs}ms delay");
                    progress?.Report(0);
                    await Task.Delay(delayMs, ct);
                }

                try
                {
                    // Quick HEAD probe to validate URL is reachable before downloading
                    Report($"Checking: {Path.GetFileName(url)}");
                    using (var probe = new HttpRequestMessage(HttpMethod.Head, url))
                    using (var probeResp = await _httpClient.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, ct))
                    {
                        if (!probeResp.IsSuccessStatusCode)
                        {
                            Report($"  HTTP {(int)probeResp.StatusCode} — skipping");
                            Logger.Write($"[UpdateService] HEAD probe failed for {url}: HTTP {(int)probeResp.StatusCode}");
                            return false; // Skip this URL entirely (no retries on 404)
                        }
                        Logger.Write($"[UpdateService] HEAD probe OK for {url} ({probeResp.Content.Headers.ContentLength ?? -1} bytes)");
                    }
                    long existingBytes = 0;
                    if (attempt > 0 && File.Exists(downloadPath))
                    {
                        existingBytes = new FileInfo(downloadPath).Length;
                    }

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (existingBytes > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(existingBytes, null);
                        Logger.Write($"[UpdateService] Resuming download from byte {existingBytes}");
                    }

                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.Write($"[UpdateService] HTTP {(int)response.StatusCode} on attempt {attempt + 1}");
                        continue;
                    }

                    var totalBytes = response.Content.Headers.ContentLength;
                    long totalExpected = totalBytes.HasValue ? existingBytes + totalBytes.Value : 0;

                    // If server doesn't support range, start fresh
                    if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                    {
                        existingBytes = 0;
                        totalExpected = totalBytes ?? 0;
                    }

                    using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                    var fileMode = existingBytes > 0 ? FileMode.Append : FileMode.Create;
                    using var fileStream = new FileStream(downloadPath, fileMode, FileAccess.Write, FileShare.None, BufferSize, true);

                    var buffer = new byte[BufferSize];
                    long totalRead = existingBytes;
                    int read;
                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read, ct);
                        totalRead += read;
                        if (totalExpected > 0 && progress != null)
                        {
                            double pct = (double)totalRead / totalExpected * 100.0;
                            progress.Report(Math.Min(pct, 100.0));
                        }
                    }

                    progress?.Report(100.0);
                    Logger.Write($"[UpdateService] Download complete: {downloadPath} ({totalRead} bytes)");

                    // Verify SHA256 if available
                    if (!string.IsNullOrEmpty(expectedSha256))
                    {
                        string actualHash = await ComputeFileHashAsync(downloadPath, ct);
                        if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Write($"[UpdateService] SHA256 mismatch! Expected: {expectedSha256}, Got: {actualHash}");
                            File.Delete(downloadPath);
                            continue; // Retry
                        }
                        Logger.Write($"[UpdateService] SHA256 verified OK");
                    }

                    return true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Logger.Write($"[UpdateService] Download attempt {attempt + 1} error: {ex.Message}");
                }
            }
            Logger.Write($"[UpdateService] All {MaxRetries + 1} download attempts failed");
            return false;
        }

        private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken ct)
        {
            using var sha256 = SHA256.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, true);
            var hash = await sha256.ComputeHashAsync(stream, ct);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private void BackupCurrentExe()
        {
            try
            {
                Directory.CreateDirectory(_bakDir);
                string destDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string exePath = Path.Combine(destDir, "Totthodhara.exe");
                string bakPath = Path.Combine(_bakDir, "Totthodhara.exe.bak");
                if (File.Exists(exePath))
                {
                    File.Copy(exePath, bakPath, overwrite: true);
                    Logger.Write($"[UpdateService] Backed up current EXE to {bakPath}");
                }
            }
            catch (Exception ex) { Logger.Write($"[UpdateService] Backup failed: {ex.Message}"); }
        }

        private static string GetFileNameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var name = Path.GetFileName(uri.LocalPath);
                if (name.Contains("?")) name = name.Substring(0, name.IndexOf('?'));
                return name;
            }
            catch { return ""; }
        }

        private static string ResolveExtractedSource(string extractedDir)
        {
            try
            {
                var files = Directory.GetFiles(extractedDir);
                var dirs = Directory.GetDirectories(extractedDir);
                if (files.Length == 0 && dirs.Length == 1)
                    return dirs[0];
                return extractedDir;
            }
            catch { return extractedDir; }
        }

        private static string BuildZipUpdateScript(int pidToWait, string source, string dest, string exeName)
        {
            string psSource = source.Replace("'", "''");
            string psDest = dest.Replace("'", "''");
            string psExe = exeName.Replace("'", "''");
            return $@"
$ErrorActionPreference = 'Stop'
$pidToWait = {pidToWait}
$source = '{psSource}'
$dest = '{psDest}'
$exeName = '{psExe}'
$logFile = Join-Path $env:TEMP 'Totthodhara_update.log'
function Log($m) {{ Add-Content -Path $logFile -Value ""[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $m"" }}

Log ""Updater started. Waiting for PID $pidToWait to exit. Source=$source Dest=$dest""
for ($i=0; $i -lt 40; $i++) {{
    $p = Get-Process -Id $pidToWait -ErrorAction SilentlyContinue
    if (-not $p) {{ break }}
    Start-Sleep -Milliseconds 500
}}
Start-Sleep -Seconds 2

# Retry copy up to 3 times
$copySuccess = $false
for ($retry=0; $retry -lt 3; $retry++) {{
    Log ""Copy attempt $($retry+1)/3...""
    try {{
        Copy-Item -Path (Join-Path $source '*') -Destination $dest -Recurse -Force -ErrorAction Stop
        $copySuccess = $true
        Log ""Copy succeeded on attempt $($retry+1)""
        break
    }} catch {{
        Log ""Copy attempt $($retry+1) failed: $_""
        Start-Sleep -Seconds 2
    }}
}}

if (-not $copySuccess) {{
    Log ""All copy attempts failed, trying robocopy fallback...""
    try {{
        robocopy ""$source"" ""$dest"" /E /R:3 /W:3 /NFL /NDL /NJH | Out-Null
        $copySuccess = $true
        Log ""Robocopy fallback succeeded""
    }} catch {{
        Log ""Robocopy also failed: $_""
    }}
}}

Start-Sleep -Seconds 1
$exePath = Join-Path $dest $exeName
if (-not (Test-Path $exePath)) {{
    $found = Get-ChildItem -Path $dest -Filter *.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($found) {{ $exePath = $found.FullName }}
}}
Log ""Launching $exePath""
if (Test-Path $exePath) {{
    Start-Process -FilePath $exePath -ErrorAction SilentlyContinue
    Log ""Launched""
}} else {{
    Log ""Exe not found, launch skipped""
}}
";
        }

        private static string BuildExeUpdateScript(int pidToWait, string downloadedExe, string dest, string exeName)
        {
            string psDownloaded = downloadedExe.Replace("'", "''");
            string psDest = dest.Replace("'", "''");
            string psExe = exeName.Replace("'", "''");
            return $@"
$ErrorActionPreference = 'Stop'
$pidToWait = {pidToWait}
$downloaded = '{psDownloaded}'
$dest = '{psDest}'
$exeName = '{psExe}'
$logFile = Join-Path $env:TEMP 'Totthodhara_update.log'
function Log($m) {{ Add-Content -Path $logFile -Value ""[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $m"" }}

Log ""Updater (EXE mode) started. Waiting for PID $pidToWait""
for ($i=0; $i -lt 40; $i++) {{
    $p = Get-Process -Id $pidToWait -ErrorAction SilentlyContinue
    if (-not $p) {{ break }}
    Start-Sleep -Milliseconds 500
}}
Start-Sleep -Seconds 2
$destExe = Join-Path $dest $exeName

# Retry copy up to 3 times
for ($retry=0; $retry -lt 3; $retry++) {{
    Log ""Copy attempt $($retry+1)/3...""
    try {{
        Copy-Item -Path $downloaded -Destination $destExe -Force -ErrorAction Stop
        Log ""Copy succeeded on attempt $($retry+1)""
        break
    }} catch {{
        Log ""Copy attempt $($retry+1) failed: $_""
        Start-Sleep -Seconds 2
    }}
}}

Start-Sleep -Seconds 1
if (Test-Path $destExe) {{
    Log ""Launching $destExe""
    Start-Process -FilePath $destExe
}} else {{
    Log ""Dest exe not found""
}}
";
        }

        private static void TryCleanup(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
    }
}
