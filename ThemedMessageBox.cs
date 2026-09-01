using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClipDropPro
{
    public static class ThemedMessageBox
    {
        public enum Buttons { OK, OKCancel, YesNo, YesNoCancel }
        public enum IconType { None, Info, Warning, Error, Question }
        public enum Result { OK, Cancel, Yes, No }

        public static Result Show(Window owner, string title, string message, Buttons buttons = Buttons.OK, IconType icon = IconType.None)
        {
            var accent = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["AccentColor"];
            var textColor = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextColor"];
            var windowBg = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["WindowBg"];
            var borderColor = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BorderColor"];

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
            titleBarContent.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = title,
                Foreground = textColor,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            });
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

            // Body
            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            System.Windows.Controls.Grid.SetRow(panel, 1);

            var headerPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            if (icon != IconType.None)
            {
                string iconChar = icon switch
                {
                    IconType.Info => "\uE946",
                    IconType.Warning => "\uE7BA",
                    IconType.Error => "\uE783",
                    IconType.Question => "\uE897",
                    _ => ""
                };
                System.Windows.Media.Brush iconColor = icon switch
                {
                    IconType.Warning => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xA8, 0x00)),
                    IconType.Error => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0x48, 0x4D)),
                    _ => accent
                };
                headerPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = iconChar,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                    FontSize = 22,
                    Foreground = iconColor,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 12, 0)
                });
            }
            headerPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = textColor,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 340
            });
            panel.Children.Add(headerPanel);

            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = message,
                Foreground = textColor,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16),
                MaxWidth = 380
            });

            var btnRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            Result dialogResult = Result.Cancel;
            System.Windows.Window winRef = null;

            void AddBtn(string label, bool isPrimary, RoutedEventHandler onClick)
            {
                var btn = new System.Windows.Controls.Button
                {
                    Content = label,
                    Padding = new Thickness(16, 6, 16, 6),
                    Margin = new Thickness(8, 0, 0, 0),
                    Background = isPrimary ? accent : System.Windows.Media.Brushes.Transparent,
                    Foreground = isPrimary ? System.Windows.Media.Brushes.White : textColor,
                    BorderBrush = isPrimary ? accent : borderColor,
                    BorderThickness = new Thickness(isPrimary ? 0 : 1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    MinWidth = 80
                };
                btn.Template = MakeButtonTemplate(isPrimary);
                btn.Click += onClick;
                btnRow.Children.Add(btn);
            }

            switch (buttons)
            {
                case Buttons.OK:
                    AddBtn("OK", true, (s, e) => { dialogResult = Result.OK; winRef?.Close(); });
                    break;
                case Buttons.OKCancel:
                    AddBtn("Cancel", false, (s, e) => { dialogResult = Result.Cancel; winRef?.Close(); });
                    AddBtn("OK", true, (s, e) => { dialogResult = Result.OK; winRef?.Close(); });
                    break;
                case Buttons.YesNo:
                    AddBtn("No", false, (s, e) => { dialogResult = Result.No; winRef?.Close(); });
                    AddBtn("Yes", true, (s, e) => { dialogResult = Result.Yes; winRef?.Close(); });
                    break;
                case Buttons.YesNoCancel:
                    AddBtn("Cancel", false, (s, e) => { dialogResult = Result.Cancel; winRef?.Close(); });
                    AddBtn("No", false, (s, e) => { dialogResult = Result.No; winRef?.Close(); });
                    AddBtn("Yes", true, (s, e) => { dialogResult = Result.Yes; winRef?.Close(); });
                    break;
            }

            panel.Children.Add(btnRow);
            outerGrid.Children.Add(panel);

            var win = new System.Windows.Window
            {
                Title = title,
                Width = 440,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Background = windowBg,
                BorderBrush = borderColor,
                BorderThickness = new Thickness(1),
                Content = outerGrid
            };
            winRef = win;

            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 1) win.DragMove();
            };
            closeBtn.Click += (s, e) => { dialogResult = Result.Cancel; win.Close(); };

            win.ShowDialog();
            return dialogResult;
        }

        private static ControlTemplate MakeButtonTemplate(bool isPrimary)
        {
            var template = new ControlTemplate(typeof(System.Windows.Controls.Button));
            var border = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            border.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BackgroundProperty));
            border.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BorderBrushProperty));
            border.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BorderThicknessProperty));
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(System.Windows.Controls.Border.PaddingProperty, new TemplateBindingExtension(System.Windows.Controls.Button.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            presenter.SetValue(System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            presenter.SetValue(System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }
    }
}