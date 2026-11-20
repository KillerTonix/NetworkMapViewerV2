using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace NetworkMapViewerV2.Helpers // Ensure this matches your namespace!
{
    public class PingStatusImageConverter : IValueConverter
    {
        private static readonly Dictionary<string, BitmapImage?> _imageCache = [];

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? basePath = parameter?.ToString();
            if (string.IsNullOrWhiteSpace(basePath)) return null;

            bool isOnline = value is not bool b || b;
            string key = (isOnline ? "ON|" : "OFF|") + basePath;

            if (!_imageCache.TryGetValue(key, out var cached))
            {
                // Load ON and OFF together while the share is reachable, so later status flips never touch the disk
                var on = TryLoad(basePath);
                var off = TryLoad(basePath.Replace("\\ON\\", "\\OFF\\", StringComparison.OrdinalIgnoreCase)) ?? on;
                _imageCache["ON|" + basePath] = on;
                _imageCache["OFF|" + basePath] = off;
                cached = isOnline ? on : off;
            }
            return cached;
        }

        private static BitmapImage? TryLoad(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();          // throws if the file is missing, so File.Exists isn't needed
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}