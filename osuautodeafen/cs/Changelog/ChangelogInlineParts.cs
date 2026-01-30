using System.Diagnostics;
using System.Windows.Input;

namespace osuautodeafen.cs.Changelog;

public abstract record InlinePart(string Text)
{
    public bool IsText => this is TextPart;
    public bool IsLink => this is LinkPart;
}

public sealed record TextPart(string Text) : InlinePart(Text);

public sealed record LinkPart(string Text, string Url) : InlinePart(Text)
{
    public ICommand OpenLinkCommand =>
        new RelayCommand(() =>
            Process.Start(new ProcessStartInfo
            {
                FileName = Url,
                UseShellExecute = true
            }));
}