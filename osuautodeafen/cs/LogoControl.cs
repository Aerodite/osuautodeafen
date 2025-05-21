using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Svg.Skia;
using System.Runtime.CompilerServices;
using Avalonia.Platform;

namespace osuautodeafen.cs;

public sealed class LogoControl : Control
{
    public SKSvg? Svg;
    public SKColor ModulateColor = SKColors.White;
    private SkiaCustomDrawOperation _drawOp;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Render(DrawingContext context)
    {
        // No null checks, assume Svg and Picture are always valid
        var picture = Svg!.Picture!;
        var bounds = Bounds;
        var color = ModulateColor;

        // No cache, always create new draw op
        _drawOp = new SkiaCustomDrawOperation(bounds, picture, color);
        context.Custom(_drawOp);
    }

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly struct SkiaCustomDrawOperation(Rect bounds, SKPicture picture, SKColor color)
        : ICustomDrawOperation
    {
        public readonly Rect Bounds = bounds;
        public readonly SKPicture Picture = picture;
        public readonly SKColor Color = color;
        private static readonly SKPaint SharedPaint = new SKPaint();
        private static SKColor _lastColor = SKColors.Transparent;
        private static SKColorFilter? _lastFilter;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HitTest(Point p) => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>(out var skiaFeature))
            {
                using (var lease = skiaFeature.Lease())
                {
                    var canvas = lease.SkCanvas;
                    canvas.Save();

                    var cullRect = Picture.CullRect;
                    var scaleX = (float)(Bounds.Width / cullRect.Width);
                    var scaleY = (float)(Bounds.Height / cullRect.Height);
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
        }

        Rect ICustomDrawOperation.Bounds => Bounds;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(ICustomDrawOperation? other) => false; // Skip equality for speed
    }
}