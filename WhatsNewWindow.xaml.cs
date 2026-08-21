using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ClipDropPro
{
    public partial class WhatsNewWindow : Window
    {
        public WhatsNewWindow()
        {
            InitializeComponent();
            
            // Apply Mica effect and theme
            RegisterTheme();
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
