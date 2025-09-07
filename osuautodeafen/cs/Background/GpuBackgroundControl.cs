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

    /// <summary>
    ///     Render the image to the drawing context with the specified stretch mode.
    /// </summary>
    /// <param name="context"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Render(DrawingContext context)
    {
        Bitmap bmp = Bitmap!;
        Rect bounds = Bounds;
        Size srcSize = new(bmp.PixelSize.Width, bmp.PixelSize.Height);

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
                double scale = Math.Min(bw / sw, bh / sh);
                double w = sw * scale;
                double h = sh * scale;
                Point topLeft = bounds.Center - new Vector(w / 2, h / 2);
                destRect = new Rect(topLeft, new Size(w, h));
                break;
            }
            case Stretch.UniformToFill:
            {
                double scale = Math.Max(bw / sw, bh / sh);
                double w = sw * scale;
                double h = sh * scale;
                Point topLeft = bounds.Center - new Vector(w / 2, h / 2);
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