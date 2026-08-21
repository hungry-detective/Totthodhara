using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ClipDropPro.Services;

namespace ClipDropPro.Plugins
{
    /// <summary>
    /// Loads C# plugins from DLL files using reflection.
    /// </summary>
    public static class CSharpPluginLoader
    {
        /// <summary>
        /// Load a plugin from a DLL file.
        /// </summary>
        /// <param name="dllPath">Path to the .dll file</param>
        /// <param name="services">Service provider for DI</param>
        /// <returns>Loaded plugin or null on failure</returns>
        public static IShelfWidget Load(string dllPath, IServiceProvider services)
        {
            if (!File.Exists(dllPath))
                return null;

            try
            {
                // Load assembly
                var assembly = Assembly.LoadFrom(dllPath);

                // Find first type implementing IShelfWidget
                var pluginType = assembly.GetTypes()
                    .FirstOrDefault(t =>
                        typeof(IShelfWidget).IsAssignableFrom(t) &&
                        !t.IsInterface &&
                        !t.IsAbstract);

                if (pluginType == null)
                    return null;

                // Create instance
                var plugin = Activator.CreateInstance(pluginType) as IShelfWidget;
                if (plugin == null)
                    return null;

                // Initialize with DI
                plugin.Initialize(services);

                return plugin;
            }
            catch (Exception ex)
            {
                Logger.Write($"[CSharpPluginLoader] Failed to load {dllPath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Scan a folder for DLL plugins.
        /// </summary>
        /// <param name="folderPath">Plugin folder path</param>
        /// <param name="services">Service provider</param>
        /// <returns>Array of loaded plugins</returns>
        public static IShelfWidget[] LoadFromFolder(string folderPath, IServiceProvider services)
        {
            if (!Directory.Exists(folderPath))
                return Array.Empty<IShelfWidget>();

            var dlls = Directory.GetFiles(folderPath, "*.dll", SearchOption.AllDirectories);
            return dlls
                .Select(dll => Load(dll, services))
                .Where(p => p != null)
                .ToArray();
        }
    }
}
