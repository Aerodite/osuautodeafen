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
using osuautodeafen.cs.ViewModels;
using AvaloniaInline = Avalonia.Controls.Documents.Inline;
using Inline = Markdig.Syntax.Inlines.Inline;

namespace osuautodeafen.cs.Changelog;

public static class ChangelogParser
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

    public static List<ChangelogViewModel.ChangelogEntry> Parse(string markdown, string changelogVersion)
    {
        MarkdownDocument doc = Markdown.Parse(markdown, Pipeline);

        ChangelogViewModel.ChangelogEntry entry = new(
            $"v{changelogVersion}",
            new List<ChangelogViewModel.ChangelogSection>()
        );

        ChangelogViewModel.ChangelogSection? currentSection = null;

        foreach (Block block in doc)
            switch (block)
            {
                case HeadingBlock h:
                    currentSection = new ChangelogViewModel.ChangelogSection(
                        GetInlineText(h.Inline),
                        new List<ChangelogViewModel.ChangelogBlock>()
                    );
                    entry.Sections.Add(currentSection);
                    break;

                case ParagraphBlock p when currentSection != null:
                    ParseParagraph(p, currentSection, changelogVersion);
                    break;

                case ListBlock list when currentSection != null:
                    ParseList(list, currentSection);
                    break;

                case QuoteBlock quote when currentSection != null:
                    currentSection.Blocks.Add(
                        new ChangelogViewModel.QuoteBlockModel(
                            GetQuoteText(quote)
                        )
                    );
                    break;

                case FencedCodeBlock code when currentSection != null:
                    currentSection.Blocks.Add(
                        new ChangelogViewModel.CodeBlockModel(
                            code.Lines.ToString(),
                            code.Info
                        )
                    );
                    break;

                case HtmlBlock html when currentSection != null:
                    TryParseHtmlImage(html.Lines.ToString(), currentSection);
                    break;

                case ThematicBreakBlock when currentSection != null:
                    currentSection.Blocks.Add(
                        new ChangelogViewModel.DividerBlockModel()
                    );
                    break;
            }

        return new List<ChangelogViewModel.ChangelogEntry> { entry };
    }

    private static void TryParseHtmlImage(
        string html,
        ChangelogViewModel.ChangelogSection section)
    {
        foreach (Match match in Regex.Matches(
                     html,
                     @"<img[^>]*src\s*=\s*[""'](?<url>[^""']+)[""']",
                     RegexOptions.IgnoreCase))
        {
            string url = match.Groups["url"].Value;

            if (IsImageUrl(url))
            {
                section.Blocks.Add(
                    new ChangelogViewModel.ImageBlockModel(url)
                );
            }
        }
    }

    private static void ParseParagraph(
        ParagraphBlock p,
        ChangelogViewModel.ChangelogSection section,
        string changelogVersion)
    {
        var image = p.Inline?
            .Descendants<LinkInline>()
            .FirstOrDefault(l => l.IsImage && l.Url != null);

        if (image != null)
        {
            section.Blocks.Add(
                new ChangelogViewModel.ImageBlockModel(image.Url!)
            );
            return;
        }

        string flatText = GetInlineText(p.Inline);

        if (TryParseMedia(flatText, section, changelogVersion))
            return;

        if (p.Inline?.Any(i => i is LinkInline { IsImage: false } || i is AutolinkInline) == true)
        {
            section.Blocks.Add(
                new ChangelogViewModel.InlineTextBlockModel(
                    ParseInlineParts(p.Inline!)
                ));
            return;
        }

        section.Blocks.Add(
            new ChangelogViewModel.TextBlockModel(flatText));
    }

    private static IReadOnlyList<AvaloniaInline> ParseInlineParts(ContainerInline inline)
    {
        var inlines = new List<AvaloniaInline>();

        foreach (Inline child in inline)
            switch (child)
            {
                case LiteralInline literal:
                    inlines.Add(new Run(
                        literal.Content.Text.Substring(
                            literal.Content.Start,
                            literal.Content.Length))
                    {
                        Foreground = Brushes.White
                    });
                    break;


                case EmphasisInline emphasis:
                    foreach (AvaloniaInline sub in ParseInlineParts(emphasis))
                        inlines.Add(sub);
                    break;

                case LinkInline link when !link.IsImage:
                    inlines.Add(CreateLinkInline(
                        GetInlineText(link),
                        link.Url!));
                    break;

                case AutolinkInline autoLink:
                    inlines.Add(CreateLinkInline(
                        autoLink.Url,
                        autoLink.Url));
                    break;

                case LineBreakInline:
                    inlines.Add(new LineBreak());
                    break;
            }

        return inlines;
    }

    private static InlineUIContainer CreateLinkInline(string text, string url)
    {
        TextBlock textBlock = new()
        {
            Text = text,
            Foreground = Brushes.MediumPurple,
            TextDecorations = TextDecorations.Underline
        };

        Button button = new()
        {
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = textBlock,
            Tag = url
        };

        button.Click += (_, _) =>
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

        return new InlineUIContainer
        {
            Child = button,
            BaselineAlignment = BaselineAlignment.Center
        };
    }

    private static bool TryParseMedia(
        string text,
        ChangelogViewModel.ChangelogSection section,
        string changelogVersion)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out _))
            return false;

        if (IsVideoUrl(text))
        {
            section.Blocks.Add(
                new ChangelogViewModel.VideoPreviewBlockModel(text, changelogVersion)
            );
            return true;
        }

        if (IsImageUrl(text))
        {
            section.Blocks.Add(
                new ChangelogViewModel.ImageBlockModel(text)
            );
            return true;
        }

        return false;
    }

    private static void ParseList(
        ListBlock list,
        ChangelogViewModel.ChangelogSection section)
    {
        foreach (ListItemBlock item in list)
        {
            ParagraphBlock? paragraph =
                item.Descendants<ParagraphBlock>().FirstOrDefault();

            if (paragraph == null)
                continue;

            section.Blocks.Add(
                ParseBullet(paragraph)
            );
        }
    }

    private static ChangelogViewModel.BulletBlockModel ParseBullet(
        ParagraphBlock paragraph)
    {
        string text = GetInlineText(paragraph.Inline);

        Match shortMatch = Regex.Match(text, @"\(#(?<pr>\d+)\)");
        if (shortMatch.Success)
        {
            string pr = shortMatch.Groups["pr"].Value;
            string url = $"https://github.com/Aerodite/osuautodeafen/pull/{pr}";

            return new ChangelogViewModel.BulletBlockModel(
                text,
                $"#{pr}",
                url
            );
        }

        LinkInline? link = paragraph
            .Inline?
            .Descendants<LinkInline>()
            .FirstOrDefault(l =>
                !l.IsImage &&
                l.Url != null &&
                l.Url.Contains("/pull/"));

        if (link != null)
        {
            Match match = Regex.Match(link.Url!, @"/pull/(?<pr>\d+)");
            if (match.Success)
            {
                string pr = match.Groups["pr"].Value;

                string replaced = text.Replace(
                    link.Url!,
                    $"(#{pr})"
                );

                return new ChangelogViewModel.BulletBlockModel(
                    replaced,
                    $"#{pr}",
                    link.Url
                );
            }
        }

        return new ChangelogViewModel.BulletBlockModel(text, null, null);
    }

    private static string GetQuoteText(QuoteBlock quote)
    {
        StringBuilder sb = new();

        foreach (Block block in quote)
            if (block is ParagraphBlock p)
                sb.AppendLine(GetInlineText(p.Inline));

        return sb.ToString().Trim();
    }

    private static string GetInlineText(ContainerInline? inline)
    {
        if (inline == null)
            return string.Empty;

        StringBuilder sb = new();

        foreach (Inline child in inline)
            switch (child)
            {
                case LiteralInline literal:
                    sb.Append(
                        literal.Content.Text.Substring(
                            literal.Content.Start,
                            literal.Content.Length
                        )
                    );
                    break;

                case LineBreakInline:
                    sb.Append('\n');
                    break;

                case LinkInline link when !link.IsImage:
                    sb.Append(GetInlineText(link));
                    break;
            }

        return sb.ToString().Trim();
    }

    private static bool IsVideoUrl(string url)
    {
        return url.EndsWith(".mp4") ||
               url.EndsWith(".webm") ||
               url.Contains("github.com/user-attachments");
    }

    private static bool IsImageUrl(string url)
    {
        return url.EndsWith(".png") ||
               url.EndsWith(".jpg") ||
               url.EndsWith(".jpeg") ||
               url.EndsWith(".gif") ||
               url.Contains("github.com/user-attachments") || 
               url.EndsWith(".webp");
    }
}
