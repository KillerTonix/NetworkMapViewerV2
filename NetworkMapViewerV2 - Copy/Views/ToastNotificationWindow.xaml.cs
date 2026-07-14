using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace NetworkMapViewerV2.Views
{
    public partial class ToastNotificationWindow : Window
    {
        private DispatcherTimer _timer;
        private static AppSettings settings = SettingsService.Load();

        public ToastNotificationWindow(string title, string message, bool isDown)
        {
            InitializeComponent();

            txtTitle.Text = title;
            txtMessage.Text = message;

            // Dynamically style the window based on the network event!
            if (isDown)
            {
                MainBorder.BorderBrush = new SolidColorBrush(Colors.Red);
                txtIcon.Text = "❌";
                txtIcon.Foreground = new SolidColorBrush(Colors.Red);
            }
            else
            {
                MainBorder.BorderBrush = new SolidColorBrush(Colors.LightGreen);
                txtIcon.Text = "✔️";
                txtIcon.Foreground = new SolidColorBrush(Colors.LightGreen);
            }

            // Start the 6-second auto-destruct timer
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            string soundFilePath = settings.ENS_OfflineSoundFilePath;
            if (!isDown) soundFilePath = settings.ENS_OnlineSoundFilePath;
            using var player = new SoundPlayer(soundFilePath);
            player.Play();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Position the window in the bottom right corner of the primary screen
            var desktopWorkingArea = SystemParameters.WorkArea;
            this.Left = desktopWorkingArea.Right - this.Width - 10;
            this.Top = desktopWorkingArea.Bottom - this.Height - 10; 
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _timer.Stop();
            this.Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            this.Close();
        }
    }
}