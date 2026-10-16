using NetworkMapViewerV2.Data;
using NetworkMapViewerV2.Models;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace NetworkMapViewerV2.Views
{
    public partial class AuditLogWindow : Window
    {
        // Colors for diff highlighting
        private static readonly SolidColorBrush RedBg = new((Color)ColorConverter.ConvertFromString("#FEE2E2"));  // Soft Red
        private static readonly SolidColorBrush RedFg = new((Color)ColorConverter.ConvertFromString("#991B1B"));  // Dark Red Text
        private static readonly SolidColorBrush GreenBg = new((Color)ColorConverter.ConvertFromString("#DCFCE7"));// Soft Green
        private static readonly SolidColorBrush GreenFg = new((Color)ColorConverter.ConvertFromString("#166534"));// Dark Green Text
        private static readonly SolidColorBrush DefaultFg = new((Color)ColorConverter.ConvertFromString("#334155"));

        private List<AuditLog> _allLogs = new();

        // Callback event when user double-clicks a log row to navigate to device
        public Action<string, int, string>? OnSelectDeviceRequested { get; set; }
        public AuditLogWindow()
        {
            InitializeComponent();
            LoadLogs();
        }

        private void LoadLogs()
        {
            var repo = new MapRepository();
            _allLogs = repo.GetAuditLogs() ?? new List<AuditLog>();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (dgAuditLogs == null || _allLogs == null) return;

            string query = txtSearch?.Text?.Trim().ToLower() ?? "";
            string selectedAction = (cmbActionFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Actions";

            var filtered = _allLogs.Where(log =>
                log != null &&
                (selectedAction == "All Actions" || string.Equals(log.ActionType, selectedAction, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(query) ||
                 (log.UserName?.ToLower().Contains(query) ?? false) ||
                 (log.MapName?.ToLower().Contains(query) ?? false) ||
                 (log.Target?.ToLower().Contains(query) ?? false) ||
                 (log.Details?.ToLower().Contains(query) ?? false))
            ).ToList();

            dgAuditLogs.ItemsSource = filtered;
        }

        private void DgAuditLogs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgAuditLogs.SelectedItem is AuditLog selectedLog)
            {
                RenderHighlightedDiff(rtbOldValue, selectedLog.OldValue, selectedLog.NewValue, isOld: true);
                RenderHighlightedDiff(rtbNewValue, selectedLog.OldValue, selectedLog.NewValue, isOld: false);
            }
            else
            {
                rtbOldValue.Document.Blocks.Clear();
                rtbNewValue.Document.Blocks.Clear();
            }
        }

        /// <summary>
        /// Parses JSON strings line-by-line and highlights lines that differ between Old and New states.
        /// </summary>
        private void RenderHighlightedDiff(RichTextBox rtb, string? oldJson, string? newJson, bool isOld)
        {
            var doc = new FlowDocument { PagePadding = new Thickness(2) };

            string targetText = isOld ? (oldJson ?? "") : (newJson ?? "");
            string otherText = isOld ? (newJson ?? "") : (oldJson ?? "");

            if (string.IsNullOrWhiteSpace(targetText))
            {
                doc.Blocks.Add(new Paragraph(new Run(isOld ? "No previous state recorded." : "No updated state recorded."))
                {
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0)
                });
                rtb.Document = doc;
                return;
            }

            string[] targetLines = targetText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            string[] otherLines = otherText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var activeBg = isOld ? RedBg : GreenBg;
            var activeFg = isOld ? RedFg : GreenFg;

            for (int i = 0; i < targetLines.Length; i++)
            {
                string line = targetLines[i];
                bool isLineChanged = false;

                // Compare line against corresponding line in other state
                if (i < otherLines.Length)
                {
                    if (line.Trim() != otherLines[i].Trim())
                    {
                        isLineChanged = true;
                    }
                }
                else
                {
                    isLineChanged = true; // Extra line added or removed
                }

                // Ignore plain opening/closing structural JSON braces
                string trimmed = line.Trim();
                if (trimmed == "{" || trimmed == "}" || trimmed == "[" || trimmed == "]" || trimmed == "}," || trimmed == "],")
                {
                    isLineChanged = false;
                }

                var paragraph = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                var run = new Run(line);

                if (isLineChanged)
                {
                    run.Background = activeBg;
                    run.Foreground = activeFg;
                    run.FontWeight = FontWeights.SemiBold;
                }
                else
                {
                    run.Foreground = DefaultFg;
                }

                paragraph.Inlines.Add(run);
                doc.Blocks.Add(paragraph);
            }

            rtb.Document = doc;
        }

        private void DgAuditLogs_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (dgAuditLogs.SelectedItem is AuditLog selectedLog)
            {
                if (string.IsNullOrWhiteSpace(selectedLog.MapName)) return;

                // Extract IP Address from log details as fallback
                string ipAddress = "";
                var ipMatch = Regex.Match(selectedLog.Details ?? "", @"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b");
                if (ipMatch.Success)
                {
                    ipAddress = ipMatch.Value;
                }

                // 1. Fire navigation callback with MapName, TargetID, and IP Address
                OnSelectDeviceRequested?.Invoke(selectedLog.MapName, selectedLog.TargetID, ipAddress);

                // 2. Close modal window so the highlighted map canvas is visible
                this.Close();
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
        private void CmbActionFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => LoadLogs();
    }
}