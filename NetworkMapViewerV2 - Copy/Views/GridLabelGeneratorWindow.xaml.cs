using NetworkMapViewerV2.Models;
using System.Windows;

namespace NetworkMapViewerV2.Views
{
    public partial class GridLabelGeneratorWindow : Window
    {
        public List<NetworkLabel> GeneratedLabels { get; private set; } = [];
        private readonly int _targetMapId;

        public GridLabelGeneratorWindow(int targetMapId, Point startLocation)
        {
            InitializeComponent();
            _targetMapId = targetMapId;           
        }

        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(txtWidth.Text, out double sizeX) ||
                !double.TryParse(txtHeight.Text, out double sizeY) ||
                !double.TryParse(txtStartX.Text, out double startLeft) ||
                !double.TryParse(txtStartY.Text, out double startTop) ||
                !double.TryParse(txtGapX.Text, out double gapX) ||
                !double.TryParse(txtGapY.Text, out double gapY))
            {
                MessageBox.Show("Please enter valid numbers for the dimensions and gaps.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GeneratedLabels.Clear();
            string[] lines = txtGridData.Text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

            for (int row = 0; row < lines.Length; row++)
            {
                string[] cells = lines[row].Split([','], StringSplitOptions.RemoveEmptyEntries);

                for (int col = 0; col < cells.Length; col++)
                {
                    string cell = cells[col].Trim();

                    if (string.IsNullOrEmpty(cell) || cell.StartsWith('0'))
                        continue; // Skip empty space

                    string brushColor = "Gray"; // Default WPF color
                    string text = "";

                    // Parse the 1:Color:Text format
                    if (cell.Contains(':'))
                    {
                        var parts = cell.Split(':');
                        if (parts.Length >= 3)
                        {
                            brushColor = parts[1].Trim();
                            // If the text itself contains colons, rejoin it
                            text = string.Join(":", parts, 2, parts.Length - 2).Trim();
                        }
                    }                   

                    double currentLeft = startLeft + (col * (sizeX + gapX));
                    double currentTop = startTop + (row * (sizeY + gapY));

                    var newLabel = new NetworkLabel
                    {
                        MapId = _targetMapId,
                        Left = currentLeft,
                        Top = currentTop,
                        Width = sizeX,
                        Height = sizeY,
                        Background = brushColor,
                        BorderBrush = "Black",
                        Foreground = "White",
                        FontSize = 10,
                        FontFamily = "Segoe UI",
                        HorizontalAlignment = "Left",
                        VerticalAlignment = "Top"                        
                    };

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        newLabel.TextLines.Add(text);
                    }

                    GeneratedLabels.Add(newLabel);
                }
            }

            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                this.Close();
        }
    }
}