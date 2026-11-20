using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CopilotActivityViewer.Formatting;
using CopilotActivityViewer.Model;

namespace CopilotActivityViewer.ToolWindow;

/// <summary>
/// Builds the details of an activity as FlowDocument XAML (arguments, scripts and results with syntax highlighting, markdown messages).
/// The XAML is sent to the IDE as a <c>XamlFragment</c>: chrome colors are <c>DynamicResource</c>s defined by <c>ActivityControl.xaml</c>
/// so they follow the IDE theme, syntax colors use a palette readable on both dark and light themes.
/// </summary>
public static class DetailsXamlBuilder
{
    /// <summary>Resource keys defined in the tool window template (and by tests).</summary>
    public static class Keys
    {
        public const string Text = "CavText";
        public const string Muted = "CavMuted";
        public const string Border = "CavBorder";
        public const string Accent = "CavAccent";
    }

    internal const string CodeBackground = "#14808080";
    internal const string InlineCodeBackground = "#26808080";
    internal const string ErrorColor = "#E5484D";
    internal const string WarningColor = "#D09000";

    private const string CodeFont = "Cascadia Mono, Consolas, Courier New";
    private const double FontSize = 12;
    private const int MaxCodeLines = 2000;

    private static readonly Dictionary<TokenKind, string> TokenColors = new()
    {
        [TokenKind.Comment] = "#57A64A",
        [TokenKind.String] = "#D4803C",
        [TokenKind.Number] = "#4FA88A",
        [TokenKind.Keyword] = "#3B8EEA",
        [TokenKind.Type] = "#2AA198",
        [TokenKind.Variable] = "#4A9FD8",
        [TokenKind.Parameter] = "#8C8C8C",
        [TokenKind.Command] = "#B58900",
        [TokenKind.Tag] = "#3B8EEA",
        [TokenKind.Attribute] = "#D06D8F",
        [TokenKind.Property] = "#4A9FD8",
        [TokenKind.Inserted] = "#3FA34D",
        [TokenKind.Deleted] = "#E5484D",
        [TokenKind.Hunk] = "#A66BC9",
    };

    private static readonly HashSet<string> ScriptKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "command", "script", "code", "cmd", "commandLine",
    };

    private static readonly HashSet<string> FileContentKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "content", "newString", "oldString", "new_str", "old_str", "file_text", "newContent", "oldContent", "insert_text", "text",
    };

    private static readonly HashSet<string> ProseKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "prompt", "message", "summary", "question", "body", "instructions", "description", "reason", "explanation", "context",
    };

    private static readonly string[] PathKeys = ["filePath", "path", "file", "filename", "fileName", "target_file", "targetFile"];

    private static readonly HashSet<string> FileReadTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "view", "get_file", "read_file", "readFile", "cat",
    };

    private static readonly HashSet<string> ProseResultTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "task", "read_agent", "web_fetch", "ask_question", "skill",
    };

    public static string TokenColor(TokenKind kind) => TokenColors.TryGetValue(kind, out var color) ? color : null;

    public static string BuildPlaceholder(string text)
    {
        var x = new Writer();
        x.Open();
        x.Paragraph($"Foreground=\"{Dyn(Keys.Muted)}\" FontStyle=\"Italic\"", () => x.Run(text));
        return x.Close();
    }

    public static string Build(ActivityItem item)
    {
        if (item == null)
        {
            return BuildPlaceholder("Select an activity to see its details.");
        }

        var x = new Writer();
        x.Open();
        AddHeader(x, item);

        if (item.Properties != null && item.Properties.Count > 0)
        {
            AddKeyValueTable(x, item.Properties.Where(p => !string.IsNullOrWhiteSpace(p.Value)).ToList(), 0);
        }

        var rendered = false;
        if (item.Arguments != null && item.Arguments.Count > 0)
        {
            AddSectionTitle(x, item.Kind == ActivityKind.Tool ? "Arguments" : "Task");
            AddObject(x, item.ToolName, item.Arguments, 0);
            rendered = true;
        }

        if (!string.IsNullOrWhiteSpace(item.Body))
        {
            AddMarkdown(x, item.Body, 0);
            rendered = true;
        }

        if (!string.IsNullOrWhiteSpace(item.Note))
        {
            x.Paragraph($"Foreground=\"{WarningColor}\" FontWeight=\"SemiBold\" Margin=\"0,8,0,0\"", () => x.Run(item.Note));
            rendered = true;
        }

        if (!string.IsNullOrEmpty(item.ResultText))
        {
            AddSectionTitle(x, item.ResultFailed ? "Error" : "Result", item.ResultFailed ? ErrorColor : null);
            AddResult(x, item);
            rendered = true;
        }
        else if (item.Kind == ActivityKind.Tool && item.IsActive)
        {
            x.Paragraph(
                $"Foreground=\"{Dyn(Keys.Muted)}\" FontStyle=\"Italic\" Margin=\"0,8,0,0\"",
                () => x.Run(item.Status == ActivityStatus.AwaitingApproval ? "Waiting for approval…" : "Running…"));
        }

        if (!rendered && item.Kind != ActivityKind.Tool && !string.IsNullOrWhiteSpace(item.Details) && item.Properties == null)
        {
            AddMarkdown(x, item.Details, 0);
        }

        return x.Close();
    }

    private static void AddHeader(Writer x, ActivityItem item)
    {
        x.Paragraph($"Margin=\"0,0,0,2\" FontSize=\"{Num(FontSize + 2)}\" FontWeight=\"SemiBold\"", () => x.Run(item.Title ?? item.KindText));

        var meta = new List<string> { item.KindText, item.AgentLabel, item.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) };
        if (!string.IsNullOrEmpty(item.ToolName) && item.Kind == ActivityKind.Tool && !string.Equals(item.ToolName, item.Title, StringComparison.Ordinal))
        {
            meta.Insert(1, item.ToolName);
        }

        meta.Add(item.StatusText);
        meta.Add(item.DurationText);

        x.Paragraph($"Margin=\"0,0,0,6\" Foreground=\"{Dyn(Keys.Muted)}\" FontSize=\"{Num(FontSize - 1)}\"", () =>
        {
            x.Run(string.Join("  ·  ", meta.Where(m => !string.IsNullOrEmpty(m))));
            if (item.Status == ActivityStatus.Failed)
            {
                x.Run("  ✖", $"Foreground=\"{ErrorColor}\"");
            }
        });

        if (item.Kind == ActivityKind.Tool && !string.IsNullOrWhiteSpace(item.Subtitle) && item.Arguments == null)
        {
            x.Paragraph("Margin=\"0,0,0,6\"", () => x.Run(item.Subtitle));
        }
    }

    private static void AddSectionTitle(Writer x, string text, string color = null)
    {
        x.Paragraph(
            $"Foreground=\"{color ?? Dyn(Keys.Muted)}\" FontWeight=\"Bold\" FontSize=\"{Num(FontSize - 2)}\" Margin=\"0,10,0,3\"",
            () => x.Run(text.ToUpperInvariant()));
    }

    private static void AddObject(Writer x, string toolName, JsonObject obj, int depth)
    {
        var pending = new List<KeyValuePair<string, string>>();
        void Flush()
        {
            if (pending.Count > 0)
            {
                AddKeyValueTable(x, pending.ToList(), depth);
                pending.Clear();
            }
        }

        var languageHint = FindPathLanguage(obj);
        foreach (var property in obj)
        {
            var key = property.Key;
            var value = property.Value;
            switch (SessionModel.Kind(value))
            {
                case JsonValueKind.String:
                    {
                        var text = value.GetValue<string>() ?? string.Empty;
                        var multiline = text.Contains('\n');
                        if (ScriptKeys.Contains(key))
                        {
                            Flush();
                            AddLabel(x, key, depth);
                            var language = CodeLanguages.FromToolName(toolName);
                            AddCode(x, text, language == CodeLanguage.Plain ? CodeLanguages.Guess(text) : language, depth);
                        }
                        else if (FileContentKeys.Contains(key) && (multiline || text.Length > 80))
                        {
                            Flush();
                            AddLabel(x, key, depth);
                            AddCode(x, text, languageHint != CodeLanguage.Plain ? languageHint : CodeLanguages.Guess(text), depth);
                        }
                        else if (ProseKeys.Contains(key) && (multiline || text.Length > 140))
                        {
                            Flush();
                            AddLabel(x, key, depth);
                            AddMarkdown(x, text, depth);
                        }
                        else if (multiline)
                        {
                            Flush();
                            AddLabel(x, key, depth);
                            var guessed = CodeLanguages.Guess(text);
                            if (guessed == CodeLanguage.Plain && MarkdownParser.LooksLikeMarkdown(text))
                            {
                                AddMarkdown(x, text, depth);
                            }
                            else
                            {
                                AddCode(x, text, guessed, depth);
                            }
                        }
                        else
                        {
                            pending.Add(new(key, text));
                        }

                        break;
                    }

                case JsonValueKind.Object:
                    Flush();
                    AddLabel(x, key, depth);
                    AddObject(x, toolName, value.AsObject(), depth + 1);
                    break;

                case JsonValueKind.Array:
                    {
                        var array = value.AsArray();
                        var compact = array.ToJsonString(SessionModel.CompactJson);
                        if (array.All(t => t is null or JsonValue) && compact.Length <= 160)
                        {
                            pending.Add(new(key, string.Join(", ", array.Select(t => SessionModel.Kind(t) == JsonValueKind.String ? t.GetValue<string>() : t?.ToJsonString() ?? "null"))));
                            break;
                        }

                        Flush();
                        if (array.All(t => t is JsonObject))
                        {
                            for (var i = 0; i < array.Count; i++)
                            {
                                AddLabel(x, key + " [" + (i + 1) + "/" + array.Count + "]", depth);
                                AddObject(x, toolName, array[i].AsObject(), depth + 1);
                            }
                        }
                        else
                        {
                            AddLabel(x, key, depth);
                            AddCode(x, compact, CodeLanguage.Json, depth);
                        }

                        break;
                    }

                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    break;

                default:
                    pending.Add(new(key, value.ToJsonString(SessionModel.CompactJson)));
                    break;
            }
        }

        Flush();
    }

    private static void AddResult(Writer x, ActivityItem item)
    {
        var text = item.ResultText;
        if (item.ResultFailed)
        {
            AddCode(x, text, CodeLanguage.Plain, 0, ErrorColor);
            return;
        }

        if (FileReadTools.Contains(item.ToolName ?? string.Empty))
        {
            var language = item.Arguments != null ? FindPathLanguage(item.Arguments) : CodeLanguage.Plain;
            AddCode(x, text, language, 0);
            return;
        }

        var guessed = CodeLanguages.Guess(text);
        if (guessed != CodeLanguage.Plain)
        {
            AddCode(x, text, guessed, 0);
            return;
        }

        if (ProseResultTools.Contains(item.ToolName ?? string.Empty) || MarkdownParser.LooksLikeMarkdown(text))
        {
            AddMarkdown(x, text, 0);
            return;
        }

        AddCode(x, text, CodeLanguage.Plain, 0);
    }

    private static void AddLabel(Writer x, string text, int depth)
    {
        x.Paragraph($"Foreground=\"{Dyn(Keys.Muted)}\" FontWeight=\"SemiBold\" Margin=\"{Num(depth * 14)},6,0,2\"", () => x.Run(text));
    }

    private static void AddKeyValueTable(Writer x, IList<KeyValuePair<string, string>> rows, int depth)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var keyWidth = Math.Min(Math.Max(rows.Max(r => r.Key.Length) * (FontSize * 0.62), 60), 200);
        // Only the key column is declared: next to a fixed column, WPF turns Width="*" into a near-zero column.
        x.Raw($"<Table CellSpacing=\"0\" Margin=\"{Num(depth * 14)},2,0,2\"><Table.Columns><TableColumn Width=\"{Num(keyWidth + 12)}\"/></Table.Columns><TableRowGroup>");
        foreach (var row in rows)
        {
            x.Raw("<TableRow><TableCell>");
            x.Paragraph($"Foreground=\"{Dyn(Keys.Muted)}\" Margin=\"0,1,8,1\"", () => x.Run(row.Key));
            x.Raw("</TableCell><TableCell>");
            x.Paragraph("Margin=\"0,1,0,1\"", () =>
            {
                if (LooksLikePath(row.Value))
                {
                    x.Run(row.Value, $"FontFamily=\"{CodeFont}\"");
                }
                else
                {
                    AddInlines(x, row.Value);
                }
            });
            x.Raw("</TableCell></TableRow>");
        }

        x.Raw("</TableRowGroup></Table>");
    }

    private static void AddCode(Writer x, string code, CodeLanguage language, int depth, string plainColor = null)
    {
        code = (code ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        if (language == CodeLanguage.Json && CodeLanguages.TryFormatJson(code, out var formatted))
        {
            code = formatted.Replace("\r\n", "\n");
        }

        if (language == CodeLanguage.Markdown)
        {
            AddMarkdown(x, code, depth);
            return;
        }

        code = LimitLines(code);
        var foreground = plainColor != null ? $" Foreground=\"{plainColor}\"" : string.Empty;
        x.Paragraph(
            $"FontFamily=\"{CodeFont}\" FontSize=\"{Num(FontSize - 0.5)}\" Background=\"{CodeBackground}\" BorderBrush=\"{Dyn(Keys.Border)}\" BorderThickness=\"1\" Padding=\"8,6,8,6\" Margin=\"{Num(depth * 14)},2,0,6\"{foreground}",
            () =>
            {
                foreach (var token in SyntaxTokenizer.Tokenize(code, language))
                {
                    x.RunsWithLineBreaks(token.Text, token.Kind == TokenKind.Text ? null : $"Foreground=\"{TokenColor(token.Kind)}\"");
                }
            });
    }

    private static void AddMarkdown(Writer x, string text, int depth)
    {
        var left = depth * 14;
        foreach (var block in MarkdownParser.Parse(text))
        {
            switch (block.Kind)
            {
                case MarkdownBlockKind.Heading:
                    x.Paragraph(
                        $"FontWeight=\"SemiBold\" FontSize=\"{Num(FontSize + Math.Max(0, 4 - block.Level))}\" Margin=\"{Num(left)},8,0,3\"",
                        () => AddInlines(x, block.Text));
                    break;

                case MarkdownBlockKind.ListItem:
                    {
                        var indent = left + 6 + (block.Level * 16);
                        x.Paragraph($"Margin=\"{Num(indent + 14)},1,0,1\" TextIndent=\"-14\"", () =>
                        {
                            x.Run(block.Marker == "•" ? "•  " : block.Marker + " ", $"Foreground=\"{Dyn(Keys.Muted)}\"");
                            AddInlines(x, block.Text);
                        });
                        break;
                    }

                case MarkdownBlockKind.Quote:
                    x.Paragraph(
                        $"Margin=\"{Num(left)},3,0,3\" Padding=\"8,2,0,2\" BorderBrush=\"{Dyn(Keys.Border)}\" BorderThickness=\"3,0,0,0\" Foreground=\"{Dyn(Keys.Muted)}\"",
                        () => AddInlines(x, block.Text));
                    break;

                case MarkdownBlockKind.Code:
                    AddCode(x, block.Text, block.Language == CodeLanguage.Plain ? CodeLanguages.Guess(block.Text) : block.Language, depth);
                    break;

                case MarkdownBlockKind.Table:
                    AddMarkdownTable(x, block.Rows, left);
                    break;

                case MarkdownBlockKind.Rule:
                    x.Raw($"<Paragraph Margin=\"{Num(left)},4,0,4\" BorderBrush=\"{Dyn(Keys.Border)}\" BorderThickness=\"0,0,0,1\" FontSize=\"2\"/>");
                    break;

                default:
                    x.Paragraph($"Margin=\"{Num(left)},3,0,5\"", () => AddInlines(x, block.Text));
                    break;
            }
        }
    }

    private static void AddMarkdownTable(Writer x, List<string[]> rows, double left)
    {
        if (rows == null || rows.Count == 0)
        {
            return;
        }

        var columns = rows.Max(r => r.Length);
        x.Raw($"<Table CellSpacing=\"0\" Margin=\"{Num(left)},4,0,6\" BorderBrush=\"{Dyn(Keys.Border)}\" BorderThickness=\"1\"><Table.Columns>");
        for (var c = 0; c < columns; c++)
        {
            x.Raw("<TableColumn/>");
        }

        x.Raw("</Table.Columns><TableRowGroup>");
        for (var r = 0; r < rows.Count; r++)
        {
            x.Raw(r == 0 ? $"<TableRow Background=\"{InlineCodeBackground}\" FontWeight=\"SemiBold\">" : "<TableRow>");
            for (var c = 0; c < columns; c++)
            {
                var right = c < columns - 1 ? 1 : 0;
                var bottom = r < rows.Count - 1 ? 1 : 0;
                x.Raw($"<TableCell Padding=\"6,2,6,2\" BorderBrush=\"{Dyn(Keys.Border)}\" BorderThickness=\"0,0,{right},{bottom}\">");
                var cell = c < rows[r].Length ? rows[r][c] : string.Empty;
                x.Paragraph("Margin=\"0\"", () => AddInlines(x, cell));
                x.Raw("</TableCell>");
            }

            x.Raw("</TableRow>");
        }

        x.Raw("</TableRowGroup></Table>");
    }

    private static void AddInlines(Writer x, string text)
    {
        foreach (var inline in MarkdownParser.ParseInline(text ?? string.Empty))
        {
            switch (inline.Kind)
            {
                case InlineKind.Bold:
                    x.Raw("<Bold>");
                    x.RunsWithLineBreaks(inline.Text, null);
                    x.Raw("</Bold>");
                    break;

                case InlineKind.Italic:
                    x.Raw("<Italic>");
                    x.RunsWithLineBreaks(inline.Text, null);
                    x.Raw("</Italic>");
                    break;

                case InlineKind.Code:
                    x.Run(" " + inline.Text + " ", $"FontFamily=\"{CodeFont}\" Background=\"{InlineCodeBackground}\"");
                    break;

                case InlineKind.Link:
                    x.Run(inline.Text, $"Foreground=\"{Dyn(Keys.Accent)}\" TextDecorations=\"Underline\" ToolTip=\"{Writer.EscapeAttribute(Uri.UnescapeDataString(inline.Target ?? string.Empty))}\"");
                    break;

                default:
                    x.RunsWithLineBreaks(inline.Text, null);
                    break;
            }
        }
    }

    private static CodeLanguage FindPathLanguage(JsonObject obj)
    {
        foreach (var key in PathKeys)
        {
            foreach (var property in obj)
            {
                if (string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase) && SessionModel.Kind(property.Value) == JsonValueKind.String)
                {
                    var language = CodeLanguages.FromPath(property.Value.GetValue<string>());
                    if (language != CodeLanguage.Plain)
                    {
                        return language;
                    }
                }
            }
        }

        return CodeLanguage.Plain;
    }

    private static string LimitLines(string code)
    {
        var count = 0;
        for (var i = 0; i < code.Length; i++)
        {
            if (code[i] == '\n' && ++count >= MaxCodeLines)
            {
                return code.Substring(0, i) + "\n… (truncated)";
            }
        }

        return code;
    }

    private static bool LooksLikePath(string value) =>
        !string.IsNullOrEmpty(value) && value.Length < 400 && !value.Contains(' ') &&
        (value.Contains('\\') || value.Contains('/')) && !value.Contains('\n');

    private static string Dyn(string key) => "{DynamicResource " + key + "}";

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Minimal XAML writer: no whitespace between elements and <c>xml:space="preserve"</c> so code keeps its indentation.</summary>
    private sealed class Writer
    {
        private readonly StringBuilder sb = new();

        public void Open()
        {
            sb.Append("<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xml:space=\"preserve\"")
              .Append(" FontFamily=\"Segoe UI\" FontSize=\"").Append(Num(FontSize)).Append('"')
              .Append(" Foreground=\"").Append(Dyn(Keys.Text)).Append('"')
              .Append(" Background=\"Transparent\" PagePadding=\"8,6,8,8\" TextAlignment=\"Left\" IsHyphenationEnabled=\"False\">");
        }

        public string Close()
        {
            sb.Append("</FlowDocument>");
            return sb.ToString();
        }

        public void Raw(string xaml) => sb.Append(xaml);

        public void Paragraph(string attributes, Action content)
        {
            sb.Append("<Paragraph ").Append(attributes).Append('>');
            content();
            sb.Append("</Paragraph>");
        }

        public void Run(string text, string attributes = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            sb.Append("<Run");
            if (attributes != null)
            {
                sb.Append(' ').Append(attributes);
            }

            sb.Append('>');
            AppendEscaped(sb, text);
            sb.Append("</Run>");
        }

        public void RunsWithLineBreaks(string text, string attributes)
        {
            var parts = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append("<LineBreak/>");
                }

                Run(parts[i], attributes);
            }
        }

        public static string EscapeAttribute(string text)
        {
            var escaped = new StringBuilder();
            AppendEscaped(escaped, text);
            var value = escaped.ToString().Replace("\"", "&quot;");
            return value.StartsWith('{') ? "{}" + value : value;
        }

        private static void AppendEscaped(StringBuilder target, string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                switch (c)
                {
                    case '&': target.Append("&amp;"); break;
                    case '<': target.Append("&lt;"); break;
                    case '>': target.Append("&gt;"); break;
                    case '\t':
                    case '\n':
                        target.Append(c);
                        break;
                    default:
                        if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                        {
                            target.Append(c).Append(text[++i]);
                        }
                        else if (c >= ' ' && !char.IsSurrogate(c) && c != '\uFFFE' && c != '\uFFFF')
                        {
                            target.Append(c);
                        }

                        break;
                }
            }
        }
    }
}
