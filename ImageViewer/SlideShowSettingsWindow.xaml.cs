using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace ImageViewer
{
    public partial class SlideShowSettingsWindow : Window
    {
        public Color BackgroundColor { get; private set; } = Colors.Black;
        public bool AutoPlay => autoPlayCheck.IsChecked == true;
        public int Interval => int.Parse(intervalBox.Text);
        public bool RandomOrder => randomCheck.IsChecked == true;
        public bool RepeatPlay => repeatCheck.IsChecked == true;
        public bool StretchSmallImages => stretchSmallImagesCheck.IsChecked == true;
        public bool ShowText => showTextCheck.IsChecked == true;
        public TransitionEffect TransitionEffect => (TransitionEffect)transitionEffectCombo.SelectedIndex;

        public SlideShowSettingsWindow()
        {
            InitializeComponent();
            colorRect.Fill = new SolidColorBrush(BackgroundColor);
        }

        private void ColorRect_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var dialog = new ColorDialog
            {
                Color = System.Drawing.Color.FromArgb(
                    BackgroundColor.A,
                    BackgroundColor.R,
                    BackgroundColor.G,
                    BackgroundColor.B)
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                BackgroundColor = Color.FromArgb(
                    dialog.Color.A,
                    dialog.Color.R,
                    dialog.Color.G,
                    dialog.Color.B);
                colorRect.Fill = new SolidColorBrush(BackgroundColor);
            }
        }

        private void IntervalUp_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(intervalBox.Text, out int value))
            {
                intervalBox.Text = (value + 1).ToString();
            }
        }

        private void IntervalDown_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(intervalBox.Text, out int value) && value > 1)
            {
                intervalBox.Text = (value - 1).ToString();
            }
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(intervalBox.Text, out int interval) || interval < 1)
            {
                MessageBox.Show("请输入有效的时间间隔。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            DialogResult = true;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}