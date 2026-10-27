using NetworkMapViewerV2.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace NetworkMapViewerV2.Views
{
    // Part: Highlight
    // Scrolls to and animates a device whose highlight was requested
    // through GlobalViewModel.HighlightedDeviceId.
    public partial class MapCanvasView : UserControl
    {

        /// <summary>
        /// If a device highlight was requested, waits until the canvas is laid out, scrolls the device into
        /// view, plays the ripple and then resets the request.
        /// </summary>
        private void CheckForPendingHighlight()
        {
            if (GlobalViewModel != null && GlobalViewModel.HighlightedDeviceId > 0)
            {
                int targetId = GlobalViewModel.HighlightedDeviceId;

                // DispatcherPriority.Loaded forces WPF to wait until all elements are fully drawn and arranged!
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (UIElement child in DrawingCanvas.Children)
                    {
                        if (child is Border b && b.Tag is NetworkDevice d && d.DeviceId == targetId)
                        {
                            b.BringIntoView();
                            double cx = Canvas.GetLeft(b) + (b.ActualWidth / 2);
                            double cy = Canvas.GetTop(b) + (b.ActualHeight / 2);

                            PlayRippleAnimation(DrawingCanvas, cx, cy);

                            GlobalViewModel.HighlightedDeviceId = 0; // Clear the signal
                            break;
                        }
                    }
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        /// <summary>
        /// Plays expanding, fading rings centered on the given point; each ring removes itself when its
        /// animation completes.
        /// </summary>
        private static void PlayRippleAnimation(Canvas canvas, double centerX, double centerY)
        {
            const double startSize = 10;
            const double endSize = 220;
            int ringCount = 5;
            var ringColor = Color.FromArgb(200, 255, 200, 0); // bright gold

            for (int i = 0; i < ringCount; i++)
            {
                var ring = new Ellipse
                {
                    Width = startSize,
                    Height = startSize,
                    Fill = Brushes.Transparent,
                    Stroke = new SolidColorBrush(ringColor),
                    StrokeThickness = 10,
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(ring, centerX - startSize / 2);
                Canvas.SetTop(ring, centerY - startSize / 2);
                Panel.SetZIndex(ring, 999);
                canvas.Children.Add(ring);

                var delay = TimeSpan.FromMilliseconds(i * 200);
                var duration = TimeSpan.FromMilliseconds(800);

                var animWidth = new DoubleAnimation { From = startSize, To = endSize, Duration = duration, BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var animHeight = new DoubleAnimation { From = startSize, To = endSize, Duration = duration, BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var animLeft = new DoubleAnimation { From = centerX - startSize / 2, To = centerX - endSize / 2, Duration = duration, BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var animTop = new DoubleAnimation { From = centerY - startSize / 2, To = centerY - endSize / 2, Duration = duration, BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var animOpacity = new DoubleAnimation { From = 1.0, To = 0.0, Duration = duration, BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
                var animStroke = new DoubleAnimation { From = 3.0, To = 0.5, Duration = duration, BeginTime = delay };

                animOpacity.Completed += (s, e) => canvas.Children.Remove(ring);

                ring.BeginAnimation(FrameworkElement.WidthProperty, animWidth);
                ring.BeginAnimation(FrameworkElement.HeightProperty, animHeight);
                ring.BeginAnimation(Canvas.LeftProperty, animLeft);
                ring.BeginAnimation(Canvas.TopProperty, animTop);
                ring.BeginAnimation(UIElement.OpacityProperty, animOpacity);
                ring.BeginAnimation(Shape.StrokeThicknessProperty, animStroke);
            }
        }
    }
}
