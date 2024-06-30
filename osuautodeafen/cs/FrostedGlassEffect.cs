using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

public class FrostedGlassEffect : UserControl
{
    public FrostedGlassEffect()
    {
        var noiseImage = new Bitmap("Resources/noise.png"); // Replace with the path to your noise image
        this.Background = new ImageBrush(noiseImage)
        {
            Opacity = 0.09727,
            Stretch = Stretch.UniformToFill
        };
    }
}