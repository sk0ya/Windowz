using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowzTabManager.Converters;

public class PathToIconConverter : IValueConverter
{
    private static readonly Dictionary<string, ImageSource?> IconCache = new();

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

        string cacheKey = path.ToLowerInvariant();
        if (IconCache.TryGetValue(cacheKey, out var cached))
            return cached;

        ImageSource? icon = null;
        try
        {
            string fullPath = path;
            if (!Path.IsPathRooted(path))
                fullPath = ResolveFromPath(path);

            if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
            {
                using var systemIcon = Icon.ExtractAssociatedIcon(fullPath);
                if (systemIcon != null)
                {
                    icon = Imaging.CreateBitmapSourceFromHIcon(
                        systemIcon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    icon.Freeze();
                }
            }
        }
        catch
        {
            // ignore and cache null
        }

        IconCache[cacheKey] = icon;
        return icon;
    }

    private static string ResolveFromPath(string command)
    {
        string[] extensions = [".exe", ".cmd", ".bat", ".com", ""];
        string pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var directories = pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (string directory in directories)
        {
            foreach (string extension in extensions)
            {
                string fullPath = Path.Combine(directory, command + extension);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        return command;
    }
}
