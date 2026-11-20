using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CopilotActivityViewer.Formatting
{
    public enum MarkdownBlockKind
    {
        Paragraph,
        Heading,
        ListItem,
        Quote,
        Code,
        Table,
        Rule,
    }

    public sealed class MarkdownBlock
    {
        public MarkdownBlock(MarkdownBlockKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }

        public MarkdownBlockKind Kind { get; }

        public string Text { get; }

        /// <summary>Heading level (1-6) or list nesting level (0 based).</summary>
        public int Level { get; set; }

        /// <summary>Bullet text for list items ("•", "1.", "2.").</summary>
        public string Marker { get; set; }

        public CodeLanguage Language { get; set; }

        public List<string[]> Rows { get; set; }
    }

    public enum InlineKind
    {
        Text,
        Bold,
        Italic,
        Code,
        Link,
    }

    public readonly struct MarkdownInline
    {
        public MarkdownInline(string text, InlineKind kind, string target = null)
        {
            Text = text;
            Kind = kind;
            Target = target;
        }

        public string Text { get; }

        public InlineKind Kind { get; }

        public string Target { get; }
    }

    /// <summary>Minimal markdown reader for chat style content (headings, lists, code fences, tables, quotes and inline emphasis).</summary>
    public static class MarkdownParser
    {
        private static readonly Regex Heading = new Regex(@"^(#{1,6})\s+(.*)$", RegexOptions.CultureInvariant);
        private static readonly Regex Bullet = new Regex(@"^(\s*)([-*+])\s+(.*)$", RegexOptions.CultureInvariant);
        private static readonly Regex Ordered = new Regex(@"^(\s*)(\d+)[.)]\s+(.*)$", RegexOptions.CultureInvariant);
        private static readonly Regex Fence = new Regex(@"^\s*(`{3,}|~{3,})\s*([^\s`]*)", RegexOptions.CultureInvariant);
        private static readonly Regex RuleLine = new Regex(@"^\s*(?:-{3,}|\*{3,}|_{3,})\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex TableSeparator = new Regex(@"^\s*\|?\s*:?-{2,}:?\s*(?:\|\s*:?-{2,}:?\s*)*\|?\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex Inline = new Regex(
            @"(?<code>`+)(?<codetext>.+?)\k<code>|\*\*(?<bold>.+?)\*\*|__(?<bold2>.+?)__|\[(?<linktext>[^\]]+)\]\((?<url>[^)\s]+)\)|(?<![\w*])\*(?<italic>[^*\s][^*]*?)\*(?![\w*])|(?<![\w_])_(?<italic2>[^_\s][^_]*?)_(?![\w_])",
            RegexOptions.CultureInvariant);

        public static List<MarkdownBlock> Parse(string text)
        {
            var blocks = new List<MarkdownBlock>();
            if (string.IsNullOrEmpty(text))
            {
                return blocks;
            }

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var paragraph = new StringBuilder();

            void FlushParagraph()
            {
                if (paragraph.Length > 0)
                {
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, paragraph.ToString()));
                    paragraph.Clear();
                }
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var fence = Fence.Match(line);
                if (fence.Success)
                {
                    FlushParagraph();
                    var marker = fence.Groups[1].Value;
                    var code = new StringBuilder();
                    i++;
                    while (i < lines.Length && !lines[i].TrimStart().StartsWith(marker, StringComparison.Ordinal))
                    {
                        if (code.Length > 0)
                        {
                            code.Append('\n');
                        }

                        code.Append(lines[i]);
                        i++;
                    }

                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Code, code.ToString()) { Language = CodeLanguages.FromName(fence.Groups[2].Value) });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushParagraph();
                    continue;
                }

                var heading = Heading.Match(line);
                if (heading.Success)
                {
                    FlushParagraph();
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Heading, heading.Groups[2].Value.Trim().TrimEnd('#').Trim()) { Level = heading.Groups[1].Length });
                    continue;
                }

                if (RuleLine.IsMatch(line))
                {
                    FlushParagraph();
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Rule, string.Empty));
                    continue;
                }

                if (IsTableRow(line) && i + 1 < lines.Length && TableSeparator.IsMatch(lines[i + 1]))
                {
                    FlushParagraph();
                    var rows = new List<string[]> { SplitRow(line) };
                    i += 2;
                    while (i < lines.Length && IsTableRow(lines[i]))
                    {
                        rows.Add(SplitRow(lines[i]));
                        i++;
                    }

                    i--;
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Table, string.Empty) { Rows = rows });
                    continue;
                }

                var bullet = Bullet.Match(line);
                var ordered = bullet.Success ? Match.Empty : Ordered.Match(line);
                if (bullet.Success || ordered.Success)
                {
                    FlushParagraph();
                    var m = bullet.Success ? bullet : ordered;
                    var item = new StringBuilder(m.Groups[3].Value);
                    var indent = m.Groups[1].Value.Replace("\t", "    ").Length;
                    while (i + 1 < lines.Length && IsContinuation(lines[i + 1], indent))
                    {
                        item.Append('\n').Append(lines[++i].Trim());
                    }

                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.ListItem, item.ToString())
                    {
                        Level = Math.Min(indent / 2, 6),
                        Marker = bullet.Success ? "•" : m.Groups[2].Value + ".",
                    });
                    continue;
                }

                if (line.TrimStart().StartsWith(">", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    var quote = new StringBuilder();
                    while (i < lines.Length && lines[i].TrimStart().StartsWith(">", StringComparison.Ordinal))
                    {
                        if (quote.Length > 0)
                        {
                            quote.Append('\n');
                        }

                        quote.Append(lines[i].TrimStart().Substring(1).TrimStart());
                        i++;
                    }

                    i--;
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Quote, quote.ToString()));
                    continue;
                }

                if (paragraph.Length > 0)
                {
                    paragraph.Append('\n');
                }

                paragraph.Append(line.TrimEnd());
            }

            FlushParagraph();
            return blocks;
        }

        public static List<MarkdownInline> ParseInline(string text)
        {
            var result = new List<MarkdownInline>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            var position = 0;
            foreach (Match match in Inline.Matches(text))
            {
                if (match.Index > position)
                {
                    result.Add(new MarkdownInline(text.Substring(position, match.Index - position), InlineKind.Text));
                }

                if (match.Groups["codetext"].Success)
                {
                    result.Add(new MarkdownInline(match.Groups["codetext"].Value.Trim(), InlineKind.Code));
                }
                else if (match.Groups["bold"].Success || match.Groups["bold2"].Success)
                {
                    result.Add(new MarkdownInline(match.Groups["bold"].Success ? match.Groups["bold"].Value : match.Groups["bold2"].Value, InlineKind.Bold));
                }
                else if (match.Groups["linktext"].Success)
                {
                    result.Add(new MarkdownInline(match.Groups["linktext"].Value, InlineKind.Link, match.Groups["url"].Value));
                }
                else
                {
                    result.Add(new MarkdownInline(match.Groups["italic"].Success ? match.Groups["italic"].Value : match.Groups["italic2"].Value, InlineKind.Italic));
                }

                position = match.Index + match.Length;
            }

            if (position < text.Length)
            {
                result.Add(new MarkdownInline(text.Substring(position), InlineKind.Text));
            }

            return result;
        }

        /// <summary>True when the text uses markdown structure worth rendering (fences, headings, lists, tables or emphasis).</summary>
        public static bool LooksLikeMarkdown(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return text.Contains("```") || text.Contains("**") ||
                Parse(text).Any(b => b.Kind == MarkdownBlockKind.Heading || b.Kind == MarkdownBlockKind.Table || b.Kind == MarkdownBlockKind.ListItem);
        }

        private static bool IsTableRow(string line)
        {
            var trimmed = line.Trim();
            return trimmed.Length > 1 && trimmed[0] == '|' && trimmed.IndexOf('|', 1) > 0;
        }

        private static bool IsContinuation(string line, int indent)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            var leading = line.Length - line.TrimStart().Length;
            return leading > indent && !Bullet.IsMatch(line) && !Ordered.IsMatch(line) && !Fence.IsMatch(line);
        }

        private static string[] SplitRow(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("|", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(1);
            }

            if (trimmed.EndsWith("|", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }

            return trimmed.Split('|').Select(c => c.Trim()).ToArray();
        }
    }
}
