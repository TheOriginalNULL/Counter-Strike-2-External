using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CounterStrike2
{
    public partial class CrosshairSettingsWindow : Window
    {
        public CrosshairSettingsWindow()
        {
            InitializeComponent();
            BuildSwatches();

            SizeSlider.Value      = Config.Current.CrosshairSize;
            ThicknessSlider.Value = Config.Current.CrosshairThickness;
            GapSlider.Value       = Config.Current.CrosshairGap;
            DotCheck.IsChecked    = Config.Current.CrosshairDot;
            HexBox.Text           = Config.Current.CrosshairColor;

            UpdatePreview();
        }

        private void OnDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void BuildSwatches()
        {
            foreach (var hex in MainWindow.SwatchColors)
            {
                var sw = new Border
                {
                    Width = 24, Height = 24,
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                    Margin = new Thickness(2),
                    Cursor = Cursors.Hand,
                    BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                };
                var capturedHex = hex;
                sw.MouseLeftButtonDown += (_, _) => ApplyColor(capturedHex);
                SwatchGrid.Children.Add(sw);
            }
        }

        private void ApplyColor(string hex)
        {
            Config.Current.CrosshairColor = hex;
            HexBox.Text = hex;
            UpdatePreview();
        }

        private void HexBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyHexFromBox();
        }

        private void ApplyHex_Click(object sender, RoutedEventArgs e) => ApplyHexFromBox();

        private void ApplyHexFromBox()
        {
            string hex = HexBox.Text.Trim();
            if (!hex.StartsWith("#")) hex = "#" + hex;
            try { ColorConverter.ConvertFromString(hex); } catch { return; }
            ApplyColor(hex);
        }

        private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Config.Current.CrosshairSize = e.NewValue;
            SizeLabel.Text = e.NewValue.ToString("0");
            UpdatePreview();
        }

        private void ThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Config.Current.CrosshairThickness = e.NewValue;
            ThicknessLabel.Text = e.NewValue.ToString("0.0");
            UpdatePreview();
        }

        private void GapSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Config.Current.CrosshairGap = e.NewValue;
            GapLabel.Text = e.NewValue.ToString("0");
            UpdatePreview();
        }

        private void DotCheck_Changed(object sender, RoutedEventArgs e)
        {
            Config.Current.CrosshairDot = DotCheck.IsChecked == true;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            PreviewCanvas.Children.Clear();

            Brush brush;
            try { brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Config.Current.CrosshairColor)); }
            catch { brush = Brushes.White; }

            double cx = PreviewCanvas.Width / 2;
            double cy = PreviewCanvas.Height / 2;
            double gap   = Config.Current.CrosshairGap;
            double size  = Config.Current.CrosshairSize;
            double thick = Config.Current.CrosshairThickness;
            double end   = gap + size;

            void AddLine(double x1, double y1, double x2, double y2) =>
                PreviewCanvas.Children.Add(new Line
                {
                    X1 = cx + x1, Y1 = cy + y1, X2 = cx + x2, Y2 = cy + y2,
                    Stroke = brush, StrokeThickness = thick,
                });

            AddLine(-end, 0, -gap, 0);
            AddLine(gap, 0, end, 0);
            AddLine(0, -end, 0, -gap);
            AddLine(0, gap, 0, end);

            if (Config.Current.CrosshairDot)
            {
                var dot = new Ellipse { Width = 2, Height = 2, Fill = brush };
                Canvas.SetLeft(dot, cx - 1);
                Canvas.SetTop(dot,  cy - 1);
                PreviewCanvas.Children.Add(dot);
            }
        }
    }
}
