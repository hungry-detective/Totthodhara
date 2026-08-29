using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using ClipDropPro.Services;
using ClipDropPro.ViewModels;
using ClipDropPro.Views;
using ClipDropPro.Plugins;
using System.IO;
using System;
using System.Linq;
using System.Diagnostics;

namespace ClipDropPro
{
    public partial class App : System.Windows.Application
    {
        private static IHost _host;
        private static System.Threading.Mutex _mutex = null;
        private static MainWindow _mainWindowInstance = null;

        private void KillOldInstances()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                string[] namesToKill = { "Totthodhara", "ClipDropPro" };
                
                foreach (string name in namesToKill)
                {
                    var processes = Process.GetProcessesByName(name);
                    foreach (var p in processes)
                    {
                        if (p.Id != current.Id)
                        {
                            Log($"Killing old instance: {p.ProcessName} (PID: {p.Id})");
                            try 
                            { 
                                p.Kill(); 
                                p.WaitForExit(2000); 
                            } 
                            catch (Exception ex) 
                            { 
                                Log($"Failed to kill {p.Id}: {ex.Message}"); 
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in KillOldInstances: {ex.Message}");
            }
        }

        public static T GetService<T>() where T : class
        {
            return _host.Services.GetService(typeof(T)) as T;
        }

        public App()
        {
            this.DispatcherUnhandledException += (s, e) => {
                Log($"UNHANDLED DISPATCHER EXCEPTION: {e.Exception}");
                e.Handled = true; // Try to keep app alive
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                Log($"FATAL UNHANDLED EXCEPTION: {e.ExceptionObject}");
            };

            AppDomain.CurrentDomain.ProcessExit += (s, e) => {
                Log($"ProcessExit triggered. StackTrace:\n{Environment.StackTrace}");
            };

            this.Exit += (s, e) => {
                Log($"App.Exit event triggered with code {e.ApplicationExitCode}. StackTrace:\n{Environment.StackTrace}");
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            KillOldInstances();

            const string appName = "Local\\Totthodhara-ClipDropPro-Unique-Mutex";
            bool createdNew = false;
            try
            {
                _mutex = new System.Threading.Mutex(true, appName, out createdNew);
                if (!createdNew)
                {
                    System.Threading.Thread.Sleep(300);
                    _mutex?.Dispose();
                    _mutex = new System.Threading.Mutex(true, appName, out createdNew);
                }
            }
            catch (Exception ex)
            {
                Log($"Mutex warning: {ex.Message}");
                createdNew = true;
            }

            if (!createdNew)
            {
                Log("Another instance is already active. Exiting.");
                System.Windows.Application.Current.Shutdown();
                return;
            }

            Log("OnStartup started");
            _host = Host.CreateDefaultBuilder(e.Args)
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<IFileStorageService, FileStorageService>();
                    services.AddSingleton<IDataService, SqliteDataService>();
                    services.AddSingleton<ISettingsService, SettingsService>();
                    services.AddSingleton<IHotkeyService, HotkeyService>();
                    services.AddSingleton<IGestureService, GestureService>();
                    services.AddSingleton<IStartupService, StartupService>();
                    services.AddSingleton<IUpdateService, UpdateService>();
                    services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
                    services.AddSingleton<PluginManager>();

                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<SettingsViewModel>();

                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<SettingsWindow>();
                })
                .Build();

            Log("Starting host...");
            _host.Start();
            Log("Host started.");

            // --- Update rollback/success ---
            var updateService = GetService<IUpdateService>();
            if (updateService.HasPendingUpdate)
            {
                // If we got this far, the update likely succeeded
                updateService.MarkUpdateSucceeded();
                Log("Pending update verified and backup cleaned.");
            }

            var settingsService = GetService<ISettingsService>();
            Log("Settings service resolved.");

            var theme = settingsService.Theme;
            // Always apply Light as the base WPF-UI theme.
            // All color personalization (both light and dark modes) is handled
            // by MainWindow.UpdateTheme() via explicit resource overrides.
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
            Log("Theme applied.");

            Log("Resolving MainWindow...");
            _mainWindowInstance = GetService<MainWindow>();
            this.MainWindow = _mainWindowInstance;
            Log("MainWindow resolved. Showing...");
            _mainWindowInstance.Show();
            Log("MainWindow shown.");

            // --- Auto-check for updates on startup (if enabled) ---
            if (settingsService.AutoCheckUpdates)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var updateInfo = await updateService.CheckForUpdateAsync();
                        if (updateInfo.IsUpdateAvailable)
                        {
                            Log($"Update available: {updateInfo.LatestVersion}");
                            if (settingsService.SilentAutoUpdate && !string.IsNullOrEmpty(updateInfo.DownloadUrl))
                            {
                                Log("Silent mode — downloading in background.");
                                var progress = new Progress<double>();
                                await updateService.DownloadAndInstallAsync(updateInfo, progress);
                            }
                            else
                            {
                                _mainWindowInstance.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    _mainWindowInstance.ShowUpdateNotification(updateInfo);
                                }));
                            }
                        }
                        else
                        {
                            Log("No update available on startup check.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Startup update check failed (non-fatal): {ex.Message}");
                    }
                });
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host != null)
            {
                using (_host)
                {
                    await _host.StopAsync(TimeSpan.FromSeconds(5));
                }
            }
            base.OnExit(e);
        }

        private static void Log(string message) => Services.Logger.Write($"[App] {message}");
    }
}
