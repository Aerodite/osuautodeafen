using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using osuautodeafen.cs.Tooltips;
using osuautodeafen.cs.ViewModels;

namespace osuautodeafen.Views;

public partial class ChangelogView : UserControl
{
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty =
        AvaloniaProperty.Register<ChangelogView, IBrush>(nameof(BackgroundBrush));

    private static TooltipManager Tooltips =>
        MainWindow.Tooltips;

    public ChangelogView()
    {
        InitializeComponent();
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

        if (tb.Tag is not IReadOnlyList<Inline> inlines)
            return;

        if (tb.Inlines == null) return;
        tb.Inlines.Clear();

        foreach (var inline in inlines)
        {
            Inline cloned = CloneInline(inline);
            tb.Inlines.Add(cloned);

            if (cloned is InlineUIContainer { Child: Button btn } &&
                btn.Tag is string url)
            {
                btn.PointerEntered += (_, ev) => ShowLinkTooltip(btn, ev, FormatUrlForTooltip(url));
                btn.PointerMoved += (_, ev) => MoveLinkTooltip(btn, ev);
                btn.PointerExited += (_, _) => Tooltips.HideTooltip();
            }
        }
    }
    
    private static string FormatUrlForTooltip(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;
        
        string host = uri.Host;
        string lastSegment = uri.Segments.LastOrDefault()?.TrimEnd('/') ?? "";

        return lastSegment.Length > 0
            ? $"{host}/…/{lastSegment}"
            : host;
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
            var clone = new Button
            {
                Padding = button.Padding,
                Background = button.Background,
                BorderThickness = button.BorderThickness,
                Cursor = button.Cursor,
                Tag = button.Tag,
                Command = button.Command,
                Content = new TextBlock
                {
                    Text = text.Text,
                    Foreground = text.Foreground,
                    FontSize = text.FontSize,
                    TextDecorations = text.TextDecorations
                }
            };
            
            if (clone.Command == null && clone.Tag is string url)
            {
                clone.Click += (_, _) => OpenUrl(url);
            }

            return clone;
        }

        throw new NotSupportedException($"Unsupported control: {control.GetType()}");
    }
    
    private static void OpenUrl(string url)
    {
        try
        {
            using var _ = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
        }
        catch
        {
            // ignore
        }
    }


    private void ShowLinkTooltip(Control target, PointerEventArgs e, string url)
    {
        var window = target.GetVisualRoot() as Window;
        if (window == null)
            return;

        Point point = osuautodeafen.cs.Tooltips.Tooltips
            .GetWindowRelativePointer(window, e);
        Tooltips.ShowTooltip(target, point, url);
    }

    private void MoveLinkTooltip(Control target, PointerEventArgs e)
    {
        var window = target.GetVisualRoot() as Window;
        if (window == null)
            return;

        Point point = osuautodeafen.cs.Tooltips.Tooltips
            .GetWindowRelativePointer(window, e);
        Tooltips.MoveTooltipToPosition(point);
    }
       
    private void VideoRedirect_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Control c)
            return;

        var window = c.GetVisualRoot() as Window;
        if (window == null)
            return;

        Point point = osuautodeafen.cs.Tooltips.Tooltips
            .GetWindowRelativePointer(window, e);
        Tooltips.ShowTooltip(c, point, "Open Video in Browser");
    }

    private void VideoRedirect_PointerExited(object? sender, PointerEventArgs e)
    {
        Tooltips.HideTooltip();
    }
    
    private void VideoRedirect_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Control c)
            return;

        var window = c.GetVisualRoot() as Window;
        if (window == null)
            return;

        Point point = osuautodeafen.cs.Tooltips.Tooltips
            .GetWindowRelativePointer(window, e);
        Tooltips.MoveTooltipToPosition(point);
    }
    
    private void PrButton_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Control c)
            return;

        var window = c.GetVisualRoot() as Window;
        if (window == null)
            return;

        Point point = osuautodeafen.cs.Tooltips.Tooltips
            .GetWindowRelativePointer(window, e);
        Tooltips.ShowTooltip(c, point, "Open Pull Request in Browser");
    }

    private void PrButton_PointerExited(object? sender, PointerEventArgs e)
    {
        Tooltips.HideTooltip();
    }
    private void PrButton_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Control c)
            return;

        var window = c.GetVisualRoot() as Window;
        if (window == null)
            return;

        Point point = osuautodeafen.cs.Tooltips.Tooltips
            .GetWindowRelativePointer(window, e);
        Tooltips.MoveTooltipToPosition(point);
    }
}