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
            var btn = sender as System.Windows.Controls.Button;
            var updateService = App.GetService<IUpdateService>();
            if (updateService == null) return;

            // Disable button while checking to prevent multiple clicks
            if (btn != null) btn.IsEnabled = false;

            // Show non-modal checking dialog
            var checkingDialog = ShowUpdateStatusWindow(
                "Checking for updates…",
                $"Current version: v{updateService.GetCurrentVersion()}\n\nContacting GitHub releases...",
                isError: false,
                isChecking: true);

            UpdateInfo info;
            try
            {
                info = await updateService.CheckForUpdateAsync();
            }
            catch (Exception ex)
            {
                checkingDialog.Close();
                if (btn != null) btn.IsEnabled = true;
                ShowUpdateStatusWindow(
                    "Update Check Failed",
                    $"Could not reach GitHub releases.\n\nError: {ex.Message}",
                    isError: true,
                    isChecking: false);
                return;
            }

            checkingDialog.Close();
            if (btn != null) btn.IsEnabled = true;

            if (info.IsUpdateAvailable)
            {
                ShowUpdateAvailableDialog(updateService, info);
            }
            else if (!string.IsNullOrEmpty(info.ErrorMessage))
            {
                ShowUpdateStatusWindow(
                    "Update Check Failed",
                    $"Could not check for updates.\n\nError: {info.ErrorMessage}\n\nCheck your internet connection and try again.",
                    isError: true,
                    isChecking: false);
            }
            else
            {
                // Build a clear message showing both versions so user knows the app is newer than what's on GitHub
                string detail;
                if (!string.IsNullOrEmpty(info.LatestVersion) && info.LatestVersion != updateService.GetCurrentVersion())
                {
                    // Local is newer than what's published — show both clearly
                    detail = $"Your version: v{updateService.GetCurrentVersion()} (newer than published)\nPublished: v{info.LatestVersion}";
                }
                else
                {
                    detail = $"Totthodhara v{updateService.GetCurrentVersion()} is the latest version.";
                }
                if (info.PublishedAt.HasValue)
                {
                    detail += $"\nLast checked: {info.PublishedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
                }
                ShowUpdateStatusWindow(
                    "You're up to date",
                    detail,
                    isError: false,
                    isChecking: false);
            }
        }

        private System.Windows.Window ShowUpdateStatusWindow(string title, string message, bool isError, bool isChecking)
        {
            var accent = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["AccentColor"];
            var textColor = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextColor"];
            var windowBg = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["WindowBg"];
            var borderColor = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BorderColor"];

            // Outer grid: title bar + content
            var outerGrid = new System.Windows.Controls.Grid();
            outerGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(32) });
            outerGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Custom title bar
            var titleBar = new System.Windows.Controls.Border
            {
                Background = windowBg,
                BorderBrush = borderColor,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var titleBarContent = new System.Windows.Controls.Grid();
            titleBarContent.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBarContent.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
            var titleText = new System.Windows.Controls.TextBlock
            {
                Text = title,
                Foreground = textColor,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            titleBarContent.Children.Add(titleText);
            var closeBtn = new System.Windows.Controls.Button
            {
                Content = "\uE8BB",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = textColor,
                BorderThickness = new Thickness(0),
                Width = 32,
                Height = 32,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            System.Windows.Controls.Grid.SetColumn(closeBtn, 1);
            titleBarContent.Children.Add(closeBtn);
            titleBar.Child = titleBarContent;
            System.Windows.Controls.Grid.SetRow(titleBar, 0);
            outerGrid.Children.Add(titleBar);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            System.Windows.Controls.Grid.SetRow(panel, 1);

            // Header
            var headerPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            headerPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = isError ? "\uE783" : (isChecking ? "\uE895" : "\uE73E"),
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 22,
                Foreground = isError ? System.Windows.Media.Brushes.OrangeRed : accent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            });
            headerPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = textColor,
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(headerPanel);

            // Message body
            var body = new System.Windows.Controls.TextBlock
            {
                Text = message,
                Foreground = textColor,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(body);

            // Spinner when checking
            if (isChecking)
            {
                var spinnerPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
                var spinnerDot = new System.Windows.Controls.TextBlock
                {
                    Text = "● ● ●",
                    FontSize = 14,
                    Foreground = accent,
                    Margin = new Thickness(0, 4, 0, 0),
                    Opacity = 0.4
                };
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0.4, 1.0, TimeSpan.FromSeconds(0.6))
                {
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };
                System.Windows.Media.Animation.Storyboard.SetTarget(fadeIn, spinnerDot);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeIn, new System.Windows.PropertyPath(System.Windows.Controls.TextBlock.OpacityProperty));
                var sb = new System.Windows.Media.Animation.Storyboard();
                sb.Children.Add(fadeIn);
                spinnerDot.Loaded += (s, e) => sb.Begin();
                spinnerPanel.Children.Add(spinnerDot);
                panel.Children.Add(spinnerPanel);
            }

            // OK button — hidden while checking, shown when result is ready
            if (!isChecking)
            {
                var btnOk = new System.Windows.Controls.Button
                {
                    Content = "OK",
                    Padding = new Thickness(20, 6, 20, 6),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    Background = accent,
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                btnOk.Template = MakeAccentButtonTemplate();
                panel.Children.Add(btnOk);
            }

            outerGrid.Children.Add(panel);

            var win = new System.Windows.Window
            {
                Title = title,
                Width = 440,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Background = windowBg,
                BorderBrush = borderColor,
                BorderThickness = new Thickness(1),
                Content = outerGrid
            };

            // Drag window by title bar
            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 1) win.DragMove();
            };

            if (!isChecking)
            {
                var btnOk = (System.Windows.Controls.Button)panel.Children[panel.Children.Count - 1];
                btnOk.Click += (s, e) => win.Close();
            }
            closeBtn.Click += (s, e) => win.Close();

            // Non-modal while checking so code can await; modal for results
            if (isChecking)
                win.Show();
            else
                win.ShowDialog();
            return win;
        }

        private void ShowUpdateAvailableDialog(IUpdateService updateService, UpdateInfo info)
        {
            var accent = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["AccentColor"];
            var textColor = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextColor"];
            var controlBg = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["ControlBg"];
            var borderColor = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BorderColor"];
            var windowBg = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["WindowBg"];

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20, 16, 20, 16) };

            // Header
            var headerPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            headerPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "\uE896",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 22,
                Foreground = accent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            });
            headerPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"Update available: v{info.LatestVersion}",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = textColor,
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(headerPanel);

            // Version comparison line
            var versionLine = new System.Windows.Controls.TextBlock
            {
                Foreground = textColor,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            };
            versionLine.Inlines.Add(new System.Windows.Documents.Run($"Current: v{updateService.GetCurrentVersion()}") { Foreground = textColor });
            versionLine.Inlines.Add(new System.Windows.Documents.Run("    →    ") { Foreground = borderColor });
            versionLine.Inlines.Add(new System.Windows.Documents.Run($"Latest: v{info.LatestVersion}") { Foreground = accent, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(versionLine);

            if (info.PublishedAt.HasValue)
            {
                panel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = $"Released: {info.PublishedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm} UTC",
                    Foreground = textColor,
                    Opacity = 0.6,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 12)
                });
            }

            // Release notes
            if (!string.IsNullOrWhiteSpace(info.ReleaseNotes))
            {
                var notesLabel = new System.Windows.Controls.TextBlock
                {
                    Text = "Release notes:",
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    Foreground = textColor,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                panel.Children.Add(notesLabel);

                var notesBox = new System.Windows.Controls.Border
                {
                    Background = controlBg,
                    BorderBrush = borderColor,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new System.Windows.CornerRadius(6),
                    Padding = new Thickness(10),
                    MaxHeight = 180,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                var notesScroll = new System.Windows.Controls.ScrollViewer { VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
                var notesText = new System.Windows.Controls.TextBlock
                {
                    Text = info.ReleaseNotes,
                    Foreground = textColor,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas")
                };
                notesScroll.Content = notesText;
                notesBox.Child = notesScroll;
                panel.Children.Add(notesBox);
            }

            // Buttons row
            var btnRow = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var btnLater = new System.Windows.Controls.Button
            {
                Content = "Later",
                Padding = new Thickness(16, 6, 16, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = textColor,
                BorderBrush = borderColor,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnLater.Template = MakeSecondaryButtonTemplate();
            btnRow.Children.Add(btnLater);

            var btnUpdate = new System.Windows.Controls.Button
            {
                Content = "Download & Install",
                Padding = new Thickness(16, 6, 16, 6),
                Background = accent,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnUpdate.Template = MakeAccentButtonTemplate();
            btnRow.Children.Add(btnUpdate);
            panel.Children.Add(btnRow);

            var win = new System.Windows.Window
            {
                Title = "Update Available",
                Width = 480,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Background = windowBg,
                BorderBrush = borderColor,
                BorderThickness = new Thickness(1),
                Content = panel
            };
            btnLater.Click += (s, e) => win.Close();
            btnUpdate.Click += async (s, e) =>
            {
                win.Close();
                await RunUpdateWithProgressAsync(updateService, info);
            };
            win.ShowDialog();
        }

        private static System.Windows.Controls.ControlTemplate MakeAccentButtonTemplate()
        {
            var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
            var border = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            border.SetValue(System.Windows.Controls.Border.BackgroundProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Button.BackgroundProperty));
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new System.Windows.CornerRadius(6));
            border.SetValue(System.Windows.Controls.Border.PaddingProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Button.PaddingProperty));
            var presenter = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            presenter.SetValue(System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            presenter.SetValue(System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }

        private static System.Windows.Controls.ControlTemplate MakeSecondaryButtonTemplate()
        {
            var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
            var border = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            border.SetValue(System.Windows.Controls.Border.BackgroundProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Button.BackgroundProperty));
            border.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Button.BorderBrushProperty));
            border.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Button.BorderThicknessProperty));
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new System.Windows.CornerRadius(6));
            border.SetValue(System.Windows.Controls.Border.PaddingProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Button.PaddingProperty));
            var presenter = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            presenter.SetValue(System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            presenter.SetValue(System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
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
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["WindowBg"] ?? System.Windows.Media.Brushes.White,
                BorderBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BorderColor"] ?? System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1),
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
                ThemedMessageBox.Show(
                    this,
                    "Update Ready",
                    "Update downloaded successfully. The app will now restart to apply the update.",
                    ThemedMessageBox.Buttons.OK,
                    ThemedMessageBox.IconType.Info);
                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                var fallback = ThemedMessageBox.Show(
                    this,
                    "Update Failed",
                    "Automatic update failed. Would you like to open the download page in your browser instead?",
                    ThemedMessageBox.Buttons.YesNo,
                    ThemedMessageBox.IconType.Warning);
                if (fallback == ThemedMessageBox.Result.Yes)
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
