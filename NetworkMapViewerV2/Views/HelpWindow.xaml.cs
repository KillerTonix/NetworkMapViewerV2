using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace NetworkMapViewerV2.Views
{
    public partial class HelpWindow : Window
    {
        public ICollectionView HotkeysView { get; private set; }
    
        public HelpWindow(int tabIndex = 0)
        {
            InitializeComponent();
            TabControlObject.SelectedIndex = tabIndex;
            // 1. Load the data
            var hotkeysList = GetHotkeys();

            // 2. Setup the CollectionView for grouping and filtering
            HotkeysView = CollectionViewSource.GetDefaultView(hotkeysList);
            HotkeysView.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
            HotkeysView.Filter = FilterHotkeys;

            // 3. Bind the DataContext so the XAML can see the list
            this.DataContext = this;

            var buildDateAttribute = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(attr => attr.Key == "BuildDate");

            string buildDate = buildDateAttribute?.Value ?? "Unknown";

            txtBuildDate.Text = $"Build Date: {buildDate}";
        }

        // Live Search Filter Logic
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            HotkeysView.Refresh(); // Triggers the filter to re-evaluate
        }

        private bool FilterHotkeys(object item)
        {
            if (string.IsNullOrWhiteSpace(txtSearch.Text)) return true;

            if (item is not HotkeyItem hotkey) return false;

            string query = txtSearch.Text.ToLower();

            // Search both the key combo and the description!
            return hotkey.KeyCombo.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   hotkey.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   hotkey.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        }

        // The Master Database of Hotkeys
        private static List<HotkeyItem> GetHotkeys()
        {
            return
            [
                // File & Tab Operations
                new("File & Tab Operations", "Ctrl + N", "Create a New Map"),
                new("File & Tab Operations", "Ctrl + O", "Open an existing Map"),
                new("File & Tab Operations", "Ctrl + S", "Save current map to database"),
                new("File & Tab Operations", "Ctrl + R", "Reload map from database"),
                new("File & Tab Operations", "Ctrl + W", "Close current tab"),
                new("File & Tab Operations", "Alt + F4", "Exit Application"),

                // Editing & Map Layout
                new("Editing & Map Layout", "Ctrl + E", "Toggle Edit Mode ON / OFF"),
                new("Editing & Map Layout", "Ctrl + G", "Toggle Grid Snapping / Visibility"),
                new("Editing & Map Layout", "F2", "Open Properties for selected item"),
                new("Editing & Map Layout", "Delete", "Remove selected item(s) from map"),
                new("Editing & Map Layout", "Ctrl + Z", "Undo last editing action"),
                new("Editing & Map Layout", "Ctrl + X", "Cut selected items"),
                new("Editing & Map Layout", "Ctrl + C / Ctrl + V", "Copy / Paste items (with offset)"),
                new("Editing & Map Layout", "Ctrl + Shift + V", "Paste in Place (exact original coordinates)"),
                new("Editing & Map Layout", "Ctrl + H", "Find and Replace text/IPs"),
                new("Editing & Map Layout", "Ctrl + F2", "Find and recover Out-of-Bounds devices"),
                new("Editing & Map Layout", "Alt + A", "Auto-Align: Snaps Devices to nearest Label"),
                new("Editing & Map Layout", "Alt + Arrows / C / S", "Manual Alignment: Align edges/centers of items"),

                // Network & Diagnostics              
                new("Network & Diagnostics", "Ctrl + F5", "Open Ping Options/Settings"),

                // Tools, Logs & Search
                new("Tools, Logs & Search", "Ctrl + Shift + Space", "Open Global Spotlight Search (Anywhere)"),
                new("Tools, Logs & Search", "Ctrl + F", "Toggle Standard Window Search"),
                new("Tools, Logs & Search", "Ctrl + Shift + F", "Open Search Options"),
                new("Tools, Logs & Search", "F11", "Open Notifications Panel"),
                new("Tools, Logs & Search", "Ctrl + F11", "Open Notification Options"),
                new("Tools, Logs & Search", "F12", "Generate Network Report"),
                new("Tools, Logs & Search", "Ctrl + L", "View Action/Audit Logs"),
                new("Tools, Logs & Search", "Ctrl + K", "Open Application Options"),

                // Help
                new("Help", "F1", "Show About Window"),
                new("Help", "Shift + F1", "Show Hotkeys Window")
            ];
        }
    }

    // The Model Class
    public class HotkeyItem(string category, string keyCombo, string description)
    {
        public string Category { get; set; } = category;
        public string KeyCombo { get; set; } = keyCombo;
        public string Description { get; set; } = description;
    }
}