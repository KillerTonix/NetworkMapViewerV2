using System.Windows;
using System.Windows.Input;

namespace NetworkMapViewerV2.Views
{
    public partial class SpotlightSearchWindow : Window
    {
        public string SearchQuery { get; private set; } = string.Empty;

        // THE FIX: This prevents the Deactivated event from causing a crash!
        private bool _isClosing = false;

        public SpotlightSearchWindow()
        {
            InitializeComponent();

            // THE FIX: Aggressive focus stealing
            this.Loaded += (s, e) =>
            {
                // 1. Force Windows OS to make this the active application
                this.Activate();

                // 2. Push the focus command to the very end of the WPF rendering queue
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    txtSpotlightSearch.Focus();
                    Keyboard.Focus(txtSpotlightSearch);
                }), System.Windows.Threading.DispatcherPriority.Input);
            };

            // macOS BEHAVIOR: If you click away to another app, the search bar vanishes
            this.Deactivated += (s, e) =>
            {
                if (_isClosing) return;

                _isClosing = true;
                this.DialogResult = false;
            };
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _isClosing = true;
                this.DialogResult = false; // Automatically closes the window
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                _isClosing = true;

                if (!string.IsNullOrWhiteSpace(txtSpotlightSearch.Text))
                {
                    SearchQuery = txtSpotlightSearch.Text.Trim();
                    this.DialogResult = true; // Automatically closes the window and returns true!
                }
                else
                {
                    this.DialogResult = false; // Automatically closes the window and returns false!
                }

                e.Handled = true;
            }

            base.OnPreviewKeyDown(e);
        }
    }
}