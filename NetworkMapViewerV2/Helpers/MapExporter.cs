using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NetworkMapViewerV2.Helpers
{
    public static class MapExporter
    {
        /// <summary>
        /// Renders the full window and automatically crops away the top menu bar, tabs, and right sidebar,
        /// saving only the active map canvas area as a JPEG image.
        /// </summary>
        public static void SaveCanvasScreenshotToJpeg(Window window, string filePath, int qualityLevel = 95)
        {
            int winWidth = (int)window.ActualWidth;
            int winHeight = (int)window.ActualHeight;

            if (winWidth <= 0 || winHeight <= 0)
            {
                throw new InvalidOperationException("Window dimensions are invalid for taking a screenshot.");
            }

            // 1. Render entire window
            var rtb = new RenderTargetBitmap(winWidth, winHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(window);

            // 2. Find the active MapCanvasView control within the window visual tree
            FrameworkElement? canvasElement = FindVisibleMapCanvas(window);

            BitmapSource finalSource = rtb;

            if (canvasElement != null && canvasElement.ActualWidth > 0 && canvasElement.ActualHeight > 0)
            {
                // 3. Get exact coordinates of the map canvas relative to the window
                GeneralTransform transform = canvasElement.TransformToAncestor(window);
                Point origin = transform.Transform(new Point(0, 0));

                int x = (int)Math.Max(0, origin.X);
                int y = (int)Math.Max(0, origin.Y);
                int width = (int)Math.Min(winWidth - x, canvasElement.ActualWidth);
                int height = (int)Math.Min(winHeight - y, canvasElement.ActualHeight);

                if (width > 0 && height > 0)
                {
                    // 4. Crop away top menu/tabs and right-hand sidebar
                    finalSource = new CroppedBitmap(rtb, new Int32Rect(x, y, width, height));
                }
            }

            // 5. Save cropped canvas to JPEG
            var encoder = new JpegBitmapEncoder
            {
                QualityLevel = Math.Clamp(qualityLevel, 1, 100)
            };
            encoder.Frames.Add(BitmapFrame.Create(finalSource));

            using var stream = File.Create(filePath);
            encoder.Save(stream);
        }

        private static FrameworkElement? FindVisibleMapCanvas(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe && fe.IsVisible)
                {
                    if (fe.GetType().Name.Equals("MapCanvasView", StringComparison.OrdinalIgnoreCase))
                    {
                        return fe;
                    }

                    var result = FindVisibleMapCanvas(child);
                    if (result != null) return result;
                }
            }
            return null;
        }
    }
}