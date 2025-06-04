using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

public sealed class GpuBackgroundControl : Control
{
    public Bitmap? Bitmap;
    public Stretch Stretch = Stretch.Uniform;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Render(DrawingContext context)
    {
        var bmp = Bitmap!;
        var bounds = Bounds;
        var srcSize = new Size(bmp.PixelSize.Width, bmp.PixelSize.Height);

        double bw = bounds.Width, bh = bounds.Height, sw = srcSize.Width, sh = srcSize.Height;
        Rect destRect;
        switch (Stretch)
        {
            case Stretch.None:
                destRect = new Rect(bounds.TopLeft, srcSize);
                break;
            case Stretch.Fill:
                destRect = bounds;
                break;
            case Stretch.Uniform:
            {
                var scale = Math.Min(bw / sw, bh / sh);
                var w = sw * scale;
                var h = sh * scale;
                var topLeft = bounds.Center - new Vector(w / 2, h / 2);
                destRect = new Rect(topLeft, new Size(w, h));
                break;
            }
            case Stretch.UniformToFill:
            {
                var scale = Math.Max(bw / sw, bh / sh);
                var w = sw * scale;
                var h = sh * scale;
                var topLeft = bounds.Center - new Vector(w / 2, h / 2);
                destRect = new Rect(topLeft, new Size(w, h));
                break;
            }
            default:
                destRect = bounds;
                break;
        }

        context.DrawImage(bmp, new Rect(0, 0, sw, sh), destRect);
    }
}