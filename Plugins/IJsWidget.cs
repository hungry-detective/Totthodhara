using System;
using System.Threading.Tasks;

namespace ClipDropPro.Plugins
{
    /// <summary>
    /// Interface for JavaScript plugins executed via Jint.
    /// JS files define functions matching these signatures.
    /// </summary>
    public interface IJsWidget
    {
        /// <summary>
        /// Plugin name (from plugin.json).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Plugin description (from plugin.json).
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Plugin version (from plugin.json).
        /// </summary>
        string Version { get; }

        /// <summary>
        /// Get current display content.
        /// JS function: getView() → { type: "text"|"image", content: string }
        /// </summary>
        Task<JsWidgetResult> GetViewAsync();

        /// <summary>
        /// Refresh data (called periodically).
        /// JS function: update() → { type: "text"|"image", content: string }
        /// </summary>
        Task<JsWidgetResult> UpdateAsync();
    }

    /// <summary>
    /// Result returned by JS widget functions.
    /// </summary>
    public class JsWidgetResult
    {
        public string Type { get; set; } = "text";
        public string Content { get; set; } = "";
    }
}
