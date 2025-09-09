using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace osuautodeafen.cs.Settings.Presets;

public class BackgroundPathToBrushConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, WeakReference<ImageBrush>> BrushCache = new();

    private const int CropWidth = 185;
    private const int CropHeight = 35;

    /// <summary>
    /// Converts a file path to an ImageBrush then crops and scales it.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="targetType"></param>
    /// <param name="parameter"></param>
    /// <param name="culture"></param>
    /// <returns></returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? path = value as string;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return new SolidColorBrush(Colors.Black);

        if (BrushCache.TryGetValue(path, out var weakRef) &&
            weakRef.TryGetTarget(out ImageBrush? cachedBrush))
        {
            return cachedBrush;
        }

        try
        {
            using Bitmap original = new Bitmap(path);
            
            PixelSize scaledSize = new PixelSize(
                Math.Max(1, original.PixelSize.Width / 4),
                Math.Max(1, original.PixelSize.Height / 4));

            RenderTargetBitmap scaled = new RenderTargetBitmap(scaledSize);
            using (DrawingContext ctx = scaled.CreateDrawingContext())
            {
                ctx.DrawImage(original,
                    new Rect(0, 0, original.Size.Width, original.Size.Height),
                    new Rect(0, 0, scaledSize.Width, scaledSize.Height));
            }
            
            int cropX = Math.Max(0, (scaledSize.Width - CropWidth) / 2);
            int cropY = Math.Max(0, (scaledSize.Height - CropHeight) / 2);
            Rect sourceRect = new Rect(cropX, cropY, CropWidth, CropHeight);

            RenderTargetBitmap cropped = new RenderTargetBitmap(new PixelSize(CropWidth, CropHeight));
            using (DrawingContext ctx = cropped.CreateDrawingContext())
            {
                ctx.DrawImage(scaled,
                    sourceRect,
                    new Rect(0, 0, CropWidth, CropHeight));
            }
            
            scaled.Dispose();
            
            ImageBrush brush = new ImageBrush(cropped)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };

            BrushCache[path] = new WeakReference<ImageBrush>(brush);
            return brush;
        }
        catch
        {
            return new SolidColorBrush(Colors.Black);
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}