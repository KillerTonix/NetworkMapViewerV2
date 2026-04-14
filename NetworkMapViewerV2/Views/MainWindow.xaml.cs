using NetworkMapViewerV2.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace NetworkMapViewerV2.Views
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }

        public MainWindow()
        {
            InitializeComponent();

            // Create the ViewModel and assign it as the DataContext for the entire window
            ViewModel = new MainViewModel();
            this.DataContext = ViewModel;
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