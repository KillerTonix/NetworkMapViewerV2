using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace NetworkMapViewerV2.Helpers // Ensure this matches your namespace!
{
    public class PingStatusImageConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 1. Get the base path from the Database (passed in as the ConverterParameter)
            string dbIconPath = parameter as string ?? "";
            if (string.IsNullOrWhiteSpace(dbIconPath)) return null;

            bool? isOnline = value as bool?;
            string finalPath = dbIconPath;

            // 2. If the device is OFFLINE (False), securely swap \ON\ to \OFF\
            if (isOnline.HasValue && isOnline.Value == false)
            {
                // StringComparison.OrdinalIgnoreCase makes sure it works even if someone typed \On\ or \on\
                finalPath = dbIconPath.Replace("\\ON\\", "\\OFF\\", StringComparison.OrdinalIgnoreCase);
            }

            // NOTE: If isOnline is NULL (meaning it hasn't been pinged yet), 
            // it will default to leaving it as the \ON\ image.

            // 3. Check if the network path actually exists before trying to load it
            if (!File.Exists(finalPath)) return null;

            // 4. Load the image and return it to the UI
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(finalPath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad; // Forces it to load into memory
                bmp.EndInit();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}