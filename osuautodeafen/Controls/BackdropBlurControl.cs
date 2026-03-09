using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

// basically commpletely taken from https://github.com/rocksdanister/weather/blob/200d11ac9599ae10887d95175d5cd37a4046ea11/src/Drizzle.UI.Avalonia/UserControls/BackdropBlurControl.cs 
// (thanks)

namespace osuautodeafen.Controls;

// Ref: https://gist.github.com/kekekeks/ac06098a74fe87d49a9ff9ea37fa67bc
public class BackdropBlurControl : ContentControl
{
    public static readonly StyledProperty<ExperimentalAcrylicMaterial> MaterialProperty =
        AvaloniaProperty.Register<BackdropBlurControl, ExperimentalAcrylicMaterial>(
            "Material");

    // Same as #10ffffff for UWP CardBackgroundBrush.
    private static readonly ImmutableExperimentalAcrylicMaterial DefaultAcrylicMaterial =
        (ImmutableExperimentalAcrylicMaterial)new ExperimentalAcrylicMaterial
        {
            MaterialOpacity = 0.1,
            TintColor = Colors.White,
            TintOpacity = 0.1,
            PlatformTransparencyCompensationLevel = 0
        }.ToImmutable();

    private static SKShader s_acrylicNoiseShader;

    static BackdropBlurControl()
    {
        AffectsRender<BackdropBlurControl>(MaterialProperty);
    }

    public ExperimentalAcrylicMaterial Material
    {
        get => GetValue(MaterialProperty);
        set => SetValue(MaterialProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ImmutableExperimentalAcrylicMaterial mat = Material != null
            ? (ImmutableExperimentalAcrylicMaterial)Material.ToImmutable()
            : DefaultAcrylicMaterial;
        context.Custom(new BlurBehindRenderOperation(mat, new Rect(default, Bounds.Size)));
    }

    private class BlurBehindRenderOperation : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly ImmutableExperimentalAcrylicMaterial _material;

        public BlurBehindRenderOperation(ImmutableExperimentalAcrylicMaterial material, Rect bounds)
        {
            _material = material;
            _bounds = bounds;
        }

        public void Dispose()
        {
        }

        public bool HitTest(Point p)
        {
            return _bounds.Contains(p);
        }

        public void Render(ImmediateDrawingContext context)
        {
            ISkiaSharpApiLeaseFeature? leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null)
                return;
            using ISkiaSharpApiLease lease = leaseFeature.Lease();

            ISkiaSharpApiLease? skiaContext = lease;
            if (skiaContext == null)
                return;

            if (!skiaContext.SkCanvas.TotalMatrix.TryInvert(out SKMatrix currentInvertedTransform))
                return;

            using SKImage? backgroundSnapshot = skiaContext.SkSurface.Snapshot();
            using SKShader? backdropShader = SKShader.CreateImage(backgroundSnapshot, SKShaderTileMode.Clamp,
                SKShaderTileMode.Clamp, currentInvertedTransform);

            using SKSurface? blurred = SKSurface.Create(skiaContext.GrContext, false, new SKImageInfo(
                (int)Math.Ceiling(_bounds.Width),
                (int)Math.Ceiling(_bounds.Height), SKImageInfo.PlatformColorType, SKAlphaType.Premul));

            using (SKImageFilter filter = SKImageFilter.CreateBlur(10, 10, SKShaderTileMode.Clamp))
            using (SKPaint blurPaint = new()
                   {
                       Shader = backdropShader,
                       ImageFilter = filter
                   })
            {
                blurred.Canvas.DrawRect(0, 0, (float)_bounds.Width, (float)_bounds.Height, blurPaint);
            }

            using (SKImage? blurSnap = blurred.Snapshot())
            using (SKShader? blurSnapShader = SKShader.CreateImage(blurSnap))
            using (SKPaint blurSnapPaint = new()
                   {
                       Shader = blurSnapShader,
                       IsAntialias = true
                   })
            {
                skiaContext.SkCanvas.DrawRect(0, 0, (float)_bounds.Width, (float)_bounds.Height, blurSnapPaint);
            }

            using SKPaint acrylicPaint = new();
            acrylicPaint.IsAntialias = true;

            const double noiseOpacity = 0.0225;

            Color tintColor = _material.TintColor;
            SKColor tint = new(tintColor.R, tintColor.G, tintColor.B, tintColor.A);

            if (s_acrylicNoiseShader == null)
                using (Stream? stream =
                       typeof(SkiaPlatform).Assembly.GetManifestResourceStream(
                           "Avalonia.Skia.Assets.NoiseAsset_256X256_PNG.png"))
                using (SKBitmap? bitmap = SKBitmap.Decode(stream))
                {
                    s_acrylicNoiseShader = SKShader
                        .CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat)
                        .WithColorFilter(CreateAlphaColorFilter(noiseOpacity));
                }

            using (SKShader? backdrop = SKShader.CreateColor(new SKColor(_material.MaterialColor.R,
                       _material.MaterialColor.G, _material.MaterialColor.B, _material.MaterialColor.A)))
            using (SKShader? tintShader = SKShader.CreateColor(tint))
            using (SKShader? effectiveTint = SKShader.CreateCompose(backdrop, tintShader))
            using (SKShader? compose = SKShader.CreateCompose(effectiveTint, s_acrylicNoiseShader))
            {
                acrylicPaint.Shader = compose;
                acrylicPaint.IsAntialias = true;
                skiaContext.SkCanvas.DrawRect(0, 0, (float)_bounds.Width, (float)_bounds.Height, acrylicPaint);
            }
        }

        public Rect Bounds => _bounds.Inflate(4);

        public bool Equals(ICustomDrawOperation? other)
        {
            return other is BlurBehindRenderOperation op && op._bounds == _bounds && op._material.Equals(_material);
        }

        private static SKColorFilter CreateAlphaColorFilter(double opacity)
        {
            opacity = Math.Clamp(opacity, 0, 1);
            byte[] c = new byte[256];
            byte[] a = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                c[i] = (byte)i;
                a[i] = (byte)(i * opacity);
            }

            return SKColorFilter.CreateTable(a, c, c, c);
        }
    }
}