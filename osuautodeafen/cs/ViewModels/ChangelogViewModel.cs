using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using osuautodeafen.cs.Changelog;

namespace osuautodeafen.cs.ViewModels;

public class ChangelogViewModel : ViewModelBase
{
    private bool _isVisible;
    public ObservableCollection<ChangelogEntry> Entries { get; } = new();
    public IBrush? BackgroundBrush { get; set; }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public ICommand DismissCommand =>
        new RelayCommand(() => { });

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

    public sealed class ImageBlockModel(string path) : ChangelogBlock
    {
        public string Path { get; } = path;
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

    public sealed class VideoPreviewBlockModel : ChangelogBlock
    {
        private readonly string _videoUrl;

        private string? _previewPath;

        private double _progress;
        private bool _started;

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
            if (_started)
                return;

            _started = true;

            VideoPreviewCache.EnsurePreview(
                _videoUrl,
                p => Dispatcher.UIThread.Post(() => Progress = p),
                path => Dispatcher.UIThread.Post(() =>
                {
                    PreviewPath = new Uri(path).AbsoluteUri;
                    Progress = 1.0;
                })
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
        }

        public string Text { get; }
        public string? PrNumber { get; }
        public string? PrUrl { get; }

        public bool HasPr => PrNumber != null;
    }

    public sealed class DividerBlockModel : ChangelogBlock
    {
    }
}