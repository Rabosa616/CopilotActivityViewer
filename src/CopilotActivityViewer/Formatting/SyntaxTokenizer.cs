using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CopilotActivityViewer.Formatting
{
    public enum TokenKind
    {
        Text,
        Comment,
        String,
        Number,
        Keyword,
        Type,
        Variable,
        Parameter,
        Command,
        Tag,
        Attribute,
        Property,
        Inserted,
        Deleted,
        Hunk,
    }

    public readonly struct CodeToken
    {
        public CodeToken(string text, TokenKind kind)
        {
            Text = text;
            Kind = kind;
        }

        public string Text { get; }

        public TokenKind Kind { get; }

        public override string ToString() => Kind + ":" + Text;
    }

    /// <summary>Small regex based highlighter: good enough to make scripts and code readable, not a full lexer.</summary>
    public static class SyntaxTokenizer
    {
        private static readonly string[] GroupNames =
        {
            "comment", "string", "property", "variable", "parameter", "command", "type", "keyword", "number", "tag", "attribute", "inserted", "deleted", "hunk",
        };

        private static readonly TokenKind[] GroupKinds =
        {
            TokenKind.Comment, TokenKind.String, TokenKind.Property, TokenKind.Variable, TokenKind.Parameter, TokenKind.Command, TokenKind.Type,
            TokenKind.Keyword, TokenKind.Number, TokenKind.Tag, TokenKind.Attribute, TokenKind.Inserted, TokenKind.Deleted, TokenKind.Hunk,
        };

        private static readonly ConcurrentDictionary<CodeLanguage, Regex> Cache = new ConcurrentDictionary<CodeLanguage, Regex>();

        private const string Number = @"(?<number>\b0[xX][0-9a-fA-F]+\b|\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b)";

        public static List<CodeToken> Tokenize(string code, CodeLanguage language)
        {
            var tokens = new List<CodeToken>();
            if (string.IsNullOrEmpty(code))
            {
                return tokens;
            }

            var regex = GetRegex(language);
            if (regex == null)
            {
                tokens.Add(new CodeToken(code, TokenKind.Text));
                return tokens;
            }

            var position = 0;
            foreach (Match match in regex.Matches(code))
            {
                if (match.Length == 0)
                {
                    continue;
                }

                if (match.Index > position)
                {
                    tokens.Add(new CodeToken(code.Substring(position, match.Index - position), TokenKind.Text));
                }

                tokens.Add(new CodeToken(match.Value, KindOf(match)));
                position = match.Index + match.Length;
            }

            if (position < code.Length)
            {
                tokens.Add(new CodeToken(code.Substring(position), TokenKind.Text));
            }

            return tokens;
        }

        private static TokenKind KindOf(Match match)
        {
            for (var i = 0; i < GroupNames.Length; i++)
            {
                if (match.Groups[GroupNames[i]].Success)
                {
                    return GroupKinds[i];
                }
            }

            return TokenKind.Text;
        }

        private static Regex GetRegex(CodeLanguage language) => Cache.GetOrAdd(language, Build);

        private static Regex Build(CodeLanguage language)
        {
            const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;
            switch (language)
            {
                case CodeLanguage.CSharp:
                    return new Regex(
                        @"(?<comment>//[^\n]*|/\*[\s\S]*?\*/)" +
                        @"|(?<string>@""(?:""""|[^""])*""|\$?""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])+')" +
                        @"|(?<keyword>\b(?:abstract|as|async|await|base|bool|break|byte|case|catch|char|checked|class|const|continue|decimal|default|delegate|do|double|else|enum|event|explicit|extern|false|finally|fixed|float|for|foreach|get|goto|if|implicit|in|init|int|interface|internal|is|lock|long|nameof|namespace|new|null|object|operator|out|override|params|partial|private|protected|public|readonly|record|ref|return|sbyte|sealed|set|short|sizeof|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|using|var|virtual|void|volatile|when|where|while|yield)\b)" +
                        @"|(?<type>\b[A-Z][A-Za-z0-9_]*\b)|" + Number,
                        Options);

                case CodeLanguage.PowerShell:
                    return new Regex(
                        @"(?<comment><\#[\s\S]*?\#>|\#[^\n]*)" +
                        @"|(?<string>@'[\s\S]*?'@|@""[\s\S]*?""@|""(?:`.|[^""`])*""|'(?:''|[^'])*')" +
                        @"|(?<variable>\$(?:\{[^}\n]+\}|[A-Za-z_][\w]*(?::[\w]+)?|[\$\?_]))" +
                        @"|(?<type>\[[A-Za-z][\w.]*(?:\[\])?\])" +
                        @"|(?<command>\b(?:Add|Clear|Compare|ConvertFrom|ConvertTo|Copy|Disable|Enable|Enter|Exit|Export|ForEach|Format|Get|Group|Import|Invoke|Join|Measure|Move|New|Out|Pop|Push|Read|Register|Remove|Rename|Resolve|Restart|Select|Set|Sort|Split|Start|Stop|Tee|Test|Update|Wait|Where|Write)-[A-Za-z]+\b)" +
                        @"|(?<parameter>(?<=^|[\s(|{;,])-[A-Za-z][\w]*)" +
                        @"|(?<keyword>\b(?:begin|break|catch|class|continue|do|else|elseif|end|enum|exit|filter|finally|for|foreach|function|if|in|param|process|return|switch|throw|trap|try|until|using|while)\b)|" + Number,
                        Options | RegexOptions.IgnoreCase | RegexOptions.Multiline);

                case CodeLanguage.Bash:
                    return new Regex(
                        @"(?<comment>(?<=^|\s)\#[^\n]*)" +
                        @"|(?<string>""(?:\\.|[^""\\])*""|'[^']*')" +
                        @"|(?<variable>\$(?:\{[^}\n]+\}|[A-Za-z_]\w*|[0-9@#?*$!-]))" +
                        @"|(?<parameter>(?<=^|\s)--?[A-Za-z][\w-]*)" +
                        @"|(?<keyword>\b(?:case|cd|do|done|echo|elif|else|esac|exit|export|fi|for|function|if|in|local|return|set|source|then|until|while)\b)|" + Number,
                        Options | RegexOptions.Multiline);

                case CodeLanguage.Python:
                    return new Regex(
                        @"(?<comment>\#[^\n]*)" +
                        @"|(?<string>[rRbBfFuU]{0,2}(?:""""""[\s\S]*?""""""|'''[\s\S]*?'''|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])*'))" +
                        @"|(?<attribute>@[A-Za-z_][\w.]*)" +
                        @"|(?<keyword>\b(?:False|None|True|and|as|assert|async|await|break|class|continue|def|del|elif|else|except|finally|for|from|global|if|import|in|is|lambda|nonlocal|not|or|pass|raise|return|self|try|while|with|yield)\b)" +
                        @"|(?<type>\b[A-Z][A-Za-z0-9_]*\b)|" + Number,
                        Options);

                case CodeLanguage.JavaScript:
                    return new Regex(
                        @"(?<comment>//[^\n]*|/\*[\s\S]*?\*/)" +
                        @"|(?<string>""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])*'|`(?:\\.|[^`\\])*`)" +
                        @"|(?<keyword>\b(?:abstract|any|as|async|await|boolean|break|case|catch|class|const|continue|default|delete|do|else|enum|export|extends|false|finally|for|from|function|if|implements|import|in|instanceof|interface|let|new|null|number|of|private|protected|public|readonly|return|static|string|super|switch|this|throw|true|try|type|typeof|undefined|var|void|while|yield)\b)" +
                        @"|(?<type>\b[A-Z][A-Za-z0-9_]*\b)|" + Number,
                        Options);

                case CodeLanguage.Json:
                    return new Regex(
                        @"(?<property>""(?:\\.|[^""\\])*""(?=\s*:))" +
                        @"|(?<string>""(?:\\.|[^""\\])*"")" +
                        @"|(?<keyword>\b(?:true|false|null)\b)" +
                        @"|(?<number>-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b)",
                        Options);

                case CodeLanguage.Xml:
                    return new Regex(
                        @"(?<comment><!--[\s\S]*?-->)" +
                        @"|(?<keyword><!\[CDATA\[|\]\]>|<\?[\w:-]+|\?>)" +
                        @"|(?<tag></?[A-Za-z_][\w:.-]*|/?>)" +
                        @"|(?<attribute>\b[A-Za-z_][\w:.-]*(?=\s*=\s*[""']))" +
                        @"|(?<string>(?<==\s*)(?:""[^""]*""|'[^']*'))",
                        Options);

                case CodeLanguage.Sql:
                    return new Regex(
                        @"(?<comment>--[^\n]*|/\*[\s\S]*?\*/)" +
                        @"|(?<string>N?'(?:''|[^'])*')" +
                        @"|(?<variable>@@?[A-Za-z_]\w*)" +
                        @"|(?<keyword>\b(?:add|all|alter|and|as|asc|begin|between|by|case|create|cross|declare|default|delete|desc|distinct|drop|else|end|exec|exists|from|full|group|having|if|in|index|inner|insert|into|is|join|left|like|merge|not|null|on|or|order|outer|primary|key|procedure|return|right|select|set|table|then|top|union|update|values|view|when|where|with)\b)|" + Number,
                        Options | RegexOptions.IgnoreCase);

                case CodeLanguage.Yaml:
                    return new Regex(
                        @"(?<comment>(?<=^|\s)\#[^\n]*)" +
                        @"|(?<property>^[ \t]*(?:-[ \t]+)?[^\s:#'""][^:\n]*?(?=:(?:\s|$)))" +
                        @"|(?<string>""(?:\\.|[^""\\])*""|'(?:''|[^'])*')" +
                        @"|(?<keyword>\b(?:true|false|null|yes|no)\b)|" + Number,
                        Options | RegexOptions.Multiline);

                case CodeLanguage.Diff:
                    return new Regex(
                        @"(?<keyword>^(?:diff |index |\+\+\+ |--- )[^\n]*)" +
                        @"|(?<hunk>^@@[^\n]*)" +
                        @"|(?<inserted>^\+[^\n]*)" +
                        @"|(?<deleted>^-[^\n]*)",
                        Options | RegexOptions.Multiline);

                default:
                    return null;
            }
        }
    }
}
