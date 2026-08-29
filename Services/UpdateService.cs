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
        private const int MaxRetries = 3;
        private const int BufferSize = 65536; // 64KB for faster downloads

        private static readonly HttpClient _httpClient;
        private static readonly string _currentVersion;
        private static readonly string _bakDir;

        private string _pendingUpdateDir = "";
        private string _pendingExeName = "";

        static UpdateService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Totthodhara-Updater");
            _currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            _bakDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "update_backup");
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
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var response = await _httpClient.GetAsync(ReleasesApiUrl, cts.Token);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string latestVersion = root.TryGetProperty("tag_name", out var tagProp)
                    ? tagProp.GetString()?.TrimStart('v', 'V') ?? ""
                    : "";

                string releaseNotes = root.TryGetProperty("body", out var bodyProp)
                    ? bodyProp.GetString() ?? ""
                    : "";

                string downloadUrl = SelectBestAsset(root);
                string sha256 = ExtractSha256FromNotes(releaseNotes);

                // Also check for .sha256 asset
                if (string.IsNullOrEmpty(sha256))
                    sha256 = await FindSha256Asset(root, downloadUrl, cts.Token);

                bool isUpdateAvailable = !string.IsNullOrEmpty(latestVersion) &&
                                         Version.TryParse(latestVersion, out var latest) &&
                                         Version.TryParse(_currentVersion, out var current) &&
                                         latest > current;

                return new UpdateInfo
                {
                    LatestVersion = latestVersion,
                    DownloadUrl = downloadUrl,
                    ReleaseNotes = releaseNotes,
                    IsUpdateAvailable = isUpdateAvailable,
                    ExpectedSha256 = sha256
                };
            }
            catch (Exception ex)
            {
                Logger.Write($"[UpdateService] Check failed: {ex.Message}");
                return new UpdateInfo { IsUpdateAvailable = false };
            }
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

        public async Task<bool> DownloadAndInstallAsync(UpdateInfo info, IProgress<double> progress = null, CancellationToken cancellationToken = default)
        {
            if (info == null || string.IsNullOrEmpty(info.DownloadUrl))
            {
                Logger.Write("[UpdateService] DownloadAndInstall: no download URL");
                return false;
            }

            string tempRoot = Path.Combine(Path.GetTempPath(), "TotthodharaUpdate", Guid.NewGuid().ToString("N"));
            string downloadPath = "";
            string extractedDir = "";
            try
            {
                Directory.CreateDirectory(tempRoot);

                string fileName = GetFileNameFromUrl(info.DownloadUrl);
                if (string.IsNullOrEmpty(fileName))
                    fileName = info.DownloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "Totthodhara.exe" : "update.zip";

                downloadPath = Path.Combine(tempRoot, fileName);

                // Download with retry + resume
                bool downloaded = await DownloadWithRetryAndResume(info.DownloadUrl, downloadPath, info.ExpectedSha256, progress, cancellationToken);
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

        private async Task<bool> DownloadWithRetryAndResume(string url, string downloadPath, string expectedSha256, IProgress<double> progress, CancellationToken ct)
        {
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
