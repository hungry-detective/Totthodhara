using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using ClipDropPro.Services;

namespace ClipDropPro.Plugins
{
    /// <summary>
    /// Manages discovery, loading, and lifecycle of all plugins (C# and JS).
    /// </summary>
    public class PluginManager : IDisposable
    {
        private readonly IServiceProvider _services;
        private readonly string _baseDir;
        private readonly string _csharpDir;
        private readonly string _jsDir;
        private readonly DispatcherTimer _updateTimer;

        private readonly List<LoadedPlugin> _plugins = new();
        private JavaScriptPluginLoader _jsLoader;

        public IReadOnlyList<LoadedPlugin> Plugins => _plugins.AsReadOnly();

        public PluginManager(IServiceProvider services)
        {
            _services = services;
            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _csharpDir = Path.Combine(_baseDir, "Plugins", "CSharp");
            _jsDir = Path.Combine(_baseDir, "Plugins", "JavaScript");

            // Ensure directories exist
            Directory.CreateDirectory(_csharpDir);
            Directory.CreateDirectory(_jsDir);

            // Update timer (every 5 seconds)
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _updateTimer.Tick += UpdateTimer_Tick;
        }

        /// <summary>
        /// Discover and load all enabled plugins.
        /// </summary>
        public async Task LoadPluginsAsync(PluginsSettings settings)
        {
            // Dispose existing plugins
            DisposePlugins();

            if (!settings.ShowPlugins)
                return;

            // Load C# plugins
            await LoadCSharpPluginsAsync(settings);

            // Load JavaScript plugins
            await LoadJsPluginsAsync(settings);

            // Start update timer
            if (_plugins.Count > 0)
                _updateTimer.Start();
        }

        private async Task LoadCSharpPluginsAsync(PluginsSettings settings)
        {
            // First, load built-in plugins from the executing assembly
            LoadBuiltInPlugins(settings);

            // Then, load external plugins from the Plugins\CSharp folder
            if (!Directory.Exists(_csharpDir))
                return;

            var folders = Directory.GetDirectories(_csharpDir);
            foreach (var folder in folders)
            {
                var folderName = Path.GetFileName(folder);
                var config = settings.Plugins.FirstOrDefault(p => p.Id == folderName && p.PluginType == "CSharp");

                // Auto-discover new plugins
                if (config == null)
                {
                    config = new PluginConfig
                    {
                        Id = folderName,
                        Name = folderName,
                        PluginType = "CSharp",
                        Path = folder,
                        IsEnabled = true
                    };
                    settings.Plugins.Add(config);
                }

                if (!config.IsEnabled)
                    continue;

                // Find DLL in folder
                var dlls = Directory.GetFiles(folder, "*.dll");
                foreach (var dll in dlls)
                {
                    var widget = CSharpPluginLoader.Load(dll, _services);
                    if (widget != null)
                    {
                        _plugins.Add(new LoadedPlugin
                        {
                            Config = config,
                            CSharpWidget = widget
                        });
                        Logger.Write($"[PluginManager] Loaded C# plugin: {widget.Name}");
                    }
                }
            }
        }

        private void LoadBuiltInPlugins(PluginsSettings settings)
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IShelfWidget).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                    .ToList();

                foreach (var type in pluginTypes)
                {
                    var pluginId = $"BuiltIn_{type.Name}";
                    var config = settings.Plugins.FirstOrDefault(p => p.Id == pluginId);

                    if (config == null)
                    {
                        config = new PluginConfig
                        {
                            Id = pluginId,
                            Name = type.Name,
                            PluginType = "CSharp",
                            Path = "Built-in",
                            IsEnabled = true
                        };
                        settings.Plugins.Add(config);
                    }

                    if (!config.IsEnabled)
                        continue;

                    var widget = Activator.CreateInstance(type) as IShelfWidget;
                    if (widget != null)
                    {
                        widget.Initialize(_services);
                        _plugins.Add(new LoadedPlugin
                        {
                            Config = config,
                            CSharpWidget = widget
                        });
                        Logger.Write($"[PluginManager] Loaded built-in plugin: {widget.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Write($"[PluginManager] Error loading built-in plugins: {ex.Message}");
            }
        }

        private async Task LoadJsPluginsAsync(PluginsSettings settings)
        {
            if (!Directory.Exists(_jsDir))
                return;

            _jsLoader?.Dispose();
            _jsLoader = new JavaScriptPluginLoader();

            var folders = Directory.GetDirectories(_jsDir);
            foreach (var folder in folders)
            {
                var folderName = Path.GetFileName(folder);
                var config = settings.Plugins.FirstOrDefault(p => p.Id == folderName && p.PluginType == "JavaScript");

                if (config == null)
                {
                    config = new PluginConfig
                    {
                        Id = folderName,
                        Name = folderName,
                        PluginType = "JavaScript",
                        Path = folder,
                        IsEnabled = true
                    };
                    settings.Plugins.Add(config);
                }

                if (!config.IsEnabled)
                    continue;

                var widget = await _jsLoader.LoadAsync(folder);
                if (widget != null)
                {
                    _plugins.Add(new LoadedPlugin
                    {
                        Config = config,
                        JsWidget = widget
                    });
                    Logger.Write($"[PluginManager] Loaded JS plugin: {widget.Name}");
                }
            }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            foreach (var plugin in _plugins)
            {
                try
                {
                    if (plugin.CSharpWidget != null)
                        plugin.CSharpWidget.Update();
                    // JS plugins updated via async
                }
                catch (Exception ex)
                {
                    Logger.Write($"[PluginManager] Update error for {plugin.Config.Name}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Get all loaded plugin views for UI display.
        /// </summary>
        public List<System.Windows.FrameworkElement> GetAllViews()
        {
            var views = new List<System.Windows.FrameworkElement>();
            foreach (var plugin in _plugins)
            {
                try
                {
                    if (plugin.CSharpWidget != null)
                    {
                        var view = plugin.CSharpWidget.CreateView();
                        if (view != null)
                            views.Add(view);
                    }
                    // JS plugins would create TextBlock views
                }
                catch (Exception ex)
                {
                    Logger.Write($"[PluginManager] View error for {plugin.Config.Name}: {ex.Message}");
                }
            }
            return views;
        }

        private void DisposePlugins()
        {
            _updateTimer.Stop();
            foreach (var plugin in _plugins)
            {
                try
                {
                    plugin.CSharpWidget?.Dispose();
                }
                catch { }
            }
            _plugins.Clear();
        }

        public void Dispose()
        {
            DisposePlugins();
            _jsLoader?.Dispose();
        }
    }

    /// <summary>
    /// Represents a loaded plugin instance.
    /// </summary>
    public class LoadedPlugin
    {
        public PluginConfig Config { get; set; }
        public IShelfWidget CSharpWidget { get; set; }
        public IJsWidget JsWidget { get; set; }
    }
}
