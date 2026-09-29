using System.Windows;
using System.Windows.Input;

namespace ClipDropPro
{
    public partial class WhatsNewWindow : Window
    {
        public WhatsNewWindow()
        {
            InitializeComponent();
            
            // Apply Mica effect and theme
            RegisterTheme();

            // Native titlebar/scrollbars follow the effective theme. Content
            // itself is all DynamicResource so it auto-adapts when MainWindow
            // pushes new resources on an OS flip — window is also rebuilt fresh
            // on every open (never a cached old-theme page).
            SourceInitialized += (s, e) =>
            {
                try { Services.OsThemeHelper.ApplyWindowTheme(this, Services.OsThemeHelper.CurrentResourcesAreLight()); } catch { }
            };
        }

        private void RegisterTheme()
        {
            // Do not call ApplicationThemeManager.Apply(this) here, 
            // as it enforces Mica/Backdrop which causes transparency.
            // We rely on the SolidWindowBg resource in XAML.
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
