using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Wind.Converters;

public class PathToIconConverter : IValueConverter
{
    private static readonly Dictionary<string, ImageSource?> _iconCache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return null;

        return GetIconForPath(path);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    public static ImageSource? GetIconForPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        // Normalize path for cache key
        var cacheKey = path.ToLowerInvariant();

        if (_iconCache.TryGetValue(cacheKey, out var cached))
            return cached;

        ImageSource? icon = null;

        try
        {
            // Handle commands in PATH (no extension or not a full path)
            string fullPath = path;
            if (!Path.IsPathRooted(path))
            {
                fullPath = ResolveFromPath(path);
            }

            if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
            {
                using var sysIcon = Icon.ExtractAssociatedIcon(fullPath);
                if (sysIcon != null)
                {
                    icon = Imaging.CreateBitmapSourceFromHIcon(
                        sysIcon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    icon.Freeze();
                }
            }
        }
        catch
        {
            // Ignore errors, return null
        }

        _iconCache[cacheKey] = icon;
        return icon;
    }

    private static string ResolveFromPath(string command)
    {
        var extensions = new[] { ".exe", ".cmd", ".bat", ".com", "" };
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var dirs = pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in dirs)
        {
            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(dir, command + ext);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        return command;
    }
}
