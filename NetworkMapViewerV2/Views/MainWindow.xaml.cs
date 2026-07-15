using NetworkMapViewerV2.Services;
using NetworkMapViewerV2.ViewModels;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NetworkMapViewerV2.Views
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }

        // --- WIN32 API IMPORTS ---
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Hotkey Constants
        private const int HOTKEY_ID = 9000;
        private const uint MOD_NONE = 0x0000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_SPACE = 0x20; // Spacebar



        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            this.DataContext = ViewModel;

            this.Closed += (sender, args) =>
            {
                // 1. Tell your ViewModel/PingService to stop the network loop
                try { ViewModel?.PingService.StopPinging(); } catch { }

                // 2. Release all MS SQL connection pools 
                try { Microsoft.Data.SqlClient.SqlConnection.ClearAllPools(); } catch { }

                // 3. Annihilate the background process
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            };
            NotificationEngine.ActiveRules = SettingsService.Load().ENS_Rules;
        }


        // This runs the moment the WPF Window is assigned a Handle by the OS
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var helper = new WindowInteropHelper(this);
            var source = HwndSource.FromHwnd(helper.Handle);

            // Add our custom listener to the Windows message pump
            source.AddHook(HwndHook);

            // Register: Ctrl (0x0002) + Shift (0x0004) + Space (0x20)
            RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_SPACE);
        }

        protected override void OnClosed(EventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            UnregisterHotKey(helper.Handle, HOTKEY_ID); // CRITICAL: Free the hotkey on exit
            base.OnClosed(e);           
            Environment.Exit(0);
        }

        // The actual listener that intercepts Windows OS messages
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                TriggerGlobalSearch();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void TriggerGlobalSearch()
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // 1. Open the floating macOS-style window
                var spotlight = new SpotlightSearchWindow();

                // 2. If the user typed something and hit Enter, this returns true
                if (spotlight.ShowDialog() == true)
                {
                    // 3. Bring the Main Network Map Viewer out of the taskbar!
                    if (this.WindowState == WindowState.Minimized)
                    {
                        this.WindowState = WindowState.Maximized;
                    }

                    this.Show();
                    this.Activate();
                    this.Topmost = true;
                    this.Topmost = false;

                    // 4. Pass the query to the ViewModel and trigger the search
                    if (this.DataContext is MainViewModel vm)
                    {
                        vm.SearchQuery = spotlight.SearchQuery;

                        // Triggers the exact same search logic as if you typed it in the main app
                        if (vm.PerformSearchCommand.CanExecute(null))
                        {
                            vm.PerformSearchCommand.Execute(null);
                        }
                    }
                }
            });
        }

        private void TxtSearch_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // If the search box just became visible...
            if (txtSearch.IsVisible)
            {
                // Wait for the UI to finish rendering it, then focus it!
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    txtSearch.Focus();
                    txtSearch.SelectAll(); // Highlights any existing text so you can instantly overwrite it
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
        }



        
    }
}