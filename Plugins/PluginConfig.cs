using System;
using System.Collections.Generic;

namespace ClipDropPro.Plugins
{
    /// <summary>
    /// Stores plugin configuration (enabled/disabled state).
    /// Persisted to settings.json.
    /// </summary>
    public class PluginConfig
    {
        /// <summary>
        /// Plugin unique identifier (folder name or assembly name).
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Display name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Whether plugin is enabled.
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Plugin type: "CSharp" or "JavaScript".
        /// </summary>
        public string PluginType { get; set; } = "CSharp";

        /// <summary>
        /// Path to plugin folder or .js file.
        /// </summary>
        public string Path { get; set; } = "";

        /// <summary>
        /// Last loaded time.
        /// </summary>
        public DateTime? LastLoaded { get; set; }
    }

    /// <summary>
    /// Root settings for all plugins.
    /// </summary>
    public class PluginsSettings
    {
        public List<PluginConfig> Plugins { get; set; } = new();
        public bool ShowPlugins { get; set; } = true;
    }
}
