using System;
using System.Windows;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace NetworkMapViewerV2.Helpers
{
    public static class ColorHelper
    {
        public static SolidColorBrush GetColorBrush(string colorCode, SolidColorBrush? fallback = null)
        {
            if (string.IsNullOrWhiteSpace(colorCode)) return fallback ?? Brushes.Transparent;
                     
            // 3. NEW: Standard WPF Hex (#FFFFFF) or standard names ("Red")
            try
            {
                if (new BrushConverter().ConvertFromString(colorCode) is SolidColorBrush brush) return brush;
            }
            catch { }

            // 4. Safe Fallback if everything fails
            return fallback ?? Brushes.Gray;
        }
    }
}