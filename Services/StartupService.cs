using System;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace ClipDropPro.Services
{
    public class StartupService : IStartupService
    {
        private const string AppName = "Totthodhara";
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    if (key == null) return false;
                    string? value = key.GetValue(AppName) as string;
                    if (string.IsNullOrEmpty(value)) return false;

                    // Verify the path is still correct
                    string currentPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                    return value.Trim('"').Equals(currentPath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        public void SetStartup(bool enable)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return;

                    if (enable)
                    {
                        string path = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                        if (!string.IsNullOrEmpty(path))
                        {
                            key.SetValue(AppName, $"\"{path}\"");
                        }
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting startup: {ex.Message}");
            }
        }
    }
}
