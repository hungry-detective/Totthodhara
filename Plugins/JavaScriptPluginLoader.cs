using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ClipDropPro.Services;
using Jint;
using Jint.Runtime;

namespace ClipDropPro.Plugins
{
    /// <summary>
    /// Loads JavaScript plugins using Jint engine.
    /// Each JS plugin is a folder with plugin.json + widget.js
    /// </summary>
    public class JavaScriptPluginLoader : IDisposable
    {
        private Engine _engine;

        public JavaScriptPluginLoader()
        {
            _engine = new Engine(cfg =>
            {
                cfg.TimeoutInterval(TimeSpan.FromSeconds(5)); // 5s timeout
                cfg.LimitMemory(16 * 1024 * 1024); // 16MB memory limit
            });
        }

        /// <summary>
        /// Load a JS plugin from a folder.
        /// Expected structure: plugin.json + widget.js
        /// </summary>
        public async Task<IJsWidget> LoadAsync(string pluginFolder)
        {
            var manifestPath = Path.Combine(pluginFolder, "plugin.json");
            var scriptPath = Path.Combine(pluginFolder, "widget.js");

            if (!File.Exists(manifestPath) || !File.Exists(scriptPath))
                return null;

            try
            {
                // Read manifest
                var manifestJson = await File.ReadAllTextAsync(manifestPath);
                var manifest = JsonSerializer.Deserialize<JsPluginManifest>(manifestJson);
                if (manifest == null) return null;

                // Read script
                var script = await File.ReadAllTextAsync(scriptPath);

                // Create widget
                var widget = new JsWidget(manifest, script, pluginFolder);
                await widget.InitializeAsync();

                return widget;
            }
            catch (Exception ex)
            {
                Logger.Write($"[JavaScriptPluginLoader] Failed to load {pluginFolder}: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            _engine?.Dispose();
        }
    }

    /// <summary>
    /// JS plugin manifest from plugin.json
    /// </summary>
    public class JsPluginManifest
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Version { get; set; } = "1.0.0";
        public string Author { get; set; } = "";
        public int UpdateIntervalMs { get; set; } = 5000;
    }

    /// <summary>
    /// Wraps a JavaScript plugin file.
    /// </summary>
    public class JsWidget : IJsWidget
    {
        private readonly JsPluginManifest _manifest;
        private readonly string _script;
        private readonly string _folder;
        private Engine _engine;

        public string Name => _manifest.Name;
        public string Description => _manifest.Description;
        public string Version => _manifest.Version;

        public JsWidget(JsPluginManifest manifest, string script, string folder)
        {
            _manifest = manifest;
            _script = script;
            _folder = folder;
        }

        public async Task InitializeAsync()
        {
            _engine = new Engine(cfg =>
            {
                cfg.TimeoutInterval(TimeSpan.FromSeconds(2));
                cfg.LimitMemory(8 * 1024 * 1024);
            });

            // Provide file system access (read-only)
            _engine.SetValue("readFile", (Func<string, string>)ReadFile);
            _engine.SetValue("getFolder", (Func<string>)(() => _folder));

            // Execute plugin script
            _engine.Execute(_script);
        }

        public async Task<JsWidgetResult> GetViewAsync()
        {
            return await CallFunctionAsync("getView");
        }

        public async Task<JsWidgetResult> UpdateAsync()
        {
            return await CallFunctionAsync("update");
        }

        private async Task<JsWidgetResult> CallFunctionAsync(string functionName)
        {
            try
            {
                var result = _engine.Invoke(functionName);
                if (result == null || result.IsUndefined())
                    return new JsWidgetResult { Content = "" };

                // Convert to JsWidgetResult
                var obj = result.AsObject();
                if (obj == null)
                    return new JsWidgetResult { Content = result.ToString() ?? "" };

                return new JsWidgetResult
                {
                    Type = obj["type"]?.ToString() ?? "text",
                    Content = obj["content"]?.ToString() ?? ""
                };
            }
            catch (JavaScriptException ex)
            {
                Logger.Write($"[JsWidget] {Name} error: {ex.Message}");
                return new JsWidgetResult { Content = $"Error: {ex.Message}" };
            }
            catch (Exception ex)
            {
                Logger.Write($"[JsWidget] {Name} error: {ex.Message}");
                return new JsWidgetResult { Content = "" };
            }
        }

        private string ReadFile(string relativePath)
        {
            var fullPath = Path.Combine(_folder, relativePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);
            return "";
        }
    }
}
