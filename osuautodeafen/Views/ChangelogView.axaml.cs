using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using osuautodeafen.cs.ViewModels;

namespace osuautodeafen.Views;

public partial class ChangelogView : UserControl
{
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty =
        AvaloniaProperty.Register<ChangelogView, IBrush>(
            nameof(BackgroundBrush));

    public ChangelogView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public IBrush BackgroundBrush
    {
        get => GetValue(BackgroundBrushProperty);
        set => SetValue(BackgroundBrushProperty, value);
    }

    private void VideoPreview_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control { DataContext: ChangelogViewModel.VideoPreviewBlockModel vm })
            vm.OnAttached();
    }
}