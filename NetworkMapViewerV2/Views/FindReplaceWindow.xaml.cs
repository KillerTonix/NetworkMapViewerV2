using System.Windows;

namespace NetworkMapViewerV2.Views
{
    public partial class FindReplaceWindow : Window
    {
        public string FindText => txtFind.Text;
        public string ReplaceText => txtReplace.Text;

        public FindReplaceWindow()
        {
            InitializeComponent();
            txtFind.Focus(); // Auto-focus the first box when opened
        }

        private void BtnReplace_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtFind.Text))
            {
                MessageBox.Show("Please enter text to find.", "Missing Text", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}