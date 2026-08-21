using System;

namespace ClipDropPro.Plugins
{
    public interface IShelfWidget
    {
        string Name { get; }
        string Description { get; }
        string Version { get; }
        System.Windows.FrameworkElement CreateView();
        void Initialize(IServiceProvider services);
        void Update();
        void Dispose();
    }
}
