using System;
using System.Windows;
using System.Windows.Controls;
using ClipDropPro.Services;
using ClipDropPro.ViewModels;
using WpfColor = System.Windows.Media.Color;
using WpfBrush = System.Windows.Media.SolidColorBrush;

namespace ClipDropPro.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly ISettingsService _settingsService;

        public SettingsWindow(SettingsViewModel viewModel, ISettingsService settingsService)
        {
            _settingsService = settingsService;
            InitializeComponent();
            DataContext = viewModel;
            
            ApplyThemeColors(viewModel.Theme);

            var updateService = App.GetService<IUpdateService>();
            if (updateService != null)
            {
                VersionText.Text = $"Version {updateService.GetCurrentVersion()}";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ColorSwatch_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border swatch && swatch.Tag is string resourceKey)
            {
                var dialog = new System.Windows.Forms.ColorDialog();
                if (System.Windows.Application.Current.Resources[resourceKey] is WpfBrush currentBrush)
                {
                    dialog.Color = System.Drawing.Color.FromArgb(currentBrush.Color.A, currentBrush.Color.R, currentBrush.Color.G, currentBrush.Color.B);
                }
                dialog.FullOpen = true;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var newColor = WpfColor.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
                    System.Windows.Application.Current.Resources[resourceKey] = new WpfBrush(newColor);
                    string hex = $"#{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}";

                    if (DataContext is SettingsViewModel vm)
                    {
                        switch (resourceKey)
                        {
                            case "CardBg": vm.CustomCardColor = hex; _settingsService.CustomCardColor = hex; break;
                            case "ControlBg": vm.CustomControlBgColor = hex; _settingsService.CustomControlBgColor = hex; break;
                            case "AccentColor": vm.CustomAccentColor = hex; _settingsService.CustomAccentColor = hex; break;
                            case "WindowBg": vm.CustomWindowBgColor = hex; _settingsService.CustomWindowBgColor = hex; break;
                            case "BorderColor": vm.CustomBorderColor = hex; _settingsService.CustomBorderColor = hex; break;
                            case "TextColor": vm.CustomTextColor = hex; _settingsService.CustomTextColor = hex; break;
                        }
                    }
                }
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is string theme)
            {
                ApplyThemeColors(theme);
            }
        }

        private void ApplyThemeColors(string theme)
        {
            bool isLight = theme == "Light";
            if (theme == "System")
            {
                var registryValue = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
                isLight = registryValue != null && (int)registryValue == 1;
            }

            void SetResource(string key, object value)
            {
                this.Resources[key] = value;
            }

            if (isLight)
            {
                SetResource("AppBackground", new WpfBrush(WpfColor.FromRgb(0xFF, 0xFF, 0xFF)));
                SetResource("WindowBg", new WpfBrush(WpfColor.FromRgb(0xFF, 0xFF, 0xFF)));
                SetResource("CardBg", new WpfBrush(WpfColor.FromArgb(0x1A, 0x00, 0x00, 0x00)));
                SetResource("TextColor", new WpfBrush(WpfColor.FromRgb(0x22, 0x22, 0x22)));
                SetResource("IconColor", new WpfBrush(WpfColor.FromRgb(0x22, 0x22, 0x22)));
                SetResource("ControlBg", new WpfBrush(WpfColor.FromArgb(0x33, 0x00, 0x00, 0x00)));
                SetResource("BorderColor", new WpfBrush(WpfColor.FromArgb(30, 0, 0, 0)));
                SetResource("AccentColor", new WpfBrush(WpfColor.FromArgb(0xFF, 0x00, 0x78, 0xD4)));
                SetResource("AccentColorDim", new WpfBrush(WpfColor.FromArgb(0x40, 0x00, 0x78, 0xD4)));
                SetResource("MenuBg", new WpfBrush(WpfColor.FromRgb(0xFA, 0xFA, 0xFA)));
                SetResource("ToolTipBg", new WpfBrush(WpfColor.FromArgb(245, 250, 250, 250)));
                SetResource("ShadowOpacity", 0.2d);
                SetResource("ShadowColor", System.Windows.Media.Colors.Black);
            }
            else
            {
                SetResource("AppBackground", new WpfBrush(WpfColor.FromRgb(0x14, 0x14, 0x14)));
                SetResource("WindowBg", new WpfBrush(WpfColor.FromRgb(0x14, 0x14, 0x14)));
                SetResource("CardBg", new WpfBrush(WpfColor.FromArgb(0x12, 0xFF, 0xFF, 0xFF)));
                SetResource("TextColor", new WpfBrush(System.Windows.Media.Colors.White));
                SetResource("IconColor", new WpfBrush(System.Windows.Media.Colors.White));
                SetResource("ControlBg", new WpfBrush(WpfColor.FromArgb(0x26, 0xFF, 0xFF, 0xFF)));
                SetResource("BorderColor", new WpfBrush(WpfColor.FromArgb(40, 255, 255, 255)));
                SetResource("AccentColor", new WpfBrush(WpfColor.FromArgb(0xFF, 0x60, 0xCD, 0xFF)));
                SetResource("AccentColorDim", new WpfBrush(WpfColor.FromArgb(0x40, 0x60, 0xCD, 0xFF)));
                SetResource("MenuBg", new WpfBrush(WpfColor.FromRgb(0x1E, 0x1E, 0x1E)));
                SetResource("ToolTipBg", new WpfBrush(WpfColor.FromArgb(245, 30, 30, 30)));
                SetResource("ShadowOpacity", 0.45d);
                SetResource("ShadowColor", System.Windows.Media.Colors.Black);
            }
        }

        private async void CheckForUpdatesSettings_Click(object sender, RoutedEventArgs e)
        {
            var updateService = App.GetService<IUpdateService>();
            if (updateService == null) return;

            var info = await updateService.CheckForUpdateAsync();

            if (info.IsUpdateAvailable)
            {
                var result = System.Windows.MessageBox.Show(
                    $"A new version v{info.LatestVersion} is available!\n\nCurrent version: v{updateService.GetCurrentVersion()}\n\n{(string.IsNullOrEmpty(info.ReleaseNotes) ? "" : $"Release notes:\n{info.ReleaseNotes}\n\n")}Download and install now? The app will restart automatically.",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes && !string.IsNullOrEmpty(info.DownloadUrl))
                {
                    await RunUpdateWithProgressAsync(updateService, info);
                }
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"You are running the latest version (v{updateService.GetCurrentVersion()}).",
                    "No Updates Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private async System.Threading.Tasks.Task RunUpdateWithProgressAsync(IUpdateService updateService, UpdateInfo info)
        {
            var progressBar = new System.Windows.Controls.ProgressBar
            {
                Minimum = 0, Maximum = 100, Height = 14, Margin = new Thickness(16, 8, 16, 0),
                IsIndeterminate = false
            };
            var statusText = new System.Windows.Controls.TextBlock
            {
                Text = "Downloading 0%...", Margin = new Thickness(16, 8, 16, 0),
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextColor"] ?? System.Windows.Media.Brushes.Black,
                FontSize = 12
            };
            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(0, 16, 0, 16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"Downloading v{info.LatestVersion}...",
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(16, 0, 16, 0),
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextColor"] ?? System.Windows.Media.Brushes.Black,
                FontSize = 13
            });
            panel.Children.Add(statusText);
            panel.Children.Add(progressBar);

            var win = new System.Windows.Window
            {
                Title = "Updating Totthodhara",
                Width = 420, Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
                Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["WindowBg"] ?? System.Windows.Media.Brushes.White,
                Content = panel
            };

            var progress = new System.Progress<double>(pct =>
            {
                progressBar.Value = pct;
                statusText.Text = pct >= 100 ? "Download complete. Preparing update..." : $"Downloading {pct:F0}%...";
            });

            win.Show();
            await System.Threading.Tasks.Task.Delay(200);

            bool ok = false;
            try
            {
                ok = await updateService.DownloadAndInstallAsync(info, progress);
            }
            catch (Exception ex)
            {
                Logger.Write($"[SettingsWindow] Update error: {ex}");
            }

            win.Close();

            if (ok)
            {
                System.Windows.MessageBox.Show(
                    "Update downloaded successfully. The app will now restart to apply the update.",
                    "Update Ready", MessageBoxButton.OK, MessageBoxImage.Information);
                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                var fallback = System.Windows.MessageBox.Show(
                    "Automatic update failed. Would you like to open the download page in your browser instead?",
                    "Update Failed", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (fallback == MessageBoxResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = info.DownloadUrl,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }
        }

        private void GitHubLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/hungry-detective/Totthodhara",
                UseShellExecute = true
            });
        }
    }
}
