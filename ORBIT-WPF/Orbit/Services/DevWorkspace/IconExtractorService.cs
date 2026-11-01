using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbit.Core;
using Orbit.Platform.Windows;

namespace Orbit.Services.DevWorkspace;

public sealed class IconExtractorService : IIconExtractorService
{
    private const string LogCategory = "ICON_EXTRACTOR";
    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ImageSource? GetOrExtractIcon(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;

        // Clean quotes or arguments if present in path string
        string cleanPath = CleanPath(filePath);
        if (string.IsNullOrWhiteSpace(cleanPath)) return null;

        if (_cache.TryGetValue(cleanPath, out var cached))
        {
            return cached;
        }

        ImageSource? icon = ExtractIconInternal(cleanPath);
        _cache[cleanPath] = icon;
        return icon;
    }

    private static string CleanPath(string raw)
    {
        string p = raw.Trim().Trim('"', '\'');
        if (p.Contains(".exe,", StringComparison.OrdinalIgnoreCase))
        {
            p = p.Split(',')[0].Trim();
        }
        else if (p.Contains(".ico,", StringComparison.OrdinalIgnoreCase))
        {
            p = p.Split(',')[0].Trim();
        }
        return p;
    }

    private static ImageSource? ExtractIconInternal(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return null;
            }

            // 0. Direct image file (.png, .ico, .jpg, .jpeg, .bmp)
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
                catch (Exception ex)
                {
                    OrbitLogger.Debug(LogCategory, $"Failed to load image directly for '{path}': {ex.Message}");
                }
            }

            // 1. Primary: Native Windows Shell SHGetFileInfo for large icon
            var shinfo = new NativeMethods.SHFILEINFO();
            IntPtr result = NativeMethods.SHGetFileInfo(
                path,
                0,
                ref shinfo,
                (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
                NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);

            if (result != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
            {
                try
                {
                    var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                        shinfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    bitmapSource.Freeze();
                    return bitmapSource;
                }
                finally
                {
                    NativeMethods.DestroyIcon(shinfo.hIcon);
                }
            }

            // 2. Secondary fallback: System.Drawing Icon.ExtractAssociatedIcon for .exe files
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (sysIcon != null)
                {
                    IntPtr hIcon = sysIcon.Handle;
                    var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                        hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    bitmapSource.Freeze();
                    return bitmapSource;
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Debug(LogCategory, $"Failed to extract icon for '{path}': {ex.Message}");
        }

        return null;
    }
}

