using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Documents;
using System.Windows.Media;
using CopilotActivityViewer.Formatting;
using CopilotActivityViewer.Model;
using CopilotActivityViewer.ToolWindow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CopilotActivityViewer.Tests
{
    [TestClass]
    public class DetailsAndFiltersTests
    {
        private const string Subagent = "aaaaaaaa-0000-0000-0000-000000000001";

        private static readonly string[] SessionEvents =
        {
            "{\"type\":\"user.message\",\"data\":{\"content\":\"Fix the bug\"},\"timestamp\":\"2026-11-19T10:00:01.000Z\"}",
            "{\"type\":\"assistant.message\",\"data\":{\"content\":\"## Plan\\n- build\\n- test\"},\"timestamp\":\"2026-11-19T10:00:02.000Z\"}",
            "{\"type\":\"tool.execution_start\",\"data\":{\"toolCallId\":\"t1\",\"toolName\":\"powershell\",\"arguments\":{\"command\":\"Get-ChildItem -Path $root | Select-Object -First 5\",\"description\":\"List files\"}},\"timestamp\":\"2026-11-19T10:00:03.000Z\"}",
            "{\"type\":\"tool.execution_complete\",\"data\":{\"toolCallId\":\"t1\",\"success\":true,\"result\":{\"content\":\"{\\\"files\\\":[\\\"a.cs\\\",\\\"b.cs\\\"],\\\"count\\\":2}\"}},\"timestamp\":\"2026-11-19T10:00:04.000Z\"}",
            "{\"type\":\"tool.execution_start\",\"data\":{\"toolCallId\":\"t2\",\"toolName\":\"grep\",\"arguments\":{\"pattern\":\"foo\"}},\"timestamp\":\"2026-11-19T10:00:05.000Z\"}",
            "{\"type\":\"tool.execution_complete\",\"data\":{\"toolCallId\":\"t2\",\"success\":false,\"error\":{\"message\":\"boom\"}},\"timestamp\":\"2026-11-19T10:00:06.000Z\"}",
            "{\"type\":\"tool.execution_start\",\"data\":{\"toolCallId\":\"t3\",\"toolName\":\"task\",\"arguments\":{\"name\":\"fixer\",\"agent_type\":\"general-purpose\",\"prompt\":\"Do the fix\"}},\"timestamp\":\"2026-11-19T10:00:07.000Z\"}",
            "{\"type\":\"subagent.started\",\"data\":{\"toolCallId\":\"t3\",\"agentName\":\"general-purpose\",\"agentDisplayName\":\"fixer\"},\"agentId\":\"" + Subagent + "\",\"timestamp\":\"2026-11-19T10:00:08.000Z\"}",
            "{\"type\":\"tool.execution_start\",\"data\":{\"toolCallId\":\"t4\",\"toolName\":\"view\",\"arguments\":{\"path\":\"C:\\\\repo\\\\Foo.cs\"}},\"agentId\":\"" + Subagent + "\",\"timestamp\":\"2026-11-19T10:00:09.000Z\"}",
            "{\"type\":\"tool.execution_complete\",\"data\":{\"toolCallId\":\"t4\",\"success\":true,\"result\":{\"content\":\"public class Foo { }\"}},\"agentId\":\"" + Subagent + "\",\"timestamp\":\"2026-11-19T10:00:10.000Z\"}",
        };

        [TestMethod]
        public void DetailsHighlightScriptsAndPrettyPrintJsonResults()
        {
            var model = Load();
            var tool = model.Activities.Single(a => a.ToolCallId == "t1");
            var failed = model.Activities.Single(a => a.ToolCallId == "t2");
            var view = model.Activities.Single(a => a.ToolCallId == "t4");
            var assistant = model.Activities.First(a => a.Kind == ActivityKind.Assistant);

            LocalSessionsTests.RunSta(() =>
            {
                var palette = DetailsPalette.Dark;
                var builder = new DetailsDocumentBuilder(palette, Brushes.White, new FontFamily("Segoe UI"), 12);

                var runs = Runs(builder.Build(tool));
                Assert.AreSame(palette.TokenBrush(TokenKind.Command), runs.Single(r => r.Text == "Get-ChildItem").Foreground);
                Assert.AreSame(palette.TokenBrush(TokenKind.Variable), runs.Single(r => r.Text == "$root").Foreground);
                Assert.AreSame(palette.TokenBrush(TokenKind.Property), runs.First(r => r.Text == "\"files\"").Foreground);
                Assert.IsTrue(runs.Any(r => r.Text.Contains("\"count\"")), "JSON result was not rendered");
                Assert.IsTrue(Blocks(builder.Build(tool)).OfType<Paragraph>().Count(p => p.Inlines.OfType<LineBreak>().Any() && p.Background == palette.CodeBackground) >= 1,
                    "JSON result should be pretty printed in a multi-line code block");

                var failedRuns = Runs(builder.Build(failed));
                Assert.IsTrue(failedRuns.Any(r => r.Text.Contains("boom")));
                Assert.IsTrue(Blocks(builder.Build(failed)).OfType<Paragraph>().Any(p => p.Foreground == palette.Error));

                Assert.AreSame(palette.TokenBrush(TokenKind.Keyword), Runs(builder.Build(view)).First(r => r.Text == "public").Foreground);

                var markdown = Blocks(builder.Build(assistant)).OfType<Paragraph>().ToList();
                Assert.IsTrue(markdown.Any(p => TextOf(p) == "Plan" && p.FontWeight == System.Windows.FontWeights.SemiBold),
                    "Markdown heading not rendered: " + string.Join(" | ", markdown.Select(TextOf)));
                Assert.IsTrue(markdown.Any(p => TextOf(p) == "•  build"), "Bullets should be rendered as list items: " + string.Join(" | ", markdown.Select(TextOf)));

                Assert.IsNotNull(builder.Build(null));
            });
        }

        [TestMethod]
        public void KindStatusAndAgentFiltersNarrowTheActivityList()
        {
            var root = Path.Combine(Path.GetTempPath(), "CopilotActivityViewerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "s1"));
            File.WriteAllText(Path.Combine(root, "s1", SessionCatalog.EventsFileName), string.Join("\n", SessionEvents) + "\n");
            try
            {
                LocalSessionsTests.RunSta(() =>
                {
                    var vm = new ActivityViewModel(new SessionCatalog(new[] { new KeyValuePair<string, string>("VS", root) }), startTimer: false);
                    var init = vm.InitializeAsync();
                    LocalSessionsTests.PumpUntil(() => init.IsCompleted && vm.Model != null && vm.Activities != null, TimeSpan.FromSeconds(30));
                    init.GetAwaiter().GetResult();
                    Assert.IsNotNull(vm.Activities, "Session not loaded: " + vm.StatusMessage);

                    var all = Visible(vm).Count;
                    Assert.IsFalse(vm.HasActiveFilters);
                    var toolFilter = vm.KindFilters.Single(f => f.Kind == ActivityKind.Tool);
                    Assert.AreEqual(vm.Model.Activities.Count(a => a.Kind == ActivityKind.Tool), toolFilter.Count);
                    Assert.AreEqual(0, vm.KindFilters.Single(f => f.Kind == ActivityKind.Skill).Count);

                    toolFilter.IsChecked = false;
                    Assert.IsTrue(vm.HasActiveFilters);
                    Assert.IsFalse(Visible(vm).Any(a => a.Kind == ActivityKind.Tool));
                    Assert.IsTrue(Visible(vm).Any(a => a.Kind == ActivityKind.User));

                    vm.SoloKind(toolFilter);
                    Assert.IsTrue(Visible(vm).All(a => a.Kind == ActivityKind.Tool));
                    Assert.AreEqual(toolFilter.Count, Visible(vm).Count);
                    vm.SoloKind(toolFilter);
                    Assert.AreEqual(all, Visible(vm).Count, "Solo on the only shown type shows every type again");

                    vm.SelectedStatus = vm.StatusOptions.Single(o => o.Value == StatusFilter.Failed);
                    CollectionAssert.AreEqual(new[] { "t2" }, Visible(vm).Select(a => a.ToolCallId).ToArray());
                    vm.SelectedStatus = vm.StatusOptions.Single(o => o.Value == StatusFilter.Ok);
                    Assert.IsTrue(Visible(vm).Any(a => a.ToolCallId == "t1"));
                    Assert.IsFalse(Visible(vm).Any(a => a.ToolCallId == "t2"));

                    vm.ResetFilters();
                    Assert.IsFalse(vm.HasActiveFilters);
                    Assert.AreEqual(all, Visible(vm).Count);

                    var subagent = vm.Model.FindAgent(Subagent);
                    CollectionAssert.AreEqual(new[] { vm.Model.MainAgent, subagent }, vm.AgentOptions.ToArray());
                    Assert.AreEqual(1, subagent.Depth);
                    vm.SelectedAgent = subagent;
                    Assert.IsTrue(Visible(vm).Count > 0);
                    Assert.IsTrue(Visible(vm).All(a => ReferenceEquals(a.Agent, subagent)));
                    vm.SelectedAgent = null;
                    Assert.AreSame(vm.Model.MainAgent, vm.SelectedAgent);

                    vm.SearchText = "Foo.cs";
                    Assert.IsTrue(Visible(vm).Any(a => a.ToolCallId == "t4"));
                    vm.SearchText = "a.cs";
                    Assert.IsTrue(Visible(vm).Any(a => a.ToolCallId == "t1"), "Search should look inside results");
                    vm.Dispose();
                });
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static SessionModel Load()
        {
            var model = new SessionModel();
            foreach (var line in SessionEvents)
            {
                model.Apply(SessionModel.ParseLine(line));
            }

            return model;
        }

        private static List<ActivityItem> Visible(ActivityViewModel vm) => vm.Activities.Cast<ActivityItem>().ToList();

        private static IEnumerable<Block> Blocks(FlowDocument document) => Flatten(document.Blocks);

        private static IEnumerable<Block> Flatten(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
            {
                yield return block;
                IEnumerable<Block> children =
                    block is Section section ? section.Blocks :
                    block is List list ? list.ListItems.SelectMany(i => i.Blocks) :
                    block is Table table ? table.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells).SelectMany(c => c.Blocks) :
                    Enumerable.Empty<Block>();
                foreach (var child in Flatten(children))
                {
                    yield return child;
                }
            }
        }

        private static List<Run> Runs(FlowDocument document) =>
            Blocks(document).OfType<Paragraph>().SelectMany(p => FlattenInlines(p.Inlines)).OfType<Run>().ToList();

        private static IEnumerable<Inline> FlattenInlines(IEnumerable<Inline> inlines)
        {
            foreach (var inline in inlines)
            {
                yield return inline;
                if (inline is Span span)
                {
                    foreach (var child in FlattenInlines(span.Inlines))
                    {
                        yield return child;
                    }
                }
            }
        }

        private static string TextOf(Paragraph paragraph) => string.Concat(FlattenInlines(paragraph.Inlines).OfType<Run>().Select(r => r.Text)).Trim();
    }
}
