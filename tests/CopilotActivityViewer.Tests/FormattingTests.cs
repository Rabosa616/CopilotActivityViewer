using System.Linq;
using CopilotActivityViewer.Formatting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CopilotActivityViewer.Tests
{
    [TestClass]
    public class FormattingTests
    {
        [TestMethod]
        public void PowerShellTokensAreClassified()
        {
            var tokens = SyntaxTokenizer.Tokenize("# list\nGet-ChildItem -Path $root -Filter \"*.cs\" | Where-Object { $_.Length -gt 10 }", CodeLanguage.PowerShell);

            Assert.AreEqual(TokenKind.Comment, Kind(tokens, "# list"));
            Assert.AreEqual(TokenKind.Command, Kind(tokens, "Get-ChildItem"));
            Assert.AreEqual(TokenKind.Parameter, Kind(tokens, "-Path"));
            Assert.AreEqual(TokenKind.Variable, Kind(tokens, "$root"));
            Assert.AreEqual(TokenKind.String, Kind(tokens, "\"*.cs\""));
            Assert.AreEqual(TokenKind.Command, Kind(tokens, "Where-Object"));
            Assert.AreEqual(TokenKind.Number, Kind(tokens, "10"));
            Assert.AreEqual("# list\nGet-ChildItem -Path $root -Filter \"*.cs\" | Where-Object { $_.Length -gt 10 }", string.Concat(tokens.Select(t => t.Text)));
        }

        [TestMethod]
        public void CSharpJsonXmlAndDiffTokensAreClassified()
        {
            var cs = SyntaxTokenizer.Tokenize("public sealed class Foo { // note\n var s = \"x\"; }", CodeLanguage.CSharp);
            Assert.AreEqual(TokenKind.Keyword, Kind(cs, "public"));
            Assert.AreEqual(TokenKind.Type, Kind(cs, "Foo"));
            Assert.AreEqual(TokenKind.Comment, Kind(cs, "// note"));
            Assert.AreEqual(TokenKind.String, Kind(cs, "\"x\""));

            var json = SyntaxTokenizer.Tokenize("{\"name\": \"value\", \"n\": 12, \"ok\": true}", CodeLanguage.Json);
            Assert.AreEqual(TokenKind.Property, Kind(json, "\"name\""));
            Assert.AreEqual(TokenKind.String, Kind(json, "\"value\""));
            Assert.AreEqual(TokenKind.Number, Kind(json, "12"));
            Assert.AreEqual(TokenKind.Keyword, Kind(json, "true"));

            var xml = SyntaxTokenizer.Tokenize("<Project Sdk=\"x\"><!-- c --></Project>", CodeLanguage.Xml);
            Assert.AreEqual(TokenKind.Tag, Kind(xml, "<Project"));
            Assert.AreEqual(TokenKind.Attribute, Kind(xml, "Sdk"));
            Assert.AreEqual(TokenKind.String, Kind(xml, "\"x\""));
            Assert.AreEqual(TokenKind.Comment, Kind(xml, "<!-- c -->"));

            var diff = SyntaxTokenizer.Tokenize("@@ -1 +1 @@\n-old\n+new\n same", CodeLanguage.Diff);
            Assert.AreEqual(TokenKind.Hunk, Kind(diff, "@@ -1 +1 @@"));
            Assert.AreEqual(TokenKind.Deleted, Kind(diff, "-old"));
            Assert.AreEqual(TokenKind.Inserted, Kind(diff, "+new"));
        }

        [TestMethod]
        public void LanguagesAreDetectedFromNamesPathsToolsAndContent()
        {
            Assert.AreEqual(CodeLanguage.CSharp, CodeLanguages.FromName("csharp"));
            Assert.AreEqual(CodeLanguage.PowerShell, CodeLanguages.FromName("ps1"));
            Assert.AreEqual(CodeLanguage.CSharp, CodeLanguages.FromPath(@"C:\repo\Foo.cs"));
            Assert.AreEqual(CodeLanguage.Xml, CodeLanguages.FromPath(@"C:\repo\App.xaml"));
            Assert.AreEqual(CodeLanguage.Json, CodeLanguages.FromPath("package.json"));
            Assert.AreEqual(CodeLanguage.PowerShell, CodeLanguages.FromToolName("powershell"));
            Assert.AreEqual(CodeLanguage.Bash, CodeLanguages.FromToolName("bash"));
            Assert.AreEqual(CodeLanguage.Json, CodeLanguages.Guess("{\"a\": [1, 2]}"));
            Assert.AreEqual(CodeLanguage.Xml, CodeLanguages.Guess("<root><a /></root>"));
            Assert.AreEqual(CodeLanguage.Diff, CodeLanguages.Guess("diff --git a/x b/x\n@@ -1 +1 @@\n-a\n+b"));
            Assert.AreEqual(CodeLanguage.Plain, CodeLanguages.Guess("Build succeeded."));

            Assert.IsTrue(CodeLanguages.TryFormatJson("{\"a\":{\"b\":1}}", out var formatted));
            StringAssert.Contains(formatted, "\n");
            Assert.IsFalse(CodeLanguages.TryFormatJson("not json", out _));
        }

        [TestMethod]
        public void MarkdownBlocksAndInlinesAreParsed()
        {
            var blocks = MarkdownParser.Parse(
                "## Plan\n\nSome **bold** and `code` text.\n\n- first\n- second\n\n1. one\n\n```powershell\nGet-Item .\n```\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n> quoted");

            Assert.AreEqual(MarkdownBlockKind.Heading, blocks[0].Kind);
            Assert.AreEqual(2, blocks[0].Level);
            Assert.AreEqual("Plan", blocks[0].Text);
            Assert.AreEqual(MarkdownBlockKind.Paragraph, blocks[1].Kind);
            Assert.AreEqual(3, blocks.Count(b => b.Kind == MarkdownBlockKind.ListItem));
            Assert.AreEqual("1.", blocks.Last(b => b.Kind == MarkdownBlockKind.ListItem).Marker);

            var code = blocks.Single(b => b.Kind == MarkdownBlockKind.Code);
            Assert.AreEqual(CodeLanguage.PowerShell, code.Language);
            Assert.AreEqual("Get-Item .", code.Text);

            var table = blocks.Single(b => b.Kind == MarkdownBlockKind.Table);
            Assert.HasCount(2, table.Rows);
            CollectionAssert.AreEqual(new[] { "1", "2" }, table.Rows[1]);
            Assert.AreEqual(MarkdownBlockKind.Quote, blocks.Last().Kind);

            var inlines = MarkdownParser.ParseInline(blocks[1].Text);
            Assert.IsTrue(inlines.Any(i => i.Kind == InlineKind.Bold && i.Text == "bold"));
            Assert.IsTrue(inlines.Any(i => i.Kind == InlineKind.Code && i.Text == "code"));

            Assert.IsTrue(MarkdownParser.LooksLikeMarkdown("Done:\n- item one\n- item two"));
            Assert.IsFalse(MarkdownParser.LooksLikeMarkdown("Build succeeded"));
        }

        private static TokenKind Kind(System.Collections.Generic.IEnumerable<CodeToken> tokens, string text)
        {
            var match = tokens.Where(t => t.Text == text).ToList();
            Assert.HasCount(1, match, "Token not found: " + text + " in " + string.Join(" | ", tokens));
            return match[0].Kind;
        }
    }
}
