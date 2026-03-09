using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Svg.Skia;

namespace osuautodeafen.cs;

public sealed class LogoControl : Control
{
    private SkiaCustomDrawOperation? _drawOp;
    private SKColor _modulateColor = SKColors.White;
    private SKSvg? _svg;

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

    public override void Render(DrawingContext context)
    {
        if (_svg?.Picture == null)
            return;

        Rect bounds = Bounds;
        SKColor color = _modulateColor;
        SKPicture? picture = _svg.Picture;

        // Only recreate draw op if parameters changed
        if (_drawOp == null || !_drawOp.Equals(bounds, picture, color))
            _drawOp = new SkiaCustomDrawOperation(bounds, picture, color);

        context.Custom(_drawOp);
    }

    private sealed class SkiaCustomDrawOperation : ICustomDrawOperation
    {
        private static readonly SKPaint SharedPaint = new();
        private static SKColor _lastColor = SKColors.Transparent;
        private static SKColorFilter? _lastFilter;
        public readonly Rect Bounds;
        public readonly SKColor Color;
        public readonly SKPicture Picture;

        public SkiaCustomDrawOperation(Rect bounds, SKPicture picture, SKColor color)
        {
            Bounds = bounds;
            Picture = picture;
            Color = color;
        }

        public void Dispose()
        {
        }

        public bool HitTest(Point p)
        {
            return true;
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>(out ISkiaSharpApiLeaseFeature? skiaFeature))
                using (ISkiaSharpApiLease lease = skiaFeature.Lease())
                {
                    SKCanvas canvas = lease.SkCanvas;
                    canvas.Save();

                    SKRect cullRect = Picture.CullRect;
                    float scaleX = (float)(Bounds.Width / cullRect.Width);
                    float scaleY = (float)(Bounds.Height / cullRect.Height);
                    canvas.Scale(scaleX, scaleY);

                    if (_lastColor != Color || _lastFilter == null)
                    {
                        _lastFilter = SKColorFilter.CreateBlendMode(Color, SKBlendMode.Modulate);
                        _lastColor = Color;
                    }

                    SharedPaint.ColorFilter = _lastFilter;
                    canvas.DrawPicture(Picture, SharedPaint);

                    canvas.Restore();
                }
        }

        Rect ICustomDrawOperation.Bounds => Bounds;

        public bool Equals(ICustomDrawOperation? other)
        {
            return false;
        }

        public bool Equals(Rect bounds, SKPicture picture, SKColor color)
        {
            return Bounds == bounds && Picture == picture && Color == color;
        }
    }
}