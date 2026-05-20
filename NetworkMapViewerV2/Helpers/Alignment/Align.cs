using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.ViewModels;
using NetworkMapViewerV2.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NetworkMapViewerV2.Helpers.Alignment
{
    internal class Align
    {
        public enum AlignMode { Left, Center, Right, Top, Middle, Bottom }

        public static void AlignSelectedElements(List<FrameworkElement> _selectedElements,MainViewModel GlobalViewModel, MapCanvasView mapCanvas, AlignMode mode)
        {
            if (_selectedElements == null || _selectedElements.Count < 2) return;

            double outerMinX = double.MaxValue, outerMinY = double.MaxValue;
            double outerMaxX = double.MinValue, outerMaxY = double.MinValue;

            double innerMinX = double.MaxValue, innerMinY = double.MaxValue;
            double innerMaxX = double.MinValue, innerMaxY = double.MinValue;

            var outerBounds = new Dictionary<FrameworkElement, Rect>();
            var innerBounds = new Dictionary<FrameworkElement, Rect>();

            // 1. Calculate TWO sets of boundaries (Outer = Whole Container + Text, Inner = Just the Icon)
            foreach (var el in _selectedElements)
            {
                if (el is FrameworkElement fw)
                {
                    // --- OUTER BOUNDS (Accounts for wide text and negative margins) ---
                    double currentLeft = double.IsNaN(Canvas.GetLeft(fw)) ? 0 : Canvas.GetLeft(fw);
                    double currentTop = double.IsNaN(Canvas.GetTop(fw)) ? 0 : Canvas.GetTop(fw);

                    double visualLeft = currentLeft + fw.Margin.Left;
                    double visualTop = currentTop + fw.Margin.Top;

                    Rect outer = new Rect(visualLeft, visualTop, fw.ActualWidth, fw.ActualHeight);
                    outerBounds[fw] = outer;

                    if (outer.Left < outerMinX) outerMinX = outer.Left;
                    if (outer.Top < outerMinY) outerMinY = outer.Top;
                    if (outer.Right > outerMaxX) outerMaxX = outer.Right;
                    if (outer.Bottom > outerMaxY) outerMaxY = outer.Bottom;

                    // --- INNER BOUNDS (Finds just the Image Icon) ---
                    Image? img = FindVisualChild<Image>(fw);
                    FrameworkElement targetVisual = img != null ? (FrameworkElement)img : fw;

                    try
                    {
                        var transform = targetVisual.TransformToAncestor(mapCanvas.DrawingCanvas);
                        Point topLeft = transform.Transform(new Point(0, 0));

                        // 3-pixel inset ONLY for the icons to ignore the cyan glow
                        double inset = (img != null) ? 3 : 0;
                        Rect inner = new Rect(
                            topLeft.X + inset,
                            topLeft.Y + inset,
                            Math.Max(0, targetVisual.ActualWidth - (inset * 2)),
                            Math.Max(0, targetVisual.ActualHeight - (inset * 2))
                        );

                        innerBounds[fw] = inner;

                        if (inner.Left < innerMinX) innerMinX = inner.Left;
                        if (inner.Top < innerMinY) innerMinY = inner.Top;
                        if (inner.Right > innerMaxX) innerMaxX = inner.Right;
                        if (inner.Bottom > innerMaxY) innerMaxY = inner.Bottom;
                    }
                    catch
                    {
                        innerBounds[fw] = outer; // Fallback just in case
                    }
                }
            }

            double innerCenterX = innerMinX + (innerMaxX - innerMinX) / 2;
            double innerCenterY = innerMinY + (innerMaxY - innerMinY) / 2;

            // 2. Apply movement depending on the Mode chosen!
            foreach (var el in _selectedElements)
            {
                if (el is FrameworkElement fw && outerBounds.ContainsKey(fw) && innerBounds.ContainsKey(fw))
                {
                    Rect outer = outerBounds[fw];
                    Rect inner = innerBounds[fw];

                    double deltaX = 0;
                    double deltaY = 0;

                    switch (mode)
                    {
                        // Edges use OUTER BOUNDS so the long text labels act as the hard boundary
                        case AlignMode.Left: deltaX = outerMinX - outer.Left; break;
                        case AlignMode.Right: deltaX = outerMaxX - outer.Right; break;
                        case AlignMode.Top: deltaY = outerMinY - outer.Top; break;
                        case AlignMode.Bottom: deltaY = outerMaxY - outer.Bottom; break;

                        // Center uses INNER BOUNDS so the physical computer icons line up!
                        case AlignMode.Center:
                            double currentInnerCenterX = inner.Left + (inner.Width / 2);
                            deltaX = innerCenterX - currentInnerCenterX;
                            break;
                        case AlignMode.Middle:
                            double currentInnerCenterY = inner.Top + (inner.Height / 2);
                            deltaY = innerCenterY - currentInnerCenterY;
                            break;
                    }

                    // Apply the final calculated movement shift
                    double currentLeft = double.IsNaN(Canvas.GetLeft(fw)) ? 0 : Canvas.GetLeft(fw);
                    double currentTop = double.IsNaN(Canvas.GetTop(fw)) ? 0 : Canvas.GetTop(fw);

                    double newLeft = currentLeft + deltaX;
                    double newTop = currentTop + deltaY;

                    Canvas.SetLeft(fw, newLeft);
                    Canvas.SetTop(fw, newTop);
                    MapCanvasView.UpdateModelPosition(fw, newLeft, newTop);
                }
            }

            if (GlobalViewModel != null) if (mapCanvas._currentState != null) mapCanvas._currentState.HasUnsavedChanges = true;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T t)
                    return t;
                else
                {
                    var childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                        return childOfChild;
                }
            }
            return null;
        }

        public static (HorizontalAlignment hAlign, VerticalAlignment vAlign, TextAlignment tAlign) ResolveLabelAlignment(NetworkLabel label)
        {
            HorizontalAlignment hAlign = HorizontalAlignment.Center;
            TextAlignment tAlign = TextAlignment.Center;

            if (label.HorizontalAlignment == "Left") { hAlign = HorizontalAlignment.Left; tAlign = TextAlignment.Left; }
            else if (label.HorizontalAlignment == "Right") { hAlign = HorizontalAlignment.Right; tAlign = TextAlignment.Right; }

            VerticalAlignment vAlign = VerticalAlignment.Center;

            if (label.VerticalAlignment == "Top") vAlign = VerticalAlignment.Top;
            else if (label.VerticalAlignment == "Bottom") vAlign = VerticalAlignment.Bottom;

            return (hAlign, vAlign, tAlign);
        }

    }
}
