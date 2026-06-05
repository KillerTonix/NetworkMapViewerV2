using System.Windows;

namespace NetworkMapViewerV2.Views
{
    /// <summary>
    /// Interaction logic for HelpWindow.xaml
    /// </summary>
    public partial class HelpWindow : Window
    {
        public HelpWindow(int tabIndex = 0)
        {
            InitializeComponent();
            TabControlObject.SelectedIndex = tabIndex;
        }
    }
}
