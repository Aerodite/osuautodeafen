using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

public class GpuBackgroundControl : Control
{
    public static readonly StyledProperty<Bitmap?> BitmapProperty =
        AvaloniaProperty.Register<GpuBackgroundControl, Bitmap?>(nameof(Bitmap));

    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<GpuBackgroundControl, Stretch>(nameof(Stretch), Stretch.Uniform);

    public Bitmap? Bitmap
    {
        get => GetValue(BitmapProperty);
        set => SetValue(BitmapProperty, value);
    }

    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bitmap == null) return;

        var sourceRect = new Rect(0, 0, Bitmap.PixelSize.Width, Bitmap.PixelSize.Height);
        var destRect = CalculateDestRect(Bounds, sourceRect.Size, Stretch);

        context.DrawImage(Bitmap, sourceRect, destRect);
    }

    private static Rect CalculateDestRect(Rect bounds, Size sourceSize, Stretch stretch)
    {
        switch (stretch)
        {
            case Stretch.None:
                return new Rect(bounds.TopLeft, sourceSize);
            case Stretch.Fill:
                return bounds;
            case Stretch.Uniform:
                {
                    var scale = Math.Min(bounds.Width / sourceSize.Width, bounds.Height / sourceSize.Height);
                    var size = new Size(sourceSize.Width * scale, sourceSize.Height * scale);
                    var topLeft = bounds.Center - new Vector(size.Width / 2, size.Height / 2);
                    return new Rect(topLeft, size);
                }
            case Stretch.UniformToFill:
                {
                    var scale = Math.Max(bounds.Width / sourceSize.Width, bounds.Height / sourceSize.Height);
                    var size = new Size(sourceSize.Width * scale, sourceSize.Height * scale);
                    var topLeft = bounds.Center - new Vector(size.Width / 2, size.Height / 2);
                    return new Rect(topLeft, size);
                }
            default:
                return bounds;
        }
    }
}