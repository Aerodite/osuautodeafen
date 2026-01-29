using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using osuautodeafen.cs.ViewModels;

namespace osuautodeafen.Views;

public partial class ChangelogView : UserControl
{
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty =
        AvaloniaProperty.Register<ChangelogView, IBrush>(nameof(BackgroundBrush));

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

    private void InlineText_Attached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBlock tb)
            return;

        if (tb.Tag is not IReadOnlyList<Inline> sourceInlines)
            return;

        tb.Inlines?.Clear();

        foreach (var inline in sourceInlines)
            tb.Inlines?.Add(CloneInline(inline));
    }

    private static Inline CloneInline(Inline inline)
    {
        return inline switch
        {
            Run run => new Run
            {
                Text = run.Text,
                FontSize = run.FontSize,
                FontWeight = run.FontWeight,
                Foreground = run.Foreground,
                TextDecorations = run.TextDecorations
            },

            LineBreak => new LineBreak(),

            InlineUIContainer container => new InlineUIContainer
            {
                BaselineAlignment = container.BaselineAlignment,
                Child = CloneControl(container.Child!)
            },

            _ => throw new NotSupportedException($"Unsupported inline: {inline.GetType()}")
        };
    }
    
    private static Control CloneControl(Control control)
    {
        if (control is Button button &&
            button.Content is TextBlock text)
        {
            return new Button
            {
                Padding = button.Padding,
                Background = button.Background,
                BorderThickness = button.BorderThickness,
                Cursor = button.Cursor,
                Command = button.Command,
                Content = new TextBlock
                {
                    Text = text.Text,
                    Foreground = text.Foreground,
                    FontSize = text.FontSize,
                    TextDecorations = text.TextDecorations
                }
            };
        }

        throw new NotSupportedException($"Unsupported control: {control.GetType()}");
    }
}