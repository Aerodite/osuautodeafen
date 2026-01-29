using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using osuautodeafen.cs.Changelog;

namespace osuautodeafen.cs.ViewModels;

public class ChangelogViewModel : ViewModelBase
{
    private bool _isVisible;
    private bool _isDisposed;
    public ObservableCollection<ChangelogEntry> Entries { get; } = new();
    public IBrush? BackgroundBrush { get; set; }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
    
    public ICommand DismissCommand => new RelayCommand(() =>
    {
        DismissRequested?.Invoke();
    });

    public event Action? DismissRequested;
    
    public void LoadFromMarkdown(string markdown)
    {
        Entries.Clear();
        foreach (ChangelogEntry entry in ChangelogParser.Parse(markdown))
            Entries.Add(entry);
    }

    public record ChangelogEntry(string Version, List<ChangelogSection> Sections);

    public record ChangelogSection(string Title, List<ChangelogBlock> Blocks);

    public abstract class ChangelogBlock : ViewModelBase
    {
    }

    public sealed class TextBlockModel(string text) : ChangelogBlock
    {
        public string Text { get; } = text;
    }

    public sealed class ImageBlockModel : ChangelogBlock, IDisposable
    {
        private static readonly HttpClient Http = new();

        private IImage? _source;
        public IImage? Source
        {
            get => _source;
            private set => SetProperty(ref _source, value);
        }
        
        public void Dispose()
        {
            if (_source is Bitmap bitmap)
                bitmap.Dispose();

            _source = null;
        }
        public ImageBlockModel(string url)
        {
            _ = LoadAsync(url);
        }

        private async Task LoadAsync(string url)
        {
            try
            {
                await using var httpStream = await Http.GetStreamAsync(url);

                var ms = new MemoryStream();
                await httpStream.CopyToAsync(ms);
                ms.Position = 0;

                var bitmap = new Bitmap(ms);

                await Dispatcher.UIThread.InvokeAsync(() =>
                    Source = bitmap);
            }
            catch (Exception e)
            {
                Debug.WriteLine($"Failed to load image: {url}\n{e}");
            }
        }
    }


    public sealed class BulletGroupBlockModel : ChangelogBlock
    {
        public BulletGroupBlockModel(string title)
        {
            Title = title;
            Children = new List<ChangelogBlock>();
        }

        public string Title { get; }
        public List<ChangelogBlock> Children { get; }
    }

    public sealed class VideoPreviewBlockModel : ChangelogBlock, IDisposable
    {
        private readonly string _videoUrl;

        private string? _previewPath;

        private double _progress;
        private bool _started;

        private bool _disposed;

        public void Dispose()
        {
            _disposed = true;
        }
        
        public VideoPreviewBlockModel(string videoUrl)
        {
            _videoUrl = videoUrl;

            OpenVideoCommand = new RelayCommand(() =>
                Process.Start(new ProcessStartInfo
                {
                    FileName = _videoUrl,
                    UseShellExecute = true
                }));
        }

        public bool IsEncoding => Progress < 1.0;

        public string? PreviewPath
        {
            get => _previewPath;
            private set => SetProperty(ref _previewPath, value);
        }

        public double Progress
        {
            get => _progress;
            private set => SetProperty(ref _progress, value);
        }


        public ICommand OpenVideoCommand { get; }

        public void OnAttached()
        {
            StartEncoding();
        }

        private void StartEncoding()
        {
            if (_started || _disposed)
                return;

            _started = true;

            VideoPreviewCache.EnsurePreview(
                _videoUrl,
                p =>
                {
                    if (_disposed) return;
                    Dispatcher.UIThread.Post(() => Progress = p);
                },
                path =>
                {
                    if (_disposed) return;
                    Dispatcher.UIThread.Post(() =>
                    {
                        PreviewPath = new Uri(path).AbsoluteUri;
                        Progress = 1.0;
                    });
                }
            );
        }
    }

    public sealed class BulletBlockModel : ChangelogBlock
    {
        public BulletBlockModel(string text, string? prNumber, string? prUrl)
        {
            Text = text;
            PrNumber = prNumber;
            PrUrl = prUrl;

            OpenPrCommand = prUrl == null
                ? null
                : new RelayCommand(() =>
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = prUrl,
                        UseShellExecute = true
                    }));
        }

        public string Text { get; }
        public string? PrNumber { get; }
        public string? PrUrl { get; }

        public bool HasPr => PrUrl != null;
        public ICommand? OpenPrCommand { get; }
    }

    public sealed class DividerBlockModel : ChangelogBlock
    {
    }

    public sealed class CodeBlockModel : ChangelogBlock
    {
        public CodeBlockModel(string code, string? language)
        {
            Code = code;
            Language = language;
        }

        public string Code { get; }
        public string? Language { get; }
    }

    public sealed class QuoteBlockModel : ChangelogBlock
    {
        public QuoteBlockModel(string text)
        {
            Text = text;
        }

        public string Text { get; }
    }

    public sealed class InlineTextBlockModel : ChangelogBlock
    {
        public InlineTextBlockModel(IReadOnlyList<Inline> inlines)
        {
            Inlines = inlines;
        }

        public IReadOnlyList<Inline> Inlines { get; }
    }
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        // Stop rendering
        IsVisible = false;

        // Dispose entries
        foreach (var entry in Entries)
        {
            foreach (var section in entry.Sections)
            {
                foreach (var block in section.Blocks)
                {
                    if (block is IDisposable d)
                        d.Dispose();
                }
            }
        }

        Entries.Clear();
        BackgroundBrush = null;
    }
}