using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using osuautodeafen.ViewModels;
using AvaloniaInline = Avalonia.Controls.Documents.Inline;
using MarkdownInline = Markdig.Syntax.Inlines.Inline;

namespace osuautodeafen.Changelog;

public static class ChangelogParser
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

    public static List<ChangelogViewModel.ChangelogEntry> Parse(string markdown, string changelogVersion)
    {
        MarkdownDocument doc = Markdown.Parse(markdown, Pipeline);
        
        var entry = new ChangelogViewModel.ChangelogEntry(
            $"v{changelogVersion}",
            new List<ChangelogViewModel.ChangelogSection>());
        
        var section = NewSection(entry, string.Empty);

        foreach (Block block in doc)
        {
            if (block is HeadingBlock heading)
            {
                section = NewSection(entry, GetInlineText(heading.Inline));
                continue;
            }

            ParseBlock(block, section, changelogVersion);
        }

        return new List<ChangelogViewModel.ChangelogEntry> { entry };
    }

    private static ChangelogViewModel.ChangelogSection NewSection(
        ChangelogViewModel.ChangelogEntry entry, string title)
    {
        var section = new ChangelogViewModel.ChangelogSection(
            title, new List<ChangelogViewModel.ChangelogBlock>());
        entry.Sections.Add(section);
        return section;
    }

    private static void ParseBlock(
        Block block,
        ChangelogViewModel.ChangelogSection section,
        string version)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                ParseParagraph(paragraph, section, version);
                break;
            case ListBlock list:
                ParseList(list, section, version);
                break;
            case QuoteBlock quote:
                section.Blocks.Add(new ChangelogViewModel.QuoteBlockModel(GetQuoteText(quote)));
                break;
            case FencedCodeBlock code:
                section.Blocks.Add(new ChangelogViewModel.CodeBlockModel(
                    code.Lines.ToString(), code.Info));
                break;
            case HtmlBlock html:
                ParseHtmlImages(html.Lines.ToString(), section);
                break;
            case ThematicBreakBlock:
                section.Blocks.Add(new ChangelogViewModel.DividerBlockModel());
                break;
            case ListItemBlock item:
                foreach (Block child in item)
                    ParseBlock(child, section, version);
                break;
        }
    }

    private static void ParseList(
        ListBlock list,
        ChangelogViewModel.ChangelogSection section,
        string version)
    {
        foreach (ListItemBlock item in list)
        {
            bool bulletCreated = false;

            foreach (Block child in item)
            {
                if (child is ParagraphBlock paragraph && !bulletCreated)
                {
                    string text = GetInlineText(paragraph.Inline);
                    string[] lines = text.Split(new[] { '\n' }, 2);

                    section.Blocks.Add(ParseBullet(paragraph, lines[0].Trim()));
                    bulletCreated = true;

                    if (lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]))
                        section.Blocks.Add(new ChangelogViewModel.TextBlockModel(lines[1].Trim()));
                }
                else
                {
                    ParseBlock(child, section, version);
                }
            }
        }
    }

    private static ChangelogViewModel.BulletBlockModel ParseBullet(
        ParagraphBlock paragraph, string text)
    {
        Match shortMatch = Regex.Match(text, @"\(#(?<pr>\d+)\)");
        if (shortMatch.Success)
        {
            string pr = shortMatch.Groups["pr"].Value;
            return new ChangelogViewModel.BulletBlockModel(
                text, $"#{pr}", $"https://github.com/Aerodite/osuautodeafen/pull/{pr}");
        }
        
        Match urlMatch = Regex.Match(text,
            @"https?://github\.com/Aerodite/osuautodeafen/pull/(?<pr>\d+)");
        if (urlMatch.Success)
        {
            string pr = urlMatch.Groups["pr"].Value;
            return new ChangelogViewModel.BulletBlockModel(
                text.Replace(urlMatch.Value, $"(#{pr})"),
                $"#{pr}", urlMatch.Value);
        }

        LinkInline? link = paragraph.Inline?
            .Descendants<LinkInline>()
            .FirstOrDefault(l => !l.IsImage && l.Url != null && l.Url.Contains("/pull/"));

        if (link != null)
        {
            Match match = Regex.Match(link.Url!, @"/pull/(?<pr>\d+)");
            if (match.Success)
            {
                string pr = match.Groups["pr"].Value;
                return new ChangelogViewModel.BulletBlockModel(
                    text.Replace(GetInlineText(link), $"(#{pr})"),
                    $"#{pr}", link.Url);
            }
        }

        return new ChangelogViewModel.BulletBlockModel(text, null, null);
    }

    private static void ParseParagraph(
        ParagraphBlock paragraph,
        ChangelogViewModel.ChangelogSection section,
        string version)
    {
        if (paragraph.Inline == null)
            return;

        LinkInline? image = paragraph.Inline.Descendants<LinkInline>()
            .FirstOrDefault(link => link.IsImage && !string.IsNullOrWhiteSpace(link.Url));
        if (image != null)
        {
            section.Blocks.Add(new ChangelogViewModel.ImageBlockModel(image.Url!));
            return;
        }

        string text = GetInlineText(paragraph.Inline);
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (TryParseMedia(text, section, version))
            return;

        var inlines = ParseInlineParts(paragraph.Inline);
        if (inlines.Any(i => i is InlineUIContainer))
            section.Blocks.Add(new ChangelogViewModel.InlineTextBlockModel(inlines));
        else
            section.Blocks.Add(new ChangelogViewModel.TextBlockModel(text));
    }

    private static IReadOnlyList<AvaloniaInline> ParseInlineParts(ContainerInline container)
    {
        var result = new List<AvaloniaInline>();
        foreach (MarkdownInline child in container)
        {
            switch (child)
            {
                case LiteralInline literal:
                    result.Add(new Run { Text = literal.Content.ToString(), Foreground = Brushes.White });
                    break;
                case EmphasisInline emphasis:
                    result.AddRange(ParseInlineParts(emphasis));
                    break;
                case LinkInline link when !link.IsImage && !string.IsNullOrWhiteSpace(link.Url):
                    result.Add(CreateLinkInline(GetInlineText(link), link.Url!));
                    break;
                case AutolinkInline autolink:
                    result.Add(CreateLinkInline(autolink.Url, autolink.Url));
                    break;
                case LineBreakInline:
                    result.Add(new LineBreak());
                    break;
                case CodeInline code:
                    result.Add(new Run { Text = code.Content, Foreground = Brushes.White });
                    break;
                case HtmlInline html:
                    break;
                case ContainerInline nested:
                    result.AddRange(ParseInlineParts(nested));
                    break;
            }
        }
        return result;
    }

    private static InlineUIContainer CreateLinkInline(string text, string url)
    {
        var button = new Button
        {
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = url,
            Content = new TextBlock
            {
                Text = text,
                Foreground = Brushes.MediumPurple,
                TextDecorations = TextDecorations.Underline
            }
        };

        button.Click += (_, _) => OpenUrl(url);
        return new InlineUIContainer
        {
            Child = button,
            BaselineAlignment = BaselineAlignment.Center
        };
    }

    private static void OpenUrl(string url)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open changelog link: {ex}");
        }
    }

    private static void ParseHtmlImages(
        string html, ChangelogViewModel.ChangelogSection section)
    {
        foreach (Match match in Regex.Matches(html,
                     @"<img\b[^>]*\bsrc\s*=\s*[""'](?<url>[^""']+)[""']",
                     RegexOptions.IgnoreCase))
        {
            string url = match.Groups["url"].Value;
            if (IsImageUrl(url))
                section.Blocks.Add(new ChangelogViewModel.ImageBlockModel(url));
        }
    }

    private static string GetQuoteText(QuoteBlock quote)
    {
        var sb = new StringBuilder();
        foreach (Block block in quote)
            if (block is ParagraphBlock paragraph)
                sb.AppendLine(GetInlineText(paragraph.Inline));
        return sb.ToString().Trim();
    }

    private static string GetInlineText(ContainerInline? container)
    {
        if (container == null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (MarkdownInline child in container)
        {
            switch (child)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content.ToString());
                    break;
                case LineBreakInline:
                    sb.Append('\n');
                    break;
                case LinkInline link when !link.IsImage:
                    sb.Append(GetInlineText(link));
                    break;
                case AutolinkInline autolink:
                    sb.Append(autolink.Url);
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case ContainerInline nested:
                    sb.Append(GetInlineText(nested));
                    break;
            }
        }
        return sb.ToString().Trim();
    }

    private static bool TryParseMedia(
        string text, ChangelogViewModel.ChangelogSection section, string version)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out _))
            return false;
        if (IsVideoUrl(text))
        {
            section.Blocks.Add(new ChangelogViewModel.VideoPreviewBlockModel(text, version));
            return true;
        }
        if (IsImageUrl(text))
        {
            section.Blocks.Add(new ChangelogViewModel.ImageBlockModel(text));
            return true;
        }
        return false;
    }

    private static bool IsVideoUrl(string url) =>
        url.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
        url.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("github.com/user-attachments", StringComparison.OrdinalIgnoreCase);

    private static bool IsImageUrl(string url) =>
        new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" }
            .Any(ext => url.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
}
