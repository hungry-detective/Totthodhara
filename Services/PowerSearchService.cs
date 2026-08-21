using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace ClipDropPro.Services
{
    public class LauncherResult
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string ExecutePath { get; set; } = "";
        public string FilePath { get; set; } = "";
        public bool IsAdmin { get; set; }
        public BitmapSource? Icon { get; set; }
        public string FileSize { get; set; } = "";
        public string FileDate { get; set; } = "";
    }

    public static class PowerSearchService
    {
        private static readonly Dictionary<string, string> _uwpApps = new(StringComparer.OrdinalIgnoreCase);
        private static bool _appsLoaded;

        public static List<LauncherResult> Search(string query)
        {
            var results = new List<LauncherResult>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            var q = query.Trim();

            // 1. Calculator
            if (IsMathExpression(q))
            {
                var calcResult = EvaluateCalculator(q);
                if (calcResult != null)
                {
                    results.Add(new LauncherResult
                    {
                        Name = calcResult,
                        Description = $"{q} = {calcResult}",
                        Category = "Calculator"
                    });
                }
            }

            // 2. Run command
            if (q.StartsWith('>'))
            {
                var cmd = q.Substring(1).Trim();
                if (!string.IsNullOrEmpty(cmd))
                {
                    results.Add(new LauncherResult
                    {
                        Name = cmd,
                        Description = "Run command",
                        Category = "Run command",
                        ExecutePath = cmd,
                        IsAdmin = true
                    });
                }
            }

            // 3. Apps
            EnsureAppsLoaded();
            var matchedApps = SearchApps(q);
            foreach (var app in matchedApps.Take(6))
            {
                var icon = ExtractIconFromFile(app.Path);
                results.Add(new LauncherResult
                {
                    Name = app.Name,
                    Description = "Application",
                    Category = "Application",
                    ExecutePath = app.Path,
                    Icon = icon
                });
            }

            // 4. Everything (es.exe search)
            var files = SearchEverythingCLI(q);
            foreach (var f in files.Take(8))
            {
                var icon = ExtractIconFromFile(f.Path);
                var fi = new FileInfo(f.Path);
                var dirInfo = new DirectoryInfo(Path.GetDirectoryName(f.Path) ?? "");
                results.Add(new LauncherResult
                {
                    Name = Path.GetFileName(f.Path),
                    Description = FormatSize(fi.Exists ? fi.Length : 0) + "  " + dirInfo.Name,
                    Category = "Everything",
                    ExecutePath = f.Path,
                    FilePath = f.Path,
                    Icon = icon,
                    FileSize = fi.Exists ? FormatSize(fi.Length) : "",
                    FileDate = fi.Exists ? fi.LastWriteTime.ToString("MMM dd, yyyy") : ""
                });
            }

            return results;
        }

        public static void Execute(LauncherResult result)
        {
            if (result == null) return;

            if (result.Category == "Calculator")
            {
                System.Windows.Clipboard.SetText(result.Name);
                return;
            }

            if (result.Category == "Run command")
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = result.ExecutePath,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    Process.Start(psi);
                }
                catch { }
                return;
            }

            if (!string.IsNullOrEmpty(result.ExecutePath))
            {
                try
                {
                    if (Directory.Exists(result.ExecutePath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{result.ExecutePath}\"",
                            UseShellExecute = true
                        });
                    }
                    else if (File.Exists(result.ExecutePath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{result.ExecutePath}\"",
                            UseShellExecute = true
                        });
                    }
                }
                catch { }
            }
        }

        #region Calculator

        private static bool IsMathExpression(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            foreach (var c in input)
            {
                if (!char.IsDigit(c) && c != '+' && c != '-' && c != '*' && c != '/'
                    && c != '^' && c != '(' && c != ')' && c != '.' && c != ' '
                    && c != '%' && c != '√')
                    return false;
            }
            return input.Contains('+') || input.Contains('-') || input.Contains('*')
                || input.Contains('/') || input.Contains('^') || input.Contains('%')
                || input.Contains('√');
        }

        private static string? EvaluateCalculator(string expr)
        {
            try
            {
                var normalized = expr
                    .Replace("√", "sqrt")
                    .Replace("^", "**")
                    .Replace("%", "/100");

                var dt = new DataTable();
                var result = dt.Compute(normalized, "");
                if (result != null && result != DBNull.Value)
                {
                    var val = Convert.ToDouble(result);
                    if (val == Math.Floor(val) && Math.Abs(val) < 1e15)
                        return val.ToString("N0");
                    return val.ToString("G10");
                }
            }
            catch { }
            return null;
        }

        #endregion

        #region App Launcher

        private static void EnsureAppsLoaded()
        {
            if (_appsLoaded) return;
            _appsLoaded = true;

            try
            {
                var startMenu = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs");
                var userStartMenu = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs");

                LoadAppsFromFolder(startMenu);
                LoadAppsFromFolder(userStartMenu);
            }
            catch { }

            try
            {
                var paths = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs")
                };

                foreach (var basePath in paths)
                {
                    if (!Directory.Exists(basePath)) continue;
                    foreach (var exe in Directory.GetFiles(basePath, "*.exe", SearchOption.TopDirectoryOnly))
                    {
                        var name = Path.GetFileNameWithoutExtension(exe);
                        if (!_uwpApps.ContainsKey(name))
                            _uwpApps[name] = exe;
                    }
                    foreach (var dir in Directory.GetDirectories(basePath))
                    {
                        try
                        {
                            foreach (var exe in Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly))
                            {
                                var name = Path.GetFileNameWithoutExtension(exe);
                                if (!_uwpApps.ContainsKey(name))
                                    _uwpApps[name] = exe;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static void LoadAppsFromFolder(string folder)
        {
            if (!Directory.Exists(folder)) return;
            try
            {
                foreach (var lnk in Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileNameWithoutExtension(lnk);
                    if (!_uwpApps.ContainsKey(name))
                        _uwpApps[name] = lnk;
                }
            }
            catch { }
        }

        private static List<(string Name, string Path)> SearchApps(string query)
        {
            var q = query.ToLowerInvariant();
            var results = new List<(string Name, string Path)>();

            foreach (var kv in _uwpApps)
            {
                if (kv.Key.ToLowerInvariant().Contains(q))
                {
                    results.Add((kv.Key, kv.Value));
                    if (results.Count >= 10) break;
                }
            }

            return results;
        }

        #endregion

        #region Everything CLI

        private static string? _esPath;

        private static string GetEsPath()
        {
            if (_esPath != null && File.Exists(_esPath)) return _esPath;

            // Check multiple locations
            var candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "es.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Everything", "es.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Everything", "es.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Everything", "es.exe")
            };

            foreach (var c in candidates)
            {
                if (File.Exists(c))
                {
                    _esPath = c;
                    return _esPath;
                }
            }
            return "";
        }

        private static List<(string Name, string Path)> SearchEverythingCLI(string query)
        {
            var results = new List<(string Name, string Path)>();
            try
            {
                var esPath = GetEsPath();
                if (string.IsNullOrEmpty(esPath)) return results;

                // es.exe flags: -n = max-results
                var psi = new ProcessStartInfo
                {
                    FileName = esPath,
                    Arguments = $"-n 15 \"{query}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);

                    // es.exe outputs plain full paths, one per line
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        // Valid path starts with X:\ or UNC \\
                        if (trimmed.Length > 2 && trimmed[1] == ':')
                        {
                            results.Add((Path.GetFileName(trimmed), trimmed));
                        }
                        else if (trimmed.StartsWith(@"\\"))
                        {
                            results.Add((Path.GetFileName(trimmed), trimmed));
                        }
                    }
                }
            }
            catch { }
            return results;
        }

        #endregion

        #region Icon Extraction

        public static BitmapSource? ExtractIconFromFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (icon != null)
                    {
                        using var bmp = icon.ToBitmap();
                        var ms = new MemoryStream();
                        bmp.Save(ms, ImageFormat.Png);
                        ms.Position = 0;
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = ms;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        return bitmap;
                    }
                }
            }
            catch { }
            return null;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F0} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F0} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }

        #endregion
    }
}
