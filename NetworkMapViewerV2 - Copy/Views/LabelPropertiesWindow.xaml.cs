using NetworkMapViewerV2.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NetworkMapViewerV2.Views
{
    public partial class LabelPropertiesWindow : Window
    {
        public NetworkLabel EditingLabel { get; private set; }

        public LabelPropertiesWindow(NetworkLabel label, bool isEditMode)
        {
            InitializeComponent();
            EditingLabel = label;

            // --- LOAD DATA INTO UI ---
            txtText.Text = string.Join(Environment.NewLine, label.TextLines);

            txtWidth.Text = label.Width > 0 ? label.Width.ToString() : "";
            txtHeight.Text = label.Height > 0 ? label.Height.ToString() : "";

            // --- LOAD COLORS INTO XCEED COLOR PICKERS ---
            cpBgColor.SelectedColor = ParseColorString(label.Background, Colors.Transparent);
            cpBorderColor.SelectedColor = ParseColorString(label.BorderBrush, Colors.Transparent);
            cpFontColor.SelectedColor = ParseColorString(label.Foreground, Colors.Black);

            txtFontFamily.Text = string.IsNullOrEmpty(label.FontFamily) ? "Segoe UI" : label.FontFamily;
            txtFontSize.Text = label.FontSize.ToString();

            btnBold.IsChecked = label.FontWeight == "Bold";
            btnItalic.IsChecked = label.FontStyle == "Italic";

            if (btnUnderline != null) btnUnderline.IsChecked = false;

            SetComboBoxByContent(cmbHAlign, label.HorizontalAlignment);
            SetComboBoxByContent(cmbVAlign, label.VerticalAlignment);

            // --- TOGGLE EDIT MODE ---
            if (isEditMode)
            {
                txtText.IsReadOnly = false;
                cmbHAlign.IsEnabled = true;
                cmbVAlign.IsEnabled = true;

                cpBgColor.IsEnabled = true;
                cpBorderColor.IsEnabled = true;
                cpFontColor.IsEnabled = true;

                txtWidth.IsReadOnly = false;
                txtHeight.IsReadOnly = false;
                txtFontFamily.IsReadOnly = false;
                txtFontSize.IsReadOnly = false;
                btnBold.IsEnabled = true;
                btnItalic.IsEnabled = true;

                if (btnUnderline != null) btnUnderline.IsEnabled = false;
            }
            else
            {
                txtText.IsReadOnly = true;
                cmbHAlign.IsEnabled = false;
                cmbVAlign.IsEnabled = false;

                cpBgColor.IsEnabled = false;
                cpBorderColor.IsEnabled = false;
                cpFontColor.IsEnabled = false;

                txtWidth.IsReadOnly = true;
                txtHeight.IsReadOnly = true;
                txtFontFamily.IsReadOnly = true;
                txtFontSize.IsReadOnly = true;
                btnBold.IsEnabled = false;
                btnItalic.IsEnabled = false;

                if (btnUnderline != null) btnUnderline.IsEnabled = false;
                this.Title += " {View Mode}";
            }
        }

        // --- HELPER: Safely convert Database Strings (Hex/Names) to WPF Colors ---
        private Color ParseColorString(string colorStr, Color fallbackColor)
        {
            if (string.IsNullOrWhiteSpace(colorStr)) return fallbackColor;
            try
            {
                var converted = ColorConverter.ConvertFromString(colorStr);
                if (converted is Color c) return c;
            }
            catch { }

            return fallbackColor;
        }

        private void SetComboBoxByContent(ComboBox cb, string targetValue)
        {
            if (string.IsNullOrEmpty(targetValue)) return;
            foreach (ComboBoxItem item in cb.Items)
            {
                if (item.Content?.ToString() == targetValue)
                {
                    cb.SelectedItem = item;
                    return;
                }
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // --- SAVE UI DATA BACK TO OBJECT ---
            EditingLabel.TextLines.Clear();
            foreach (var line in txtText.Text.Split([Environment.NewLine], StringSplitOptions.None))
                EditingLabel.TextLines.Add(line);

            // --- SAVE XCEED COLOR PICKERS BACK TO STRINGS ---
            EditingLabel.Background = cpBgColor.SelectedColor?.ToString() ?? "Transparent";
            EditingLabel.BorderBrush = cpBorderColor.SelectedColor?.ToString() ?? "Transparent";
            EditingLabel.Foreground = cpFontColor.SelectedColor?.ToString() ?? "#FF000000";

            EditingLabel.FontFamily = txtFontFamily.Text;

            if (double.TryParse(txtWidth.Text, out double w)) EditingLabel.Width = w; else EditingLabel.Width = 0;
            if (double.TryParse(txtHeight.Text, out double h)) EditingLabel.Height = h; else EditingLabel.Height = 0;
            if (double.TryParse(txtFontSize.Text, out double fSize)) EditingLabel.FontSize = fSize;

            EditingLabel.FontWeight = btnBold.IsChecked == true ? "Bold" : "Normal";
            EditingLabel.FontStyle = btnItalic.IsChecked == true ? "Italic" : "Normal";

            if (cmbHAlign.SelectedItem is ComboBoxItem hItem)
            {
                EditingLabel.HorizontalAlignment = hItem.Content?.ToString() ?? "Center";
            }

            if (cmbVAlign.SelectedItem is ComboBoxItem vItem)
            {
                EditingLabel.VerticalAlignment = vItem.Content?.ToString() ?? "Center";
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