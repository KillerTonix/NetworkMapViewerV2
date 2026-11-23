using NetworkMapViewerV2.Services;
using NetworkMapViewerV2.ViewModels;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace NetworkMapViewerV2.Views
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }
        private bool _closeConfirmed;

        // --- WIN32 API IMPORTS ---
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Hotkey Constants
        private const int HOTKEY_ID = 9000;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_SPACE = 0x20; // Spacebar

        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            this.DataContext = ViewModel;
            if (!Data.DatabaseService.CanWrite) Title += "   [Read-only]";

            // Whenever the search bar closes (Esc, the X button, ...) hand the keyboard back to the map,
            // otherwise map shortcuts such as F2 / Delete / Ctrl+C stay dead until the canvas is clicked.
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsSearchVisible) && !ViewModel.IsSearchVisible)
                {
                    Dispatcher.BeginInvoke(new Action(FocusActiveMap), DispatcherPriority.Input);
                }
            };

            this.Closing += async (sender, args) =>
            {
                if (_closeConfirmed) return;

                var dirtyTabs = ViewModel.OpenTabs.Where(t => t.HasUnsavedChanges).ToList();
                if (dirtyTabs.Count == 0) return;

                args.Cancel = true;   // must be set before the first await

                foreach (var tab in dirtyTabs)
                {
                    if (!await ViewModel.ConfirmCloseTabAsync(tab))
                        return;       // Cancel or a failed save: keep the app open
                }

                _closeConfirmed = true;
                Close();              // ask again; this time nothing stops it
            };

            var settings = SettingsService.Load();
            NotificationEngine.ActiveRules = settings.ENS_Rules ?? [];

            // Start central background monitoring
            if (settings.PingAutostart)
            {
                BackgroundPingService.Instance.Start();
            }
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
            if (sender is TextBox textBox && textBox.IsVisible)
            {
                // Ensures both logical and keyboard focus
                textBox.Focus();
                Keyboard.Focus(textBox);

                // Optional: Select all text so typing immediately overwrites previous search
                textBox.SelectAll();
            }
        }

        /// <summary>Finds the map canvas of the tab that is currently shown (null if no map is open).</summary>
        private static MapCanvasView? FindActiveMapCanvas(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is MapCanvasView view && view.IsVisible) return view;

                var nested = FindActiveMapCanvas(child);
                if (nested != null) return nested;
            }
            return null;
        }

        /// <summary>
        /// Gives keyboard focus to the active map canvas. Map shortcuts (F2, Delete, Ctrl+C/V/Z, arrows) are
        /// handled by the canvas, so they only work while it has focus. Falls back to the window if no map is open.
        /// </summary>
        private void FocusActiveMap()
        {
            var canvas = FindActiveMapCanvas(this);
            if (canvas != null)
            {
                canvas.Focus();
                Keyboard.Focus(canvas);
            }
            else
            {
                this.Focus();
                Keyboard.Focus(this);
            }
        }

        private void TxtSearch_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // F2 (properties) normally needs focus on the map. Forward it so it also works straight after a search,
            // when the cursor is still in the search box and the found device is the selected one.
            if (e.Key == Key.F2)
            {
                var canvas = FindActiveMapCanvas(this);
                if (canvas != null) MapKeyboardService.HandlePreviewKeyDown(e, canvas);
                return;
            }

            if (e.Key == Key.Enter)
            {
                e.Handled = true; // Stops Enter from performing default WPF focus navigation

                // 1. Run search command
                if (DataContext is MainViewModel vm && vm.PerformSearchCommand.CanExecute(null))
                {
                    vm.PerformSearchCommand.Execute(null);
                }

                // 2. Explicitly return focus to txtSearch
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    txtSearch.Focus();
                    Keyboard.Focus(txtSearch);

                    // Optional: Choose text cursor behavior:
                    txtSearch.SelectAll(); // Highlight text so typing replaces it
                                           // OR:
                                           // txtSearch.CaretIndex = txtSearch.Text.Length; // Place cursor at the end
                }), DispatcherPriority.Input);
            }
            if (e.Key == Key.Escape)
            {
                e.Handled = true; // Stops Escape from performing default WPF focus navigation
                // 1. Close the search box
                if (DataContext is MainViewModel vm && vm.ToggleSearchCloseCommand.CanExecute(null))
                {
                    vm.ToggleSearchCloseCommand.Execute(null);
                }
                // 2. Return focus to the map (not just the window), so F2 / Delete / Ctrl+C work right away
                Dispatcher.BeginInvoke(new Action(FocusActiveMap), DispatcherPriority.Input);
            }
        }
    }
}