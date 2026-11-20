using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CopilotActivityViewer.Model;
using CopilotActivityViewer.ToolWindow;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CopilotActivityViewer.Tests
{
    /// <summary>Exercises the viewer against the Copilot sessions stored on this machine (skipped when there are none).</summary>
    [TestClass]
    public class LocalSessionsTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public void AllLocalSessionsParseAndProduceConsistentAgentTrees()
        {
            var sessions = SessionCatalog.CreateDefault().Discover();
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
        public void ToolWindowRendersRealSession()
        {
            var source = SessionCatalog.CreateDefault().Discover()
                .OrderByDescending(s => new EventLogReader(s.EventsPath).ReadNewLines().Count(l => l.Contains("\"subagent.started\"")))
                .ThenByDescending(s => s.LastWriteUtc)
                .FirstOrDefault();
            if (source == null)
            {
                Assert.Inconclusive("No local Copilot sessions found.");
            }

            var root = Path.Combine(Path.GetTempPath(), "CopilotActivityViewerRender", Guid.NewGuid().ToString("N"));
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

            var output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CopilotActivity.png");
            try
            {
                RunSta(() =>
                {
                    var catalog = new SessionCatalog(new[] { new KeyValuePair<string, string>("VS", root) });
                    var control = new ActivityToolWindowControl(new ActivityViewModel(catalog, startTimer: false));
                    ApplyDarkTheme(control);

                    var init = control.ViewModel.InitializeAsync();
                    PumpUntil(() => init.IsCompleted && control.ViewModel.Model != null && !control.ViewModel.HasStatusMessage, TimeSpan.FromSeconds(60));
                    init.GetAwaiter().GetResult();

                    var model = control.ViewModel.Model;
                    Assert.IsNotNull(model, "Session was not loaded: " + control.ViewModel.StatusMessage);
                    Assert.IsTrue(control.ViewModel.Activities.Count > 0, "No activities shown");

                    var sample = control.ViewModel.Activities.Cast<ActivityItem>().LastOrDefault(a => a.ToolName == "powershell" && a.ResultText != null)
                        ?? control.ViewModel.Activities.Cast<ActivityItem>().Last();
                    control.ViewModel.SelectedActivity = sample;

                    control.Measure(new Size(1500, 850));
                    control.Arrange(new Rect(0, 0, 1500, 850));
                    control.UpdateLayout();
                    PumpUntil(() => false, TimeSpan.FromMilliseconds(500));
                    control.UpdateLayout();

                    var details = (System.Windows.Controls.FlowDocumentScrollViewer)control.FindName("DetailsViewer");
                    Assert.IsTrue(details.Document.Blocks.Count > 1, "Details were not rendered for " + sample.Title);

                    var list = (System.Windows.Controls.ListBox)control.FindName("ActivityList");
                    var tree = (System.Windows.Controls.TreeView)control.FindName("AgentTree");
                    Assert.IsTrue(list.Items.Count > 0, "Activity list is empty");
                    Assert.AreEqual(1, tree.Items.Count, "Agent tree must have the main agent as single root");

                    var bitmap = new RenderTargetBitmap(1500, 850, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(control);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(output))
                    {
                        encoder.Save(stream);
                    }

                    TestContext.WriteLine($"Rendered {source.Id}: {list.Items.Count} activities, {model.Agents.Count()} agents -> {output}");
                    control.Dispose();
                });
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        internal static void ApplyDarkTheme(FrameworkElement element)
        {
            Brush Brush(uint rgb) => new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            element.Resources[EnvironmentColors.ToolWindowBackgroundBrushKey] = Brush(0x1F1F1F);
            element.Resources[EnvironmentColors.ToolWindowTextBrushKey] = Brush(0xF1F1F1);
            element.Resources[EnvironmentColors.ToolWindowBorderBrushKey] = Brush(0x3F3F46);
            element.Resources[EnvironmentColors.SystemGrayTextBrushKey] = Brush(0x9A9A9A);
            element.Resources[VsFonts.EnvironmentFontFamilyKey] = new FontFamily("Segoe UI");
            element.Resources[VsFonts.EnvironmentFontSizeKey] = 12.0;
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
    }
}
