using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Svg.Skia;

namespace osuautodeafen.cs;

public class LogoControl : Control
{
    private SKSvg? _svg;
    public SKSvg? Svg
    {
        get => _svg;
        set
        {
            if (_svg != value)
            {
                _svg = value;
                InvalidateVisual();
            }
        }
    }

    private SKColor _modulateColor = SKColors.White;
    public SKColor ModulateColor
    {
        get => _modulateColor;
        set
        {
            if (_modulateColor != value)
            {
                _modulateColor = value;
                InvalidateVisual();
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        
        Console.WriteLine($"[LogoControl] Render called. Bounds: {Bounds}");

        if (Svg?.Picture == null)
        {
            context.DrawRectangle(Brushes.Red, null, new Rect(0, 0, Bounds.Width, Bounds.Height));
            Console.WriteLine("[LogoControl] Svg.Picture is null");
            return;
        }

        Console.WriteLine($"[LogoControl] Svg.Picture size: {Svg.Picture.CullRect.Width}x{Svg.Picture.CullRect.Height}");
        context.Custom(new SkiaCustomDrawOperation(Bounds, Svg.Picture, ModulateColor));
    }

    private class SkiaCustomDrawOperation : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly SKPicture _picture;
        private readonly SKColor _color;

        public SkiaCustomDrawOperation(Rect bounds, SKPicture picture, SKColor color)
        {
            _bounds = bounds;
            _picture = picture;
            _color = color;
        }

        public void Dispose() { }

        public bool HitTest(Point p) => _bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>(out var skiaFeature) && skiaFeature != null)
            {
                using (var lease = skiaFeature.Lease())
                {
                    var canvas = lease.SkCanvas;
                    canvas.Save();

                    var cullRect = _picture.CullRect;
                    if (cullRect.Width > 0 && cullRect.Height > 0 && _bounds.Width > 0 && _bounds.Height > 0)
                    {
                        var scaleX = (float)(_bounds.Width / cullRect.Width);
                        var scaleY = (float)(_bounds.Height / cullRect.Height);
                        canvas.Scale(scaleX, scaleY);
                    }

                    using var paint = new SKPaint
                    {
                        ColorFilter = SKColorFilter.CreateBlendMode(_color, SKBlendMode.Modulate)
                    };
                    canvas.DrawPicture(_picture, paint);

                    canvas.Restore();
                }
            }
            else
            {
                Console.WriteLine("[LogoControl] Skia feature is null");
                return;
            }
        }

        public Rect Bounds => _bounds;
        public bool Equals(ICustomDrawOperation? other) => false;
    }
}