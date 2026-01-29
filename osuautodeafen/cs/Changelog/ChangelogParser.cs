using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using osuautodeafen.cs.Update;
using osuautodeafen.cs.ViewModels;

namespace osuautodeafen.cs.Changelog;

public static class ChangelogParser
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

    public static List<ChangelogViewModel.ChangelogEntry> Parse(string markdown)
    {
        MarkdownDocument doc = Markdown.Parse(markdown, Pipeline);

        var entry = new ChangelogViewModel.ChangelogEntry(
            $"v{UpdateChecker.CurrentVersion}",
            new List<ChangelogViewModel.ChangelogSection>()
        );

        ChangelogViewModel.ChangelogSection? currentSection = null;

        foreach (Block block in doc)
        {
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
                    ParseParagraph(p, currentSection);
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

                case ThematicBreakBlock when currentSection != null:
                    currentSection.Blocks.Add(
                        new ChangelogViewModel.DividerBlockModel()
                    );
                    break;
            }
        }

        return new List<ChangelogViewModel.ChangelogEntry> { entry };
    }

    private static void ParseParagraph(
        ParagraphBlock p,
        ChangelogViewModel.ChangelogSection section)
    {
        // markdown image syntax = [text](link)
        if (p.Inline?.FirstChild is LinkInline link && link.IsImage)
        {
            section.Blocks.Add(
                new ChangelogViewModel.ImageBlockModel(link.Url!)
            );
            return;
        }

        string text = GetInlineText(p.Inline);
        
        if (TryParseMedia(text, section))
            return;

        section.Blocks.Add(
            new ChangelogViewModel.TextBlockModel(text)
        );
    }

    private static bool TryParseMedia(
        string text,
        ChangelogViewModel.ChangelogSection section)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out _))
            return false;

        if (IsVideoUrl(text))
        {
            section.Blocks.Add(
                new ChangelogViewModel.VideoPreviewBlockModel(text)
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
                ParseBullet(GetInlineText(paragraph.Inline) is string text
                    ? paragraph
                    : throw new InvalidOperationException()
                )
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
        
        var link = paragraph
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
        var sb = new StringBuilder();

        foreach (Block block in quote)
            if (block is ParagraphBlock p)
                sb.AppendLine(GetInlineText(p.Inline));

        return sb.ToString().Trim();
    }

    private static string GetInlineText(ContainerInline? inline)
    {
        if (inline == null)
            return string.Empty;

        var sb = new StringBuilder();

        foreach (Inline child in inline)
        {
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
        }

        return sb.ToString().Trim();
    }

    private static bool IsVideoUrl(string url) =>
        url.EndsWith(".mp4") ||
        url.EndsWith(".webm") ||
        url.Contains("github.com/user-attachments");

    private static bool IsImageUrl(string url) =>
        url.EndsWith(".png") ||
        url.EndsWith(".jpg") ||
        url.EndsWith(".jpeg") ||
        url.EndsWith(".gif");
}
