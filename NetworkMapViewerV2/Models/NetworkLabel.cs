using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace NetworkMapViewerV2.Models
{
    public partial class NetworkLabel : ObservableObject
    {
        // --- RELATIONAL IDs ---
        [ObservableProperty] private int _labelId;
        [ObservableProperty] private int _mapId;

        // --- POSITIONING & SIZE ---
        [ObservableProperty] private double _left;
        [ObservableProperty] private double _top;
        [ObservableProperty] private double _width = 125;
        [ObservableProperty] private double _height = 120;

        // --- STYLING (Modern WPF Names) ---
        [ObservableProperty] private string _background = "Transparent";
        [ObservableProperty] private string _borderBrush = "Transparent";
        [ObservableProperty] private int _borderThickness = 0;

        [ObservableProperty] private string _horizontalAlignment = "Left"; // Left, Center, Right
        [ObservableProperty] private string _verticalAlignment = "Top";   // Top, Center, Bottom

        // --- TYPOGRAPHY ---
        [ObservableProperty] private string _fontFamily = "Segoe UI";
        [ObservableProperty] private double _fontSize = 12;
        [ObservableProperty] private string _fontStyle = "Normal"; // Normal, Italic, Oblique
        [ObservableProperty] private string _fontWeight = "Normal"; // Normal, Bold
        [ObservableProperty] private string _foreground = "#000000";

        // --- DATA ---
        // Maps to your TextJson column in the DB
        [ObservableProperty] private ObservableCollection<string> _textLines = [];
    }
}