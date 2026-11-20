using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using CopilotActivityViewer.Formatting;
using CopilotActivityViewer.Model;
using Newtonsoft.Json.Linq;

namespace CopilotActivityViewer.ToolWindow
{
    /// <summary>Colors used by the details view. Two fixed palettes chosen from the tool window background brightness.</summary>
    public sealed class DetailsPalette
    {
        public static readonly DetailsPalette Dark = new DetailsPalette(true);
        public static readonly DetailsPalette Light = new DetailsPalette(false);

        private readonly Dictionary<TokenKind, Brush> tokens;

        private DetailsPalette(bool dark)
        {
            IsDark = dark;
            Muted = Freeze(dark ? 0x9A9A9A : 0x6A6A6A);
            CodeBackground = Freeze(dark ? 0x181818 : 0xF5F5F5);
            InlineCodeBackground = Freeze(dark ? 0x2D2D30 : 0xEAEAEA);
            Border = Freeze(dark ? 0x3F3F46 : 0xD0D0D0);
            Accent = Freeze(dark ? 0x4FA3F7 : 0x0066BF);
            Error = Freeze(dark ? 0xF48771 : 0xC72E2E);
            Warning = Freeze(dark ? 0xE0A000 : 0x9A6700);
            tokens = new Dictionary<TokenKind, Brush>
            {
                [TokenKind.Comment] = Freeze(dark ? 0x6A9955 : 0x008000),
                [TokenKind.String] = Freeze(dark ? 0xCE9178 : 0xA31515),
                [TokenKind.Number] = Freeze(dark ? 0xB5CEA8 : 0x098658),
                [TokenKind.Keyword] = Freeze(dark ? 0x569CD6 : 0x0000FF),
                [TokenKind.Type] = Freeze(dark ? 0x4EC9B0 : 0x267F99),
                [TokenKind.Variable] = Freeze(dark ? 0x9CDCFE : 0x001080),
                [TokenKind.Parameter] = Freeze(dark ? 0xA0A0A0 : 0x5A5A5A),
                [TokenKind.Command] = Freeze(dark ? 0xDCDCAA : 0x795E26),
                [TokenKind.Tag] = Freeze(dark ? 0x569CD6 : 0x800000),
                [TokenKind.Attribute] = Freeze(dark ? 0x9CDCFE : 0xE50000),
                [TokenKind.Property] = Freeze(dark ? 0x9CDCFE : 0x0451A5),
                [TokenKind.Inserted] = Freeze(dark ? 0x81C784 : 0x116329),
                [TokenKind.Deleted] = Freeze(dark ? 0xF48771 : 0xB31D28),
                [TokenKind.Hunk] = Freeze(dark ? 0xC586C0 : 0x6F42C1),
            };
        }

        public bool IsDark { get; }

        public Brush Muted { get; }

        public Brush CodeBackground { get; }

        public Brush InlineCodeBackground { get; }

        public Brush Border { get; }

        public Brush Accent { get; }

        public Brush Error { get; }

        public Brush Warning { get; }

        public Brush TokenBrush(TokenKind kind) => tokens.TryGetValue(kind, out var brush) ? brush : null;

        public static DetailsPalette For(Brush background)
        {
            if (background is SolidColorBrush solid)
            {
                var c = solid.Color;
                var luminance = (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
                return luminance < 128 ? Dark : Light;
            }

            return Dark;
        }

        private static Brush Freeze(int rgb)
        {
            var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>Builds a readable <see cref="FlowDocument"/> for an activity: arguments, scripts and results with syntax highlighting, markdown messages.</summary>
    public sealed class DetailsDocumentBuilder
    {
        private static readonly FontFamily CodeFont = new FontFamily("Cascadia Mono, Consolas, Courier New");

        private static readonly HashSet<string> ScriptKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "command", "script", "code", "cmd", "commandLine",
        };

        private static readonly HashSet<string> FileContentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "content", "newString", "oldString", "new_str", "old_str", "file_text", "newContent", "oldContent", "insert_text", "text",
        };

        private static readonly HashSet<string> ProseKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "prompt", "message", "summary", "question", "body", "instructions", "description", "reason", "explanation", "context",
        };

        private static readonly string[] PathKeys = { "filePath", "path", "file", "filename", "fileName", "target_file", "targetFile" };

        private static readonly HashSet<string> FileReadTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "view", "get_file", "read_file", "readFile", "cat",
        };

        private static readonly HashSet<string> ProseResultTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "task", "read_agent", "web_fetch", "ask_question", "skill",
        };

        private readonly DetailsPalette palette;
        private readonly Brush foreground;
        private readonly FontFamily textFont;
        private readonly double fontSize;

        public DetailsDocumentBuilder(DetailsPalette palette, Brush foreground, FontFamily textFont, double fontSize)
        {
            this.palette = palette ?? DetailsPalette.Dark;
            this.foreground = foreground ?? Brushes.Black;
            this.textFont = textFont ?? new FontFamily("Segoe UI");
            this.fontSize = fontSize > 0 ? fontSize : 12;
        }

        public FlowDocument CreateDocument()
        {
            return new FlowDocument
            {
                FontFamily = textFont,
                FontSize = fontSize,
                Foreground = foreground,
                Background = Brushes.Transparent,
                PagePadding = new Thickness(8, 6, 8, 8),
                TextAlignment = TextAlignment.Left,
                IsHyphenationEnabled = false,
            };
        }

        public FlowDocument BuildPlaceholder(string text)
        {
            var document = CreateDocument();
            document.Blocks.Add(new Paragraph(new Run(text)) { Foreground = palette.Muted, FontStyle = FontStyles.Italic });
            return document;
        }

        public FlowDocument Build(ActivityItem item)
        {
            if (item == null)
            {
                return BuildPlaceholder("Select an activity to see its details.");
            }

            var document = CreateDocument();
            var blocks = document.Blocks;
            AddHeader(blocks, item);

            if (item.Properties != null && item.Properties.Count > 0)
            {
                AddKeyValueTable(blocks, item.Properties.Where(p => !string.IsNullOrWhiteSpace(p.Value)).ToList());
            }

            var rendered = false;
            if (item.Arguments != null && item.Arguments.HasValues)
            {
                AddSectionTitle(blocks, item.Kind == ActivityKind.Tool ? "Arguments" : "Task");
                AddObject(blocks, item.ToolName, item.Arguments, 0);
                rendered = true;
            }

            if (!string.IsNullOrWhiteSpace(item.Body))
            {
                AddMarkdown(blocks, item.Body);
                rendered = true;
            }

            if (!string.IsNullOrWhiteSpace(item.Note))
            {
                blocks.Add(new Paragraph(new Run(item.Note)) { Foreground = palette.Warning, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
                rendered = true;
            }

            if (!string.IsNullOrEmpty(item.ResultText))
            {
                AddSectionTitle(blocks, item.ResultFailed ? "Error" : "Result", item.ResultFailed ? palette.Error : null);
                AddResult(blocks, item);
                rendered = true;
            }
            else if (item.Kind == ActivityKind.Tool && item.IsActive)
            {
                blocks.Add(new Paragraph(new Run(item.Status == ActivityStatus.AwaitingApproval ? "Waiting for approval…" : "Running…"))
                {
                    Foreground = palette.Muted,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 8, 0, 0),
                });
            }

            if (!rendered && item.Kind != ActivityKind.Tool && !string.IsNullOrWhiteSpace(item.Details) && item.Properties == null)
            {
                AddMarkdown(blocks, item.Details);
            }

            return document;
        }

        private void AddHeader(BlockCollection blocks, ActivityItem item)
        {
            var title = new Paragraph { Margin = new Thickness(0, 0, 0, 2), FontSize = fontSize + 2, FontWeight = FontWeights.SemiBold };
            title.Inlines.Add(new Run(item.Title ?? item.KindText));
            blocks.Add(title);

            var meta = new List<string> { item.KindText, item.AgentLabel, item.Timestamp.ToLocalTime().ToString("HH:mm:ss") };
            if (!string.IsNullOrEmpty(item.ToolName) && item.Kind == ActivityKind.Tool && !string.Equals(item.ToolName, item.Title, StringComparison.Ordinal))
            {
                meta.Insert(1, item.ToolName);
            }

            if (!string.IsNullOrEmpty(item.StatusText))
            {
                meta.Add(item.StatusText);
            }

            if (!string.IsNullOrEmpty(item.DurationText))
            {
                meta.Add(item.DurationText);
            }

            var line = new Paragraph { Margin = new Thickness(0, 0, 0, 6), Foreground = palette.Muted, FontSize = Math.Max(fontSize - 1, 9) };
            line.Inlines.Add(new Run(string.Join("  ·  ", meta.Where(m => !string.IsNullOrEmpty(m)))));
            if (item.Status == ActivityStatus.Failed)
            {
                line.Inlines.Add(new Run("  ✖") { Foreground = palette.Error });
            }

            blocks.Add(line);

            if (item.Kind == ActivityKind.Tool && !string.IsNullOrWhiteSpace(item.Subtitle) && item.Arguments == null)
            {
                blocks.Add(new Paragraph(new Run(item.Subtitle)) { Margin = new Thickness(0, 0, 0, 6) });
            }
        }

        private void AddSectionTitle(BlockCollection blocks, string text, Brush brush = null)
        {
            blocks.Add(new Paragraph(new Run(text.ToUpperInvariant()))
            {
                Foreground = brush ?? palette.Muted,
                FontWeight = FontWeights.Bold,
                FontSize = Math.Max(fontSize - 2, 9),
                Margin = new Thickness(0, 10, 0, 3),
            });
        }

        private void AddObject(BlockCollection blocks, string toolName, JObject obj, int depth)
        {
            var pending = new List<KeyValuePair<string, string>>();
            void Flush()
            {
                if (pending.Count > 0)
                {
                    AddKeyValueTable(blocks, pending.ToList(), depth);
                    pending.Clear();
                }
            }

            var languageHint = FindPathLanguage(obj);
            foreach (var property in obj.Properties())
            {
                var key = property.Name;
                var value = property.Value;
                switch (value.Type)
                {
                    case JTokenType.String:
                        {
                            var text = (string)value ?? string.Empty;
                            var multiline = text.IndexOf('\n') >= 0;
                            if (ScriptKeys.Contains(key))
                            {
                                Flush();
                                AddLabel(blocks, key, depth);
                                var language = CodeLanguages.FromToolName(toolName);
                                AddCode(blocks, text, language == CodeLanguage.Plain ? CodeLanguages.Guess(text) : language, depth);
                            }
                            else if (FileContentKeys.Contains(key) && (multiline || text.Length > 80))
                            {
                                Flush();
                                AddLabel(blocks, key, depth);
                                AddCode(blocks, text, languageHint != CodeLanguage.Plain ? languageHint : CodeLanguages.Guess(text), depth);
                            }
                            else if (ProseKeys.Contains(key) && (multiline || text.Length > 140))
                            {
                                Flush();
                                AddLabel(blocks, key, depth);
                                AddMarkdown(blocks, text, depth);
                            }
                            else if (multiline)
                            {
                                Flush();
                                AddLabel(blocks, key, depth);
                                var guessed = CodeLanguages.Guess(text);
                                if (guessed == CodeLanguage.Plain && MarkdownParser.LooksLikeMarkdown(text))
                                {
                                    AddMarkdown(blocks, text, depth);
                                }
                                else
                                {
                                    AddCode(blocks, text, guessed, depth);
                                }
                            }
                            else
                            {
                                pending.Add(new KeyValuePair<string, string>(key, text));
                            }

                            break;
                        }

                    case JTokenType.Object:
                        Flush();
                        AddLabel(blocks, key, depth);
                        AddObject(blocks, toolName, (JObject)value, depth + 1);
                        break;

                    case JTokenType.Array:
                        {
                            var array = (JArray)value;
                            if (array.All(t => t is JValue) && array.ToString(Newtonsoft.Json.Formatting.None).Length <= 160)
                            {
                                pending.Add(new KeyValuePair<string, string>(key, string.Join(", ", array.Select(t => t.ToString()))));
                                break;
                            }

                            Flush();
                            if (array.All(t => t is JObject))
                            {
                                for (var i = 0; i < array.Count; i++)
                                {
                                    AddLabel(blocks, key + " [" + (i + 1) + "/" + array.Count + "]", depth);
                                    AddObject(blocks, toolName, (JObject)array[i], depth + 1);
                                }
                            }
                            else
                            {
                                AddLabel(blocks, key, depth);
                                AddCode(blocks, array.ToString(Newtonsoft.Json.Formatting.Indented), CodeLanguage.Json, depth);
                            }

                            break;
                        }

                    case JTokenType.Null:
                    case JTokenType.Undefined:
                        break;

                    default:
                        pending.Add(new KeyValuePair<string, string>(key, value.ToString(Newtonsoft.Json.Formatting.None)));
                        break;
                }
            }

            Flush();
        }

        private void AddResult(BlockCollection blocks, ActivityItem item)
        {
            var text = item.ResultText;
            if (item.ResultFailed)
            {
                AddCode(blocks, text, CodeLanguage.Plain, 0, palette.Error);
                return;
            }

            if (FileReadTools.Contains(item.ToolName ?? string.Empty))
            {
                var language = item.Arguments != null ? FindPathLanguage(item.Arguments) : CodeLanguage.Plain;
                AddCode(blocks, text, language, 0);
                return;
            }

            var guessed = CodeLanguages.Guess(text);
            if (guessed != CodeLanguage.Plain)
            {
                AddCode(blocks, text, guessed, 0);
                return;
            }

            if (ProseResultTools.Contains(item.ToolName ?? string.Empty) || MarkdownParser.LooksLikeMarkdown(text))
            {
                AddMarkdown(blocks, text);
                return;
            }

            AddCode(blocks, text, CodeLanguage.Plain, 0);
        }

        private void AddLabel(BlockCollection blocks, string text, int depth)
        {
            blocks.Add(new Paragraph(new Run(text))
            {
                Foreground = palette.Muted,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(depth * 14, 6, 0, 2),
            });
        }

        private void AddKeyValueTable(BlockCollection blocks, IList<KeyValuePair<string, string>> rows, int depth = 0)
        {
            if (rows.Count == 0)
            {
                return;
            }

            var table = new Table { CellSpacing = 0, Margin = new Thickness(depth * 14, 2, 0, 2) };
            var keyWidth = Math.Min(Math.Max(rows.Max(r => r.Key.Length) * (fontSize * 0.62), 60), 200);
            table.Columns.Add(new TableColumn { Width = new GridLength(keyWidth + 12) });
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            var group = new TableRowGroup();
            foreach (var row in rows)
            {
                var tableRow = new TableRow();
                tableRow.Cells.Add(new TableCell(new Paragraph(new Run(row.Key)) { Foreground = palette.Muted, Margin = new Thickness(0, 1, 8, 1) }));
                var value = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                if (LooksLikePath(row.Value))
                {
                    value.Inlines.Add(new Run(row.Value) { FontFamily = CodeFont });
                }
                else
                {
                    AddInlines(value.Inlines, row.Value);
                }

                tableRow.Cells.Add(new TableCell(value));
                group.Rows.Add(tableRow);
            }

            table.RowGroups.Add(group);
            blocks.Add(table);
        }

        private void AddCode(BlockCollection blocks, string code, CodeLanguage language, int depth, Brush plainBrush = null)
        {
            code = (code ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
            if (language == CodeLanguage.Json && CodeLanguages.TryFormatJson(code, out var formatted))
            {
                code = formatted.Replace("\r\n", "\n");
            }

            if (language == CodeLanguage.Markdown)
            {
                AddMarkdown(blocks, code, depth);
                return;
            }

            var paragraph = new Paragraph
            {
                FontFamily = CodeFont,
                FontSize = Math.Max(fontSize - 0.5, 9),
                Background = palette.CodeBackground,
                BorderBrush = palette.Border,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(depth * 14, 2, 0, 6),
                LineHeight = double.NaN,
            };

            if (plainBrush != null)
            {
                paragraph.Foreground = plainBrush;
            }

            foreach (var token in SyntaxTokenizer.Tokenize(code, language))
            {
                AppendWithLineBreaks(paragraph.Inlines, token.Text, token.Kind == TokenKind.Text ? null : palette.TokenBrush(token.Kind));
            }

            blocks.Add(paragraph);
        }

        private void AddMarkdown(BlockCollection blocks, string text, int depth = 0)
        {
            var left = depth * 14;
            foreach (var block in MarkdownParser.Parse(text))
            {
                switch (block.Kind)
                {
                    case MarkdownBlockKind.Heading:
                        {
                            var paragraph = new Paragraph
                            {
                                FontWeight = FontWeights.SemiBold,
                                FontSize = fontSize + Math.Max(0, 4 - block.Level),
                                Margin = new Thickness(left, 8, 0, 3),
                            };
                            AddInlines(paragraph.Inlines, block.Text);
                            blocks.Add(paragraph);
                            break;
                        }

                    case MarkdownBlockKind.ListItem:
                        {
                            var indent = left + 6 + (block.Level * 16);
                            var paragraph = new Paragraph { Margin = new Thickness(indent + 14, 1, 0, 1), TextIndent = -14 };
                            paragraph.Inlines.Add(new Run(block.Marker == "•" ? "•  " : block.Marker + " ") { Foreground = palette.Muted });
                            AddInlines(paragraph.Inlines, block.Text);
                            blocks.Add(paragraph);
                            break;
                        }

                    case MarkdownBlockKind.Quote:
                        {
                            var paragraph = new Paragraph
                            {
                                Margin = new Thickness(left, 3, 0, 3),
                                Padding = new Thickness(8, 2, 0, 2),
                                BorderBrush = palette.Border,
                                BorderThickness = new Thickness(3, 0, 0, 0),
                                Foreground = palette.Muted,
                            };
                            AddInlines(paragraph.Inlines, block.Text);
                            blocks.Add(paragraph);
                            break;
                        }

                    case MarkdownBlockKind.Code:
                        AddCode(blocks, block.Text, block.Language == CodeLanguage.Plain ? CodeLanguages.Guess(block.Text) : block.Language, depth);
                        break;

                    case MarkdownBlockKind.Table:
                        AddMarkdownTable(blocks, block.Rows, left);
                        break;

                    case MarkdownBlockKind.Rule:
                        blocks.Add(new Paragraph { Margin = new Thickness(left, 4, 0, 4), BorderBrush = palette.Border, BorderThickness = new Thickness(0, 0, 0, 1), FontSize = 2 });
                        break;

                    default:
                        {
                            var paragraph = new Paragraph { Margin = new Thickness(left, 3, 0, 5) };
                            AddInlines(paragraph.Inlines, block.Text);
                            blocks.Add(paragraph);
                            break;
                        }
                }
            }
        }

        private void AddMarkdownTable(BlockCollection blocks, List<string[]> rows, double left)
        {
            if (rows == null || rows.Count == 0)
            {
                return;
            }

            var columns = rows.Max(r => r.Length);
            var table = new Table { CellSpacing = 0, Margin = new Thickness(left, 4, 0, 6), BorderBrush = palette.Border, BorderThickness = new Thickness(1) };
            for (var c = 0; c < columns; c++)
            {
                table.Columns.Add(new TableColumn());
            }

            var group = new TableRowGroup();
            for (var r = 0; r < rows.Count; r++)
            {
                var row = new TableRow();
                if (r == 0)
                {
                    row.Background = palette.InlineCodeBackground;
                    row.FontWeight = FontWeights.SemiBold;
                }

                for (var c = 0; c < columns; c++)
                {
                    var paragraph = new Paragraph { Margin = new Thickness(0) };
                    AddInlines(paragraph.Inlines, c < rows[r].Length ? rows[r][c] : string.Empty);
                    row.Cells.Add(new TableCell(paragraph)
                    {
                        Padding = new Thickness(6, 2, 6, 2),
                        BorderBrush = palette.Border,
                        BorderThickness = new Thickness(0, 0, c < columns - 1 ? 1 : 0, r < rows.Count - 1 ? 1 : 0),
                    });
                }

                group.Rows.Add(row);
            }

            table.RowGroups.Add(group);
            blocks.Add(table);
        }

        private void AddInlines(InlineCollection inlines, string text)
        {
            foreach (var inline in MarkdownParser.ParseInline(text ?? string.Empty))
            {
                switch (inline.Kind)
                {
                    case InlineKind.Bold:
                        {
                            var bold = new Bold();
                            AppendWithLineBreaks(bold.Inlines, inline.Text, null);
                            inlines.Add(bold);
                            break;
                        }

                    case InlineKind.Italic:
                        {
                            var italic = new Italic();
                            AppendWithLineBreaks(italic.Inlines, inline.Text, null);
                            inlines.Add(italic);
                            break;
                        }

                    case InlineKind.Code:
                        inlines.Add(new Run(" " + inline.Text + " ") { FontFamily = CodeFont, Background = palette.InlineCodeBackground });
                        break;

                    case InlineKind.Link:
                        inlines.Add(new Run(inline.Text) { Foreground = palette.Accent, TextDecorations = TextDecorations.Underline, ToolTip = Uri.UnescapeDataString(inline.Target ?? string.Empty) });
                        break;

                    default:
                        AppendWithLineBreaks(inlines, inline.Text, null);
                        break;
                }
            }
        }

        private static void AppendWithLineBreaks(InlineCollection inlines, string text, Brush brush)
        {
            var parts = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    inlines.Add(new LineBreak());
                }

                if (parts[i].Length > 0)
                {
                    var run = new Run(parts[i]);
                    if (brush != null)
                    {
                        run.Foreground = brush;
                    }

                    inlines.Add(run);
                }
            }
        }

        private static CodeLanguage FindPathLanguage(JObject obj)
        {
            foreach (var key in PathKeys)
            {
                if (obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var token) && token.Type == JTokenType.String)
                {
                    var language = CodeLanguages.FromPath((string)token);
                    if (language != CodeLanguage.Plain)
                    {
                        return language;
                    }
                }
            }

            return CodeLanguage.Plain;
        }

        private static bool LooksLikePath(string value) =>
            !string.IsNullOrEmpty(value) && value.Length < 400 && value.IndexOf(' ') < 0 &&
            (value.IndexOf('\\') >= 0 || value.IndexOf('/') >= 0) && value.IndexOf('\n') < 0;
    }
}
