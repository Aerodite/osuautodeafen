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
    private static readonly Regex UrlRegex =
        new(@"https?://\S+", RegexOptions.Compiled);

    public static List<ChangelogViewModel.ChangelogEntry> Parse(string markdown)
    {
        MarkdownDocument doc = Markdown.Parse(markdown);

        var entries = new List<ChangelogViewModel.ChangelogEntry>();

        ChangelogViewModel.ChangelogEntry entry = new(
            $"v{UpdateChecker.CurrentVersion}",
            new List<ChangelogViewModel.ChangelogSection>()
        );
        entries.Add(entry);

        ChangelogViewModel.ChangelogSection? section = null;

        foreach (Block block in doc)
            switch (block)
            {
                case HeadingBlock h:
                    section = new ChangelogViewModel.ChangelogSection(
                        GetInlineText(h.Inline),
                        new List<ChangelogViewModel.ChangelogBlock>()
                    );
                    entry.Sections.Add(section);
                    break;

                case ParagraphBlock p when section != null:
                    HandleParagraph(p, section);
                    break;

                case ListBlock list when section != null:
                    foreach (ListItemBlock item in list)
                        if (item.First() is ParagraphBlock pb)
                            section.Blocks.Add(ParseBullet(GetInlineText(pb.Inline)));

                    break;

                case ThematicBreakBlock:
                    section?.Blocks.Add(new ChangelogViewModel.DividerBlockModel());
                    break;
            }

        return entries;
    }

    private static void HandleParagraph(
        ParagraphBlock p,
        ChangelogViewModel.ChangelogSection section)
    {
        string text = GetInlineText(p.Inline);

        // checks if the paragraph has a url
        Match match = UrlRegex.Match(text);
        if (match.Success && match.Value == text)
        {
            string url = match.Value;

            if (IsVideoUrl(url))
            {
                section.Blocks.Add(
                    new ChangelogViewModel.VideoPreviewBlockModel(url)
                );
                return;
            }

            if (IsImageUrl(url))
            {
                section.Blocks.Add(
                    new ChangelogViewModel.ImageBlockModel(url)
                );
                return;
            }
        }

        // Fallback: normal text
        section.Blocks.Add(
            new ChangelogViewModel.TextBlockModel(text)
        );
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
               url.EndsWith(".gif");
    }

    private static ChangelogViewModel.BulletBlockModel ParseBullet(string text)
    {
        Match match = Regex.Match(
            text,
            @"https://github\.com/[^/]+/[^/]+/pull/(?<pr>\d+)"
        );

        if (!match.Success)
            return new ChangelogViewModel.BulletBlockModel(text, null, null);

        string pr = match.Groups["pr"].Value;
        string url = match.Value;

        string cleaned = text.Replace($"({url})", "").Trim();

        return new ChangelogViewModel.BulletBlockModel(cleaned, $"#{pr}", url);
    }

    private static string GetInlineText(ContainerInline? inline)
    {
        if (inline == null)
            return string.Empty;

        StringBuilder sb = new();

        foreach (Inline child in inline)
            if (child is LiteralInline literal)
                sb.Append(literal.Content.Text.Substring(
                    literal.Content.Start,
                    literal.Content.Length));
            else if (child is LineBreakInline) sb.Append('\n');

        return sb.ToString().Trim();
    }
}