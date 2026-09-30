using System.Windows;
using System.Windows.Media;

namespace NetworkMapViewerV2.Views
{
    public partial class BatchAddLabelsWindow : Window
    {
        public int Quantity { get; private set; }
        public double LabelWidth { get; private set; }
        public double LabelHeight { get; private set; }
        public double StartLeft { get; private set; }
        public double StartTop { get; private set; }
        public string SelectedColorHex { get; private set; } = "Transparent";
        public bool IsHorizontal { get; private set; }

        public BatchAddLabelsWindow(double startX, double startY)
        {
            InitializeComponent();
            TxtLeft.Text = startX.ToString();
            TxtTop.Text = startY.ToString();
        }

        private void BtnSpawn_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtCount.Text, out int count))
            {
                Quantity = count;
                LabelWidth = 125;
                LabelHeight = 120;
                _ = double.TryParse(TxtLeft.Text, out double left); StartLeft = left;
                _ = double.TryParse(TxtTop.Text, out double top); StartTop = top;

                SelectedColorHex = ColorPicker.SelectedColor?.ToString() ?? "Transparent";
                IsHorizontal = RbHorizontal.IsChecked == true;

                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Please enter a valid quantity.");
            }
        }
    }
}