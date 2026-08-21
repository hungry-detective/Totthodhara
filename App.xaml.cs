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

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => {
                Log($"UNOBSERVED TASK EXCEPTION: {e.Exception}");
                e.SetObserved();
            };
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            KillOldInstances();

            const string appName = "Totthodhara-ClipDropPro-Unique-Mutex";
            bool createdNew;

            _mutex = new System.Threading.Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                // App is already running!
                // Since this app has a tray icon and a shelf, we don't need to do much 
                // but prevent the second instance from starting.
                System.Windows.MessageBox.Show("Totthodhara is already running.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
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

                    services.AddTransient<MainWindow>();
                    services.AddTransient<SettingsWindow>();
                })
                .Build();

            Log("Starting host...");
            await _host.StartAsync();
            Log("Host started.");

            var settingsService = GetService<ISettingsService>();
            Log("Settings service resolved.");

            var theme = settingsService.Theme;
            // Always apply Light as the base WPF-UI theme.
            // All color personalization (both light and dark modes) is handled
            // by MainWindow.UpdateTheme() via explicit resource overrides.
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
            Log("Theme applied.");

            Log("Resolving MainWindow...");
            var mainWindow = GetService<MainWindow>();
            Log("MainWindow resolved. Showing...");
            mainWindow.Show();
            
            base.OnStartup(e);
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
