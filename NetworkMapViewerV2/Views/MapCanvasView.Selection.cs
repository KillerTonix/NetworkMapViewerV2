using NetworkMapViewerV2.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NetworkMapViewerV2.Views
{
    // Part: Selection
    // Marquee (rubber-band) selection on the canvas background and
    // selection state / glow handling.
    public partial class MapCanvasView : UserControl
    {

        /// <summary>Starts a marquee selection when the empty canvas background is clicked in edit mode.</summary>
        private void DrawingCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Only start marquee if we click the empty canvas background, NOT an icon
            if (e.OriginalSource == DrawingCanvas && _currentState != null && _currentState.IsEditingEnabled)
            {
                SelectElement(null, false); // Clear old selection

                _isMarqueeSelecting = true;
                _selectionStartPoint = e.GetPosition(DrawingCanvas);

                _selectionBox = new Rectangle
                {
                    Stroke = Brushes.DodgerBlue,
                    StrokeThickness = 1,
                    Fill = new SolidColorBrush(Color.FromArgb(40, 30, 144, 255)), // Semi-transparent blue
                    StrokeDashArray = [2, 2]
                };

                Canvas.SetLeft(_selectionBox, _selectionStartPoint.X);
                Canvas.SetTop(_selectionBox, _selectionStartPoint.Y);
                Panel.SetZIndex(_selectionBox, 999);

                DrawingCanvas.Children.Add(_selectionBox);
                DrawingCanvas.CaptureMouse();
                this.Loaded += (s, e) => this.Focus();
            }
        }

        /// <summary>Resizes the marquee rectangle while dragging; works in any drag direction.</summary>
        private void DrawingCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isMarqueeSelecting && _selectionBox != null)
            {
                Point currentPoint = e.GetPosition(DrawingCanvas);

                // Calculate the true X/Y and Width/Height (allows dragging backwards!)
                double x = Math.Min(currentPoint.X, _selectionStartPoint.X);
                double y = Math.Min(currentPoint.Y, _selectionStartPoint.Y);
                double width = Math.Abs(currentPoint.X - _selectionStartPoint.X);
                double height = Math.Abs(currentPoint.Y - _selectionStartPoint.Y);

                Canvas.SetLeft(_selectionBox, x);
                Canvas.SetTop(_selectionBox, y);
                _selectionBox.Width = width;
                _selectionBox.Height = height;
            }
        }

        /// <summary>Finishes the marquee: selects every device and label it intersects, then removes the rectangle.</summary>
        private void DrawingCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isMarqueeSelecting && _selectionBox != null)
            {
                _isMarqueeSelecting = false;
                DrawingCanvas.ReleaseMouseCapture();

                // 1. Calculate the bounding box of our selection rectangle
                Rect selectionRect = new(Canvas.GetLeft(_selectionBox), Canvas.GetTop(_selectionBox), _selectionBox.Width, _selectionBox.Height);

                // 2. Loop through all children and see if they intersect with the box!
                foreach (UIElement child in DrawingCanvas.Children)
                {
                    if (child is Border b && (b.Tag is NetworkDevice || b.Tag is NetworkLabel))
                    {
                        Rect elementBounds = new(Canvas.GetLeft(b), Canvas.GetTop(b), b.ActualWidth, b.ActualHeight);
                        if (selectionRect.IntersectsWith(elementBounds))
                        {
                            SelectElement(b, true); // True = Multi-select!
                        }
                    }
                }

                // 3. Clean up the blue rectangle
                DrawingCanvas.Children.Remove(_selectionBox);
                _selectionBox = null;
            }
        }

        /// <summary>
        /// Selects an element (cyan glow). With multiSelect false the previous selection is cleared first;
        /// passing null just clears the selection.
        /// </summary>
        internal void SelectElement(FrameworkElement? element, bool multiSelect)
        {
            // Clear old selection if we aren't holding CTRL
            if (!multiSelect)
            {
                if (_selectedElements != null)
                {
                    foreach (var el in _selectedElements)
                    {
                        el.Effect = null; // Remove glow
                    }
                    _selectedElements.Clear();
                }
            }

            if (element != null && !_selectedElements?.Contains(element) == true)
            {
                _selectedElements?.Add(element);
                this.Loaded += (s, e) =>
                {
                    this.Focus();
                    Keyboard.Focus(this);
                };
                element.Effect = _selectionGlow; // Apply visual cyan glow!
            }
        }
    }
}
