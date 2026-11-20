using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CopilotActivityViewer.Model;
using CopilotActivityViewer.ToolWindow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CopilotActivityViewer.Tests;

/// <summary>Exercises the viewer against the Copilot sessions stored on this machine (inconclusive when there are none).</summary>
[TestClass]
public partial class LocalSessionsTests
{
    private const string ControlResource = "CopilotActivityViewer.ToolWindow.ActivityControl.xaml";

    public TestContext TestContext { get; set; }

    [TestMethod]
    public void AllLocalSessionsParseAndProduceConsistentAgentTrees()
    {
        var sessions = new SessionCatalog(SessionCatalog.CreateDefault().Roots).Discover();
        if (sessions.Count == 0)
        {
            Assert.Inconclusive("No local Copilot sessions found.");
        }

        foreach (var session in sessions)
        {
            var model = new SessionModel();
            var startedAgents = new HashSet<string>();
            foreach (var line in new EventLogReader(session.EventsPath).ReadNewLines())
            {
                var e = SessionModel.ParseLine(line);
                if (e == null)
                {
                    continue;
                }

                if ((string)e["type"] == "subagent.started" && e["agentId"] != null)
                {
                    startedAgents.Add((string)e["agentId"]);
                }

                model.Apply(e);
            }

            model.UpdateSummary();
            foreach (var id in startedAgents)
            {
                var agent = model.FindAgent(id);
                Assert.IsNotNull(agent, session.Id + ": missing agent " + id);
                Assert.IsTrue(model.MainAgent.IsSelfOrAncestorOf(agent), session.Id + ": agent not under main " + id);
            }

            Assert.IsTrue(model.Activities.All(a => a.Agent != null), session.Id + ": activity without agent");
            TestContext.WriteLine($"{session.Id}: {model.EventCount} events, {model.Activities.Count} activities, {startedAgents.Count} subagents — {model.Summary}");
        }
    }

    [TestMethod]
    public void RemoteUiTemplateRendersRealSession()
    {
        var source = new SessionCatalog(SessionCatalog.CreateDefault().Roots).Discover()
            .OrderByDescending(s => new EventLogReader(s.EventsPath).ReadNewLines().Count(l => l.Contains("\"subagent.started\"")))
            .ThenByDescending(s => s.LastWriteUtc)
            .FirstOrDefault();
        if (source == null)
        {
            Assert.Inconclusive("No local Copilot sessions found.");
        }

        var root = Path.Combine(Path.GetTempPath(), "CopilotActivityViewerTests", Guid.NewGuid().ToString("N"));
        var copy = Path.Combine(root, source.Id);
        Directory.CreateDirectory(copy);
        using (var input = new FileStream(source.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var target = File.Create(Path.Combine(copy, SessionCatalog.EventsFileName)))
        {
            input.CopyTo(target);
        }

        var workspace = Path.Combine(source.Directory, "workspace.yaml");
        if (File.Exists(workspace))
        {
            File.Copy(workspace, Path.Combine(copy, "workspace.yaml"));
        }

        var output = Path.Combine(AppContext.BaseDirectory, "CopilotActivity.png");
        try
        {
            using var vm = new ActivityViewModel(new SessionCatalog([new KeyValuePair<string, string>("VS", root)]));
            vm.InitializeAsync().GetAwaiter().GetResult();
            Assert.IsNotNull(vm.Model, "Session was not loaded: " + vm.StatusMessage);
            Assert.IsNotEmpty(vm.Activities, "No activities shown");
            Assert.IsFalse(vm.HasScope, "A catalog built from explicit roots is not scoped to an IDE instance");

            var sample = vm.Activities.LastOrDefault(a => a.ToolName == "powershell" && a.ResultText != null) ?? vm.Activities[^1];
            vm.SelectedActivity = sample;
            vm.WhenIdleAsync().GetAwaiter().GetResult();
            Assert.IsNotNull(vm.DetailsXaml, "No details for " + sample.Title);

            RunSta(() =>
            {
                // The view model mutates its collections off the UI thread (as Remote UI expects), so bind only once it is idle.
                var host = new Border { Child = LoadControl(), DataContext = vm };
                ApplyDarkTheme(host);
                host.Measure(new Size(1500, 850));
                host.Arrange(new Rect(0, 0, 1500, 850));
                host.UpdateLayout();

                var viewer = Descendants<FlowDocumentScrollViewer>(host).Single();
                viewer.Document = DetailsAndFiltersTests.Parse(vm.DetailsXaml);
                PumpUntil(() => false, TimeSpan.FromMilliseconds(300));
                host.UpdateLayout();

                var lists = Descendants<ListBox>(host).ToList();
                Assert.HasCount(2, lists, "Agents list and activity list");
                Assert.AreEqual(vm.Agents.Count, lists[0].Items.Count, "Agents list is not bound");
                Assert.AreEqual(vm.Activities.Count, lists[1].Items.Count, "Activity list is not bound");
                Assert.AreSame(sample, lists[1].SelectedItem, "Selected activity is not bound");
                Assert.IsGreaterThan(1, viewer.Document.Blocks.Count, "Details were not rendered for " + sample.Title);
                Assert.AreEqual(vm.KindFilters.Count, Descendants<Button>(host).Count(b => b.DataContext is KindFilter), "Kind chips are not rendered");

                var bitmap = new RenderTargetBitmap(1500, 850, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(host);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(output))
                {
                    encoder.Save(stream);
                }

                TestContext.WriteLine($"Rendered {source.Id}: {lists[1].Items.Count} activities, {vm.Agents.Count} agents -> {output}");
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Loads the embedded Remote UI template into plain WPF: Visual Studio resource keys become string keys supplied by
    /// <see cref="ApplyDarkTheme"/> and the Remote UI-only event handlers are dropped.
    /// </summary>
    internal static FrameworkElement LoadControl()
    {
        string xaml;
        using (var stream = typeof(ActivityViewModel).Assembly.GetManifestResourceStream(ControlResource))
        {
            Assert.IsNotNull(stream, "Embedded control XAML not found");
            using var reader = new StreamReader(stream);
            xaml = reader.ReadToEnd();
        }

        xaml = RemoteUiEventHandlers().Replace(xaml, string.Empty);
        xaml = VsBasedOn().Replace(xaml, string.Empty);
        xaml = VsStaticKey().Replace(xaml, "${key}");
        xaml = VsNamespaces().Replace(xaml, string.Empty);
        Assert.IsFalse(xaml.Contains("vs:", StringComparison.Ordinal), "Unexpected Remote UI markup left in the template");

        var template = (DataTemplate)XamlReader.Parse(xaml);
        return (FrameworkElement)template.LoadContent();
    }

    internal static void ApplyDarkTheme(FrameworkElement element)
    {
        static Color Rgb(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        var colors = new Dictionary<string, Color>
        {
            ["ToolWindowBackground"] = Rgb(0x1F1F1F),
            ["ToolWindowText"] = Rgb(0xF1F1F1),
            ["ToolWindowBorder"] = Rgb(0x3F3F46),
            ["SystemGrayText"] = Rgb(0x9A9A9A),
            ["ControlLinkText"] = Rgb(0x4FC1FF),
        };

        foreach (var (name, color) in colors)
        {
            element.Resources[name + "ColorKey"] = color;
            element.Resources[name + "BrushKey"] = new SolidColorBrush(color);
        }

        element.Resources["EnvironmentFontFamilyKey"] = new FontFamily("Segoe UI");
        element.Resources["EnvironmentFontSizeKey"] = 12.0;
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    internal static void RunSta(Action action)
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    internal static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < timeout)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
    }

    [GeneratedRegex(@"<vs:ExtensibilityUICommands\.EventHandlers>.*?</vs:ExtensibilityUICommands\.EventHandlers>", RegexOptions.Singleline)]
    private static partial Regex RemoteUiEventHandlers();

    [GeneratedRegex(@"\s+BasedOn=""\{StaticResource \{x:Static styles:VsResourceKeys\.\w+\}\}""")]
    private static partial Regex VsBasedOn();

    [GeneratedRegex(@"\{x:Static (?:colors|styles):\w+\.(?<key>\w+)\}")]
    private static partial Regex VsStaticKey();

    [GeneratedRegex(@"\s+xmlns:(?:vs|styles|colors)=""[^""]*""")]
    private static partial Regex VsNamespaces();
}
