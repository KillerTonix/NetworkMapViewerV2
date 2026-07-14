using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace NetworkMapViewerV2.Helpers // Ensure this matches your namespace!
{
    public class PingStatusImageConverter : IValueConverter
    {
        private static readonly Dictionary<string, BitmapImage> _imageCache = [];

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? basePath = parameter?.ToString();
            if (string.IsNullOrWhiteSpace(basePath)) return null;
            
            
            bool isOnline = true;

            if (value is bool actualStatus)
            {
                isOnline = actualStatus;
            }

            string targetPath = isOnline ? basePath : basePath.Replace("\\ON\\", "\\OFF\\", StringComparison.OrdinalIgnoreCase);

            if (!File.Exists(targetPath))
            {
                targetPath = basePath;
                if (!File.Exists(targetPath)) return null;
            }

            // 4. Load, Freeze, and Cache the image
            if (!_imageCache.TryGetValue(targetPath, out BitmapImage? value1))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(targetPath, UriKind.Absolute);
                bmp.EndInit();

                bmp.Freeze();
                value1 = bmp;
                _imageCache[targetPath] = value1;
            }

            return value1;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}