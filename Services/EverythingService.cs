using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClipDropPro.Services
{
    /// <summary>
    /// voidtools Everything launcher search (official SDK, x86 Everything32.dll
    /// shipped next to the exe). Zero hard dependency: if Everything isn't
    /// installed/running or the DLL is missing, every call returns empty and
    /// the shelf search silently stays clipboard-only.
    /// </summary>
    public static class EverythingService
    {
        private const int EVERYTHING_OK = 0;
        private const int EVERYTHING_ERROR_IPC = 2;

        private const uint EVERYTHING_REQUEST_FILE_NAME = 0x00000001;
        private const uint EVERYTHING_REQUEST_PATH = 0x00000002;

        // Title-only ranking: NO run history anywhere. The Everything app's
        // relevance order reads run counts (history); here we sort by name and
        // re-rank exact/starts-with on top, so a folder title always wins on
        // its own name. We also never write history (no IncRunCount /
        // SaveRunHistory calls; SDK queries don't touch the GUI history).
        private const uint EVERYTHING_SORT_NAME_ASCENDING = 1;

        [DllImport("Everything32.dll", CharSet = CharSet.Unicode)]
        private static extern uint Everything_SetSearchW(string lpSearchString);
        [DllImport("Everything32.dll")]
        private static extern void Everything_SetMatchPath(bool bEnable);
        [DllImport("Everything32.dll")]
        private static extern void Everything_SetRequestFlags(uint dwRequestFlags);
        [DllImport("Everything32.dll")]
        private static extern void Everything_SetMax(uint dwMax);
        [DllImport("Everything32.dll")]
        private static extern void Everything_SetSort(uint dwSortType);
        [DllImport("Everything32.dll")]
        private static extern void Everything_SetOffset(uint dwOffset);
        [DllImport("Everything32.dll")]
        private static extern bool Everything_QueryW(bool bWait);
        [DllImport("Everything32.dll")]
        private static extern uint Everything_GetNumResults();
        [DllImport("Everything32.dll")]
        private static extern uint Everything_GetLastError();
        [DllImport("Everything32.dll")]
        private static extern bool Everything_IsFolderResult(uint nIndex);
        [DllImport("Everything32.dll", CharSet = CharSet.Unicode)]
        private static extern uint Everything_GetResultFullPathNameW(uint nIndex, StringBuilder lpString, uint nMaxCount);
        [DllImport("Everything32.dll")]
        private static extern void Everything_Reset();

        private static readonly object _gate = new object();
        private static bool? _dllPresent;
        private static DateTime _lastProcessCheck = DateTime.MinValue;
        private static bool _lastProcessRunning;

        public sealed class FileHit
        {
            public string FullPath;
            public string Name;
            public bool IsFolder;
        }

        private static int NameRank(string name, string query)
        {
            if (string.IsNullOrEmpty(name)) return 4;
            if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
            if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            return 3; // path-only term match
        }

        private static bool DllPresent()
        {
            if (_dllPresent.HasValue) return _dllPresent.Value;
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string path = Path.Combine(dir, "Everything32.dll");
                if (!File.Exists(path))
                    ExtractEmbeddedDll(path); // auto-update swapped only the exe
                _dllPresent = File.Exists(path);
            }
            catch { _dllPresent = false; }
            return _dllPresent.Value;
        }

        /// <summary>
        /// Writes the embedded SDK copy next to the exe (updaters never got
        /// the sidecar file). Best-effort: read-only folders stay DLL-less and
        /// search simply stays clipboard-only.
        /// </summary>
        private static void ExtractEmbeddedDll(string path)
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using var s = asm.GetManifestResourceStream("Totthodhara.Everything32.dll");
                if (s == null) return;
                using var f = File.OpenWrite(path);
                s.CopyTo(f);
            }
            catch { /* read-only dir etc. — graceful fallback */ }
        }

        /// <summary>Cheap availability probe (DLL + client process, cached 10s).</summary>
        public static bool IsAvailable()
        {
            if (!DllPresent()) return false;
            try
            {
                if ((DateTime.UtcNow - _lastProcessCheck).TotalSeconds > 10)
                {
                    _lastProcessCheck = DateTime.UtcNow;
                    _lastProcessRunning = System.Diagnostics.Process
                        .GetProcessesByName("Everything").Length > 0;
                }
                return _lastProcessRunning;
            }
            catch { return false; }
        }

        /// <summary>
        /// Ranked file/folder hits for a query. Never throws; empty on any
        /// failure (Everything closed mid-query, IPC error, bad syntax).
        /// Runs the blocking query off the UI thread.
        /// </summary>
        public static Task<List<FileHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var hits = new List<FileHit>();
                if (string.IsNullOrWhiteSpace(query) || ct.IsCancellationRequested) return hits;
                if (!IsAvailable()) return hits;
                lock (_gate)
                {
                    try
                    {
                        Everything_Reset();
                        Everything_SetSearchW(query.Trim());
                        // Title matching: filename terms only. MatchPath(true)
                        // floods the pool (16479 path-term hits for "for me",
                        // parent folder drowned out by its own children).
                        try { Everything_SetMatchPath(false); } catch { }
                        Everything_SetRequestFlags(EVERYTHING_REQUEST_FILE_NAME | EVERYTHING_REQUEST_PATH);
                        Everything_SetMax((uint)Math.Max(1, Math.Min(64, maxResults)));
                        Everything_SetOffset(0);
                        try { Everything_SetSort(EVERYTHING_SORT_NAME_ASCENDING); } catch { }
                        if (!Everything_QueryW(true)) return hits;
                        if (Everything_GetLastError() != EVERYTHING_OK && Everything_GetLastError() != 0)
                        {
                            // IPC-level failure (e.g. client closed) — silent empty.
                        }
                        uint n = Everything_GetNumResults();
                        var sb = new StringBuilder(1024);
                        for (uint i = 0; i < n; i++)
                        {
                            if (ct.IsCancellationRequested) break;
                            sb.Clear();
                            if (Everything_GetResultFullPathNameW(i, sb, 1024) == 0) continue;
                            string full = sb.ToString();
                            if (string.IsNullOrEmpty(full)) continue;
                            bool folder = false;
                            try { folder = Everything_IsFolderResult(i); } catch { }
                            hits.Add(new FileHit
                            {
                                FullPath = full,
                                Name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                                IsFolder = folder
                            });
                        }
                        // Launcher ranking: an exact "for me" folder must beat 1500
                        // WinSxS term-matches. Exact name → starts-with → contains
                        // → rest (DB order); folders first within a rank.
                        if (hits.Count > 1)
                        {
                            string q = query.Trim();
                            hits = hits
                                .OrderBy(h => NameRank(h.Name, q))
                                .ThenBy(h => h.IsFolder ? 0 : 1)
                                .ToList();
                        }
                    }
                    catch (DllNotFoundException) { _dllPresent = false; }
                    catch { /* silent fallback — clipboard search keeps working */ }
                    finally
                    {
                        try { Everything_Reset(); } catch { }
                    }
                }
                return hits;
            }, ct);
        }
    }
}
