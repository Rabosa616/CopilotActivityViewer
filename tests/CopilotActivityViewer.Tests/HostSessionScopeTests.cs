using CopilotActivityViewer.Model;
using CopilotActivityViewer.ToolWindow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CopilotActivityViewer.Tests;

[TestClass]
public class HostSessionScopeTests
{
    private const string SessionA = "aaaaaaaa-1111-4111-8111-111111111111";
    private const string SessionB = "bbbbbbbb-2222-4222-8222-222222222222";
    private const string SessionC = "cccccccc-3333-4333-8333-333333333333";

    private string tempRoot;

    [TestInitialize]
    public void Setup()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "CopilotActivityViewerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(tempRoot, recursive: true);
    }

    [TestMethod]
    public void DetectsTheIdeAncestorAndOnlyItsCopilotSessions()
    {
        var logsA = WriteLog("A", "process-1-20.log", Create(SessionA), Delivery(SessionA));
        var logsB = WriteLog("B", "process-1-21.log", Create(SessionB), Delivery(SessionB));
        IReadOnlyList<ProcessEntry> processes =
        [
            new(5, 1, "devenv.exe"),
            new(9, 5, "DevHub.exe"),
            new(10, 9, "dotnet.exe"),
            new(20, 5, "copilot.exe"),
            new(6, 1, "devenv.exe"),
            new(21, 6, "copilot.exe"),
        ];
        var logDirs = new Dictionary<int, string> { [20] = logsA, [21] = logsB };

        var scope = HostSessionScope.Detect(10, () => processes, pid => logDirs.GetValueOrDefault(pid));

        Assert.IsNotNull(scope);
        Assert.AreEqual(5, scope.HostProcessId);
        Assert.AreEqual("Visual Studio (PID 5)", scope.Description);
        CollectionAssert.AreEquivalent(new[] { SessionA }, scope.Refresh().ToArray());
        Assert.IsTrue(scope.CopilotRunning);
    }

    [TestMethod]
    public void DetectsSsmsAndReturnsNullOutsideAnIde()
    {
        IReadOnlyList<ProcessEntry> processes = [new(3, 1, "Ssms.exe"), new(4, 3, "ServiceHub.Host.Extensibility.exe"), new(8, 2, "dotnet.exe")];

        Assert.AreEqual("SSMS", HostSessionScope.Detect(4, () => processes, _ => null).HostName);
        Assert.IsNull(HostSessionScope.Detect(8, () => processes, _ => null));
        Assert.IsNull(HostSessionScope.Detect(3, () => processes, _ => null), "The IDE itself is not hosted by an IDE");
    }

    [TestMethod]
    public void CopilotNotStartedYieldsNoSessionsAndUnknownLogsYieldNull()
    {
        IReadOnlyList<ProcessEntry> processes = [new(5, 1, "devenv.exe"), new(9, 5, "DevHub.exe")];
        var scope = HostSessionScope.Detect(9, () => processes, _ => null);
        Assert.IsEmpty(scope.Refresh());
        Assert.IsFalse(scope.CopilotRunning);

        processes = [.. processes, new(30, 5, "conhost.exe"), new(31, 30, "copilot.exe")];
        Assert.IsNull(scope.Refresh(), "Copilot runs (as a grandchild) but its logs are unknown: do not filter");
        Assert.IsTrue(scope.CopilotRunning);
    }

    [TestMethod]
    public void IdeStyleCopilotThatBelongsToNoIdeDisablesFilteringOnlyWhileThisIdeHasNone()
    {
        var logs = WriteLog("A", "process-1-20.log", Create(SessionA));
        var orphanLogs = WriteLog("orphan", "process-1-41.log", Create(SessionB));
        IReadOnlyList<ProcessEntry> processes =
        [
            new(5, 1, "devenv.exe"), new(9, 5, "DevHub.exe"),
            new(6, 1, "devenv.exe"), new(21, 6, "copilot.exe"), new(22, 21, "copilot.exe"),
            new(30, 1, "github.exe"), new(40, 30, "copilot.exe"),
        ];
        var logDirs = new Dictionary<int, string> { [20] = logs, [41] = orphanLogs };
        CollectionAssert.AreEquivalent(new[] { 40 }, HostSessionScope.UnattributedCopilots(processes), "Copilot processes under an IDE (or under another Copilot) are attributed");

        var scope = HostSessionScope.Detect(9, () => processes, pid => logDirs.GetValueOrDefault(pid));
        Assert.IsEmpty(scope.Refresh(), "Neither another IDE's Copilot nor another tool's copilot.exe counts");

        processes = [.. processes, new(41, 77, "copilot.exe")];
        Assert.IsNull(scope.Refresh(), "An IDE-style Copilot started by an unknown parent may serve this IDE: do not filter");

        processes = [.. processes, new(20, 5, "copilot.exe")];
        CollectionAssert.AreEquivalent(new[] { SessionA }, scope.Refresh().ToArray(), "Once this IDE's own Copilot is known, filter again");
    }

    [TestMethod]
    public void IdeStyleCopilotProcessesOnThisMachineAreAttributedToAnIde()
    {
        var processes = NativeProcesses.Snapshot();
        var ideStyle = processes
            .Where(p => string.Equals(p.Name, "copilot.exe", StringComparison.OrdinalIgnoreCase))
            .Where(p => HostSessionScope.ParseLogDirectory(NativeProcesses.TryGetCommandLine(p.Id)) != null)
            .Select(p => p.Id)
            .ToList();
        if (ideStyle.Count == 0)
        {
            Assert.Inconclusive("No IDE is running Copilot.");
        }

        CollectionAssert.AreEqual(Array.Empty<int>(), HostSessionScope.UnattributedCopilots(processes).Intersect(ideStyle).ToArray());
    }

    [TestMethod]
    public void ScannerReadsIncrementallyAndSurvivesRotation()
    {
        var directory = WriteLog("rot", "process-1-20.log", "2026-11-20T07:00:00.000Z [INFO] start", Create(SessionA));
        var log = Path.Combine(directory, "process-1-20.log");
        var scanner = new CopilotLogScanner(directory);
        var ids = new HashSet<string>();

        scanner.Scan(ids);
        CollectionAssert.AreEquivalent(new[] { SessionA }, ids.ToArray());

        var line = Delivery(SessionB);
        File.AppendAllText(log, line[..40]);
        scanner.Scan(ids);
        File.AppendAllText(log, line[40..] + "\n");
        scanner.Scan(ids);
        CollectionAssert.AreEquivalent(new[] { SessionA, SessionB }, ids.ToArray(), "A line split across two scans is read once complete");

        File.Move(log, Path.Combine(directory, "process-1-20.1.log"));
        File.WriteAllText(log, "2026-11-20T08:00:00.000Z [INFO] rotated\n" + Delivery(SessionC) + "\n");
        ids.Clear();
        scanner.Scan(ids);
        CollectionAssert.AreEquivalent(new[] { SessionC }, ids.ToArray(), "The rotated file was already read; only the new file is scanned");

        ids.Clear();
        File.AppendAllText(log, "{\"sessionId\":\"" + SessionA + "\"} unrelated mention\n");
        scanner.Scan(ids);
        Assert.IsEmpty(ids, "Only session create/resume requests and event deliveries count");
    }

    [TestMethod]
    public void ParsesTheLogDirectoryFromTheCopilotCommandLine()
    {
        Assert.AreEqual(
            @"C:\Users\me\AppData\Local\Temp\VSGitHubCopilotLogs\4278\cli_logs",
            HostSessionScope.ParseLogDirectory("\"C:\\Program Files\\VS\\copilot.exe\" --no-auto-update --log-dir C:\\Users\\me\\AppData\\Local\\Temp\\VSGitHubCopilotLogs\\4278\\cli_logs --headless"));
        Assert.AreEqual(@"C:\Log Dir\x", HostSessionScope.ParseLogDirectory("copilot.exe --log-dir \"C:\\Log Dir\\x\" --stdio"));
        Assert.AreEqual(@"C:\x", HostSessionScope.ParseLogDirectory("copilot.exe --log-dir=C:\\x"));
        Assert.IsNull(HostSessionScope.ParseLogDirectory("copilot.exe --stdio"));
        Assert.IsNull(HostSessionScope.ParseLogDirectory(null));
    }

    [TestMethod]
    public void NativeProcessQueriesSeeTheCurrentProcess()
    {
        var processes = NativeProcesses.Snapshot();
        var self = processes.Single(p => p.Id == Environment.ProcessId);
        Assert.AreEqual(Path.GetFileName(Environment.ProcessPath), self.Name, ignoreCase: true);
        Assert.AreNotEqual(0, self.ParentId);
        StringAssert.Contains(NativeProcesses.TryGetCommandLine(Environment.ProcessId), Path.GetFileNameWithoutExtension(Environment.ProcessPath));
    }

    [TestMethod]
    public async Task ViewerShowsOnlyThisInstanceSessionsUnlessAllInstancesIsTicked()
    {
        var sessions = Path.Combine(tempRoot, "session-state");
        foreach (var id in new[] { SessionA, SessionB })
        {
            Directory.CreateDirectory(Path.Combine(sessions, id));
            File.WriteAllText(Path.Combine(sessions, id, SessionCatalog.EventsFileName), DetailsAndFiltersTests.SessionEvents[0] + "\n");
        }

        File.SetLastWriteTimeUtc(Path.Combine(sessions, SessionA, SessionCatalog.EventsFileName), DateTime.UtcNow.AddMinutes(-5));
        var logs = WriteLog("A", "process-1-20.log", Create(SessionA));
        IReadOnlyList<ProcessEntry> processes = [new(5, 1, "devenv.exe"), new(9, 5, "DevHub.exe"), new(20, 5, "copilot.exe")];
        var catalog = new SessionCatalog([new KeyValuePair<string, string>("VS", sessions)])
        {
            Scope = HostSessionScope.Detect(9, () => processes, pid => pid == 20 ? logs : null),
        };

        using var vm = new ActivityViewModel(catalog);
        await vm.InitializeAsync();
        Assert.IsTrue(vm.HasScope);
        CollectionAssert.AreEqual(new[] { SessionA }, vm.Sessions.Select(s => s.Id).ToArray());
        Assert.AreEqual(SessionA, vm.SelectedSession.Id);
        StringAssert.Contains(vm.ScopeText, "Visual Studio (PID 5)");

        vm.AllInstances = true;
        await vm.WhenIdleAsync();
        CollectionAssert.AreEqual(new[] { SessionB, SessionA }, vm.Sessions.Select(s => s.Id).ToArray());
        await vm.ScanSessionsAsync();
        Assert.AreEqual(SessionB, vm.SelectedSession.Id, "Follow latest moves to the newest session of any instance");

        vm.AllInstances = false;
        await vm.WhenIdleAsync();
        CollectionAssert.AreEqual(new[] { SessionA }, vm.Sessions.Select(s => s.Id).ToArray());
        Assert.AreEqual(SessionA, vm.SelectedSession.Id, "A session of another instance is never kept selected");
    }

    private string WriteLog(string name, string file, params string[] lines)
    {
        var directory = Path.Combine(tempRoot, name, "cli_logs");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, file), string.Concat(lines.Select(l => l + "\n")));
        return directory;
    }

    private static string Create(string id) =>
        "2026-11-20T07:38:01.707Z [DEBUG] Received session.create request: {\"sessionId\":\"" + id + "\",\"clientName\":\"microsoft/visualstudio-chat\"}";

    private static string Delivery(string id) =>
        "2026-11-20T07:38:01.645Z [DEBUG] [rust:events_engine::host_delivery] Detached host delivery started {\"delivery_session_id\":\"" + id + "\",\"queue_id\":1}";
}
