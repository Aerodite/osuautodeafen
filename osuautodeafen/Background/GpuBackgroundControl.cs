using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace osuautodeafen.Background;

public sealed class GpuBackgroundControl : Control
{ 
    public Bitmap? TextureA;
    public Bitmap? TextureB;

    public double Blend
    {
        get => _blend;
        set
        {
            if (Math.Abs(_blend - value) < 0.0001)
                return;

            _blend = value;

            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }
    private double _blend;
    
    private static Rect GetCoverRect(Bitmap bmp, Rect bounds)
    {
        double scale = Math.Max(
            bounds.Width / bmp.PixelSize.Width,
            bounds.Height / bmp.PixelSize.Height);

        double w = bmp.PixelSize.Width * scale;
        double h = bmp.PixelSize.Height * scale;

        double x = bounds.X + (bounds.Width - w) / 2;
        double y = bounds.Y + (bounds.Height - h) / 2;

        return new Rect(x, y, w, h);
    }
  
    public override void Render(DrawingContext context)
    {
        switch (TextureA)
        {
            case null when TextureB == null:
                return;
            case null when TextureB != null:
                TextureA = TextureB;
                break;
            default:
            {
                if (TextureA != null && TextureB == null)
                {
                    TextureB = TextureA;
                }

                break;
            }
        }

        Rect bounds = Bounds;

        double aOpacity = 1.0 - Blend;
        double bOpacity = Blend;

        if (TextureA != null && aOpacity > 0)
        {
            using (context.PushOpacity(aOpacity))
            {
                context.DrawImage(
                    TextureA,
                    new Rect(0, 0, TextureA.PixelSize.Width, TextureA.PixelSize.Height),
                    GetCoverRect(TextureA, bounds));
            }
        }

        if (TextureB != null && bOpacity > 0)
        {
            using (context.PushOpacity(bOpacity))
            {
                context.DrawImage(
                    TextureB,
                    new Rect(0, 0, TextureB.PixelSize.Width, TextureB.PixelSize.Height),
                    GetCoverRect(TextureB, bounds));
            }
        }
    }
}