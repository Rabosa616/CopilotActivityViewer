using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using CopilotActivityViewer.Formatting;
using CopilotActivityViewer.Model;
using CopilotActivityViewer.ToolWindow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CopilotActivityViewer.Tests;

[TestClass]
public class DetailsAndFiltersTests
{
    private const string Subagent = "aaaaaaaa-0000-0000-0000-000000000001";

    internal static readonly string[] SessionEvents =
    [
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
    ];

    [TestMethod]
    public void DetailsXamlHighlightsScriptsAndPrettyPrintsJsonResults()
    {
        var model = Load();
        var tool = model.Activities.Single(a => a.ToolCallId == "t1");
        var failed = model.Activities.Single(a => a.ToolCallId == "t2");
        var view = model.Activities.Single(a => a.ToolCallId == "t4");
        var assistant = model.Activities.First(a => a.Kind == ActivityKind.Assistant);

        LocalSessionsTests.RunSta(() =>
        {
            var toolDocument = Parse(DetailsXamlBuilder.Build(tool));
            var runs = Runs(toolDocument);
            Assert.AreEqual(ColorOf(DetailsXamlBuilder.TokenColor(TokenKind.Command)), Foreground(runs.Single(r => r.Text == "Get-ChildItem")));
            Assert.AreEqual(ColorOf(DetailsXamlBuilder.TokenColor(TokenKind.Variable)), Foreground(runs.Single(r => r.Text == "$root")));
            Assert.AreEqual(ColorOf(DetailsXamlBuilder.TokenColor(TokenKind.Property)), Foreground(runs.First(r => r.Text == "\"files\"")));
            Assert.IsTrue(runs.Any(r => r.Text.Contains("\"count\"")), "JSON result was not rendered");
            Assert.IsTrue(
                Blocks(toolDocument).OfType<Paragraph>().Any(p => p.Inlines.OfType<LineBreak>().Any() && BrushColor(p.Background) == ColorOf(DetailsXamlBuilder.CodeBackground)),
                "JSON result should be pretty printed in a multi-line code block");

            var failedDocument = Parse(DetailsXamlBuilder.Build(failed));
            Assert.IsTrue(Runs(failedDocument).Any(r => r.Text.Contains("boom")));
            Assert.IsTrue(Blocks(failedDocument).OfType<Paragraph>().Any(p => BrushColor(p.Foreground) == ColorOf(DetailsXamlBuilder.ErrorColor)));

            Assert.AreEqual(ColorOf(DetailsXamlBuilder.TokenColor(TokenKind.Keyword)), Foreground(Runs(Parse(DetailsXamlBuilder.Build(view))).First(r => r.Text == "public")));

            var markdown = Blocks(Parse(DetailsXamlBuilder.Build(assistant))).OfType<Paragraph>().ToList();
            Assert.IsTrue(markdown.Any(p => TextOf(p) == "Plan" && p.FontWeight == System.Windows.FontWeights.SemiBold),
                "Markdown heading not rendered: " + string.Join(" | ", markdown.Select(TextOf)));
            Assert.IsTrue(markdown.Any(p => TextOf(p) == "•  build"), "Bullets should be rendered as list items: " + string.Join(" | ", markdown.Select(TextOf)));

            Assert.IsTrue(Runs(Parse(DetailsXamlBuilder.Build(null))).Any(r => r.Text.Contains("Select an activity")));
        });
    }

    [TestMethod]
    public void DetailsXamlEscapesMarkupBracesAndControlCharacters()
    {
        var model = new SessionModel();
        model.ApplyLine("{\"type\":\"user.message\",\"data\":{\"content\":\"{Binding X} <Run>&amp; \\u0001 \\\"q\\\" ' \\ud83d\\ude00 end\"},\"timestamp\":\"2026-11-19T10:00:01.000Z\"}");
        model.ApplyLine("{\"type\":\"tool.execution_start\",\"data\":{\"toolCallId\":\"t1\",\"toolName\":\"powershell\",\"arguments\":{\"command\":\"Write-Host '<b>{0}</b>' & $x\"}},\"timestamp\":\"2026-11-19T10:00:02.000Z\"}");

        LocalSessionsTests.RunSta(() =>
        {
            var user = string.Concat(Runs(Parse(DetailsXamlBuilder.Build(model.Activities[0]))).Select(r => r.Text));
            StringAssert.Contains(user, "{Binding X} <Run>&amp;");
            StringAssert.Contains(user, "\"q\" ' 😀 end");
            Assert.DoesNotContain('\u0001', user);

            var command = string.Concat(Runs(Parse(DetailsXamlBuilder.Build(model.Activities[1]))).Select(r => r.Text));
            StringAssert.Contains(command, "Write-Host '<b>{0}</b>' & $x");
        });
    }

    [TestMethod]
    public async Task KindStatusAgentAndSearchFiltersNarrowTheActivityList()
    {
        using var session = TempSession.Create(SessionEvents);
        using var vm = new ActivityViewModel(session.Catalog);
        await vm.InitializeAsync();
        Assert.IsNotNull(vm.Model, "Session not loaded: " + vm.StatusMessage);

        var all = vm.Activities.Count;
        Assert.AreEqual(vm.Model.Activities.Count, all);
        Assert.AreEqual(all + " events", vm.CountText);
        Assert.IsFalse(vm.HasActiveFilters);
        var toolFilter = vm.KindFilters.Single(f => f.Kind == ActivityKind.Tool);
        Assert.AreEqual(vm.Model.Activities.Count(a => a.Kind == ActivityKind.Tool), toolFilter.Count);
        Assert.IsFalse(vm.KindFilters.Single(f => f.Kind == ActivityKind.Skill).HasItems);

        await vm.ToggleKindCommand.ExecuteAsync(toolFilter, null, CancellationToken.None);
        Assert.IsFalse(toolFilter.IsChecked);
        Assert.IsTrue(vm.HasActiveFilters);
        Assert.IsFalse(vm.Activities.Any(a => a.Kind == ActivityKind.Tool));
        Assert.IsTrue(vm.Activities.Any(a => a.Kind == ActivityKind.User));
        Assert.AreEqual(vm.Activities.Count + " of " + all + " events", vm.CountText);

        await vm.SoloKindCommand.ExecuteAsync("TOOL", null, CancellationToken.None);
        Assert.IsTrue(vm.Activities.All(a => a.Kind == ActivityKind.Tool));
        Assert.HasCount(toolFilter.Count, vm.Activities);
        await vm.SoloKindCommand.ExecuteAsync(toolFilter, null, CancellationToken.None);
        Assert.HasCount(all, vm.Activities, "Solo on the only shown type shows every type again");

        vm.SelectedStatus = vm.StatusOptions.Single(o => o.Value == StatusFilter.Failed);
        await vm.WhenIdleAsync();
        CollectionAssert.AreEqual(new[] { "t2" }, vm.Activities.Select(a => a.ToolCallId).ToArray());
        vm.SelectedStatus = vm.StatusOptions.Single(o => o.Value == StatusFilter.Ok);
        await vm.WhenIdleAsync();
        Assert.IsTrue(vm.Activities.Any(a => a.ToolCallId == "t1"));
        Assert.IsFalse(vm.Activities.Any(a => a.ToolCallId == "t2"));

        await vm.ClearFiltersCommand.ExecuteAsync(null, null, CancellationToken.None);
        Assert.IsFalse(vm.HasActiveFilters);
        Assert.HasCount(all, vm.Activities);

        var subagent = vm.Model.FindAgent(Subagent);
        CollectionAssert.AreEqual(new[] { vm.Model.MainAgent, subagent }, vm.Agents.ToArray());
        Assert.AreEqual("14,0,0,0", subagent.IndentMargin);
        vm.SelectedAgent = subagent;
        await vm.WhenIdleAsync();
        Assert.IsNotEmpty(vm.Activities);
        Assert.IsTrue(vm.Activities.All(a => ReferenceEquals(a.Agent, subagent)));
        vm.SelectedAgent = null;
        await vm.WhenIdleAsync();
        Assert.AreSame(vm.Model.MainAgent, vm.SelectedAgent);

        vm.SearchText = "Foo.cs";
        await vm.WhenIdleAsync();
        Assert.IsTrue(vm.Activities.Any(a => a.ToolCallId == "t4"));
        vm.SearchText = "a.cs";
        await vm.WhenIdleAsync();
        Assert.IsTrue(vm.Activities.Any(a => a.ToolCallId == "t1"), "Search should look inside results");
    }

    [TestMethod]
    public async Task NewEventsAreMergedNewestFirstAndDetailsFollowTheSelection()
    {
        using var session = TempSession.Create(SessionEvents.Take(3));
        using var vm = new ActivityViewModel(session.Catalog);
        await vm.InitializeAsync();

        Assert.IsTrue(vm.NewestFirst);
        Assert.AreEqual("t1", vm.Activities[0].ToolCallId, "Newest activity first");
        var running = vm.Activities[0];
        Assert.IsTrue(running.IsActive);

        vm.SelectedActivity = running;
        await vm.WhenIdleAsync();
        Assert.IsTrue(vm.HasSelectedActivity);
        StringAssert.Contains(vm.DetailsXaml, "Get-ChildItem");
        StringAssert.Contains(vm.DetailsXaml, "Running…");

        var before = vm.Activities.ToList();
        session.Append(SessionEvents.Skip(3).Take(3));
        await vm.PollAsync();

        Assert.AreEqual("t2", vm.Activities[0].ToolCallId);
        CollectionAssert.AreEqual(before, vm.Activities.Skip(1).ToArray(), "Existing rows must be kept (incremental update)");
        Assert.AreEqual(ActivityStatus.Succeeded, running.Status);
        Assert.AreSame(running, vm.SelectedActivity);
        StringAssert.Contains(vm.DetailsXaml, "a.cs", "Details refresh when the selected tool completes");

        vm.NewestFirst = false;
        await vm.WhenIdleAsync();
        Assert.AreEqual(ActivityKind.User, vm.Activities[0].Kind);

        string copied = null;
        vm.ClipboardWriter = text => { copied = text; return true; };
        await vm.CopyDetailsCommand.ExecuteAsync(null, null, CancellationToken.None);
        StringAssert.Contains(copied, "Get-ChildItem");

        await vm.ToggleKindCommand.ExecuteAsync("TOOL", null, CancellationToken.None);
        Assert.IsNull(vm.SelectedActivity, "Selection is cleared when the item is filtered out");
        Assert.IsFalse(vm.HasSelectedActivity);
        StringAssert.Contains(vm.DetailsXaml, "Select an activity");
    }

    [TestMethod]
    public async Task LongSessionsListOnlyTheLatestActivityUntilOlderIsRequested()
    {
        const int tools = ActivityViewModel.PageSize * 2;
        static string Tool(int i) =>
            "{\"type\":\"tool.execution_start\",\"data\":{\"toolCallId\":\"c" + i + "\",\"toolName\":\"view\",\"arguments\":{\"path\":\"File" + i + ".cs\"}},\"timestamp\":\"2026-11-19T10:00:00.000Z\"}";

        using var session = TempSession.Create(new[] { SessionEvents[0] }.Concat(Enumerable.Range(1, tools).Select(Tool)));
        using var vm = new ActivityViewModel(session.Catalog);
        await vm.InitializeAsync();

        var total = tools + 1;
        Assert.HasCount(ActivityViewModel.PageSize, vm.Activities);
        Assert.AreEqual("c" + tools, vm.Activities[0].ToolCallId, "The most recent activity is listed first");
        Assert.AreEqual(total - ActivityViewModel.PageSize, vm.HiddenCount);
        Assert.IsTrue(vm.HasHiddenActivities);
        Assert.AreEqual(total + " events · latest " + ActivityViewModel.PageSize + " shown", vm.CountText);

        vm.NewestFirst = false;
        await vm.WhenIdleAsync();
        Assert.AreEqual("c" + tools, vm.Activities[^1].ToolCallId, "Oldest first still keeps the most recent activity");

        vm.SearchText = "File1.cs";
        await vm.WhenIdleAsync();
        CollectionAssert.AreEqual(new[] { "c1" }, vm.Activities.Select(a => a.ToolCallId).ToArray(), "Filters cover the whole session");
        Assert.IsFalse(vm.HasHiddenActivities);

        vm.SearchText = string.Empty;
        await vm.WhenIdleAsync();
        var before = vm.Activities.ToList();
        await vm.ShowOlderCommand.ExecuteAsync(null, null, CancellationToken.None);
        Assert.HasCount(total, vm.Activities);
        Assert.IsFalse(vm.HasHiddenActivities);
        CollectionAssert.AreEqual(before, vm.Activities.Skip(total - before.Count).ToArray(), "Older rows are added without replacing the list");
        Assert.AreEqual(ActivityKind.User, vm.Activities[0].Kind);
        Assert.AreEqual(total + " events", vm.CountText);

        session.Append([Tool(tools + 1)]);
        await vm.PollAsync();
        Assert.HasCount(total + 1, vm.Activities, "Showing older activity survives live updates");
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

    internal static FlowDocument Parse(string xaml)
    {
        var document = (FlowDocument)XamlReader.Parse(xaml);
        document.Resources[DetailsXamlBuilder.Keys.Text] = Brushes.White;
        document.Resources[DetailsXamlBuilder.Keys.Muted] = Brushes.Gray;
        document.Resources[DetailsXamlBuilder.Keys.Border] = Brushes.DimGray;
        document.Resources[DetailsXamlBuilder.Keys.Accent] = Brushes.SkyBlue;
        return document;
    }

    private static Color ColorOf(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Color? BrushColor(Brush brush) => (brush as SolidColorBrush)?.Color;

    private static Color? Foreground(Run run) => BrushColor(run.Foreground);

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
                [];
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

/// <summary>A session folder with an events.jsonl under a temporary catalog root.</summary>
internal sealed class TempSession : IDisposable
{
    private TempSession(string root, string eventsPath)
    {
        Root = root;
        EventsPath = eventsPath;
        Catalog = new SessionCatalog([new KeyValuePair<string, string>("VS", root)]);
    }

    public string Root { get; }

    public string EventsPath { get; }

    public SessionCatalog Catalog { get; }

    public static TempSession Create(IEnumerable<string> lines, string id = "s1")
    {
        var root = Path.Combine(Path.GetTempPath(), "CopilotActivityViewerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, id));
        var path = Path.Combine(root, id, SessionCatalog.EventsFileName);
        File.WriteAllText(path, string.Concat(lines.Select(l => l + "\n")));
        return new TempSession(root, path);
    }

    public void Append(IEnumerable<string> lines) => File.AppendAllText(EventsPath, string.Concat(lines.Select(l => l + "\n")));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
