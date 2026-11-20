using System.Text.RegularExpressions;

namespace CopilotActivityViewer.Model;

/// <summary>
/// Identifies the Copilot sessions that belong to the IDE instance (Visual Studio or SQL Server Management Studio) hosting
/// this extension. Every IDE starts its own <c>copilot.exe</c> with a private <c>--log-dir</c>; its logs name the sessions it serves.
/// All instances share the same session-state folder, so this is what keeps two open IDEs from mixing their sessions.
/// </summary>
public sealed partial class HostSessionScope
{
    private const string CopilotProcessName = "copilot.exe";

    private readonly Func<IReadOnlyList<ProcessEntry>> snapshot;
    private readonly Func<int, string> logDirectoryOf;
    private readonly Dictionary<int, string> logDirectories = [];
    private readonly Dictionary<int, string> unattributedLogDirectories = [];
    private readonly Dictionary<string, CopilotLogScanner> scanners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> sessionIds = new(StringComparer.OrdinalIgnoreCase);

    internal HostSessionScope(int hostProcessId, string hostName, Func<IReadOnlyList<ProcessEntry>> snapshot, Func<int, string> logDirectoryOf)
    {
        HostProcessId = hostProcessId;
        HostName = hostName;
        this.snapshot = snapshot;
        this.logDirectoryOf = logDirectoryOf;
    }

    public int HostProcessId { get; }

    /// <summary>"Visual Studio" or "SSMS".</summary>
    public string HostName { get; }

    /// <summary>Whether a Copilot process was found under the host in the last <see cref="Refresh"/>.</summary>
    public bool CopilotRunning { get; private set; }

    public string Description => HostName + " (PID " + HostProcessId + ")";

    /// <summary>Finds the IDE process among the ancestors of the current process (the extension host is started by the IDE).</summary>
    public static HostSessionScope Detect()
    {
        try
        {
            return Detect(Environment.ProcessId, NativeProcesses.Snapshot, DefaultLogDirectory);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    internal static HostSessionScope Detect(int selfId, Func<IReadOnlyList<ProcessEntry>> snapshot, Func<int, string> logDirectoryOf)
    {
        var byId = Index(snapshot());
        var visited = new HashSet<int>();
        var current = selfId;
        while (byId.TryGetValue(current, out var entry) && visited.Add(current))
        {
            var hostName = HostNameOf(entry.Name);
            if (hostName != null && current != selfId)
            {
                return new HostSessionScope(entry.Id, hostName, snapshot, logDirectoryOf);
            }

            current = entry.ParentId;
        }

        return null;
    }

    /// <summary>
    /// Returns the ids of the sessions served so far by the Copilot processes of this IDE instance, or <c>null</c> when they
    /// cannot be determined, in which case callers should not filter: Copilot runs but its logs could not be found, or this
    /// IDE has no Copilot process of its own while an IDE-style Copilot process cannot be attributed to any IDE.
    /// </summary>
    public IReadOnlyCollection<string> Refresh()
    {
        var processes = snapshot();
        var copilots = FindCopilotProcesses(processes, HostProcessId);
        CopilotRunning = copilots.Count > 0;
        foreach (var pid in copilots)
        {
            if (!logDirectories.TryGetValue(pid, out var directory) || directory == null)
            {
                directory = logDirectoryOf(pid);
                logDirectories[pid] = directory;
            }

            if (directory != null && !scanners.ContainsKey(directory))
            {
                scanners[directory] = new CopilotLogScanner(directory);
            }
        }

        foreach (var scanner in scanners.Values)
        {
            scanner.Scan(sessionIds);
        }

        if (CopilotRunning && scanners.Count == 0)
        {
            return null;
        }

        if (!CopilotRunning && scanners.Count == 0 && UnattributedCopilots(processes).Any(HasIdeStyleLogs))
        {
            return null;
        }

        return sessionIds.ToList();
    }

    /// <summary>Copilot started like an IDE starts it (with a private log directory); other tools also ship a copilot.exe.</summary>
    private bool HasIdeStyleLogs(int copilotProcessId)
    {
        if (!unattributedLogDirectories.TryGetValue(copilotProcessId, out var directory))
        {
            directory = logDirectoryOf(copilotProcessId);
            unattributedLogDirectories[copilotProcessId] = directory;
        }

        return directory != null;
    }

    /// <summary>Copilot processes without a Visual Studio / SSMS ancestor (an unknown launch model, e.g. through a broker, or another tool).</summary>
    internal static List<int> UnattributedCopilots(IReadOnlyList<ProcessEntry> processes)
    {
        var byId = Index(processes);
        var result = new List<int>();
        foreach (var copilot in processes.Where(p => string.Equals(p.Name, CopilotProcessName, StringComparison.OrdinalIgnoreCase)))
        {
            var attributed = false;
            var visited = new HashSet<int>();
            var current = copilot.ParentId;
            while (!attributed && byId.TryGetValue(current, out var ancestor) && visited.Add(current))
            {
                attributed = HostNameOf(ancestor.Name) != null || string.Equals(ancestor.Name, CopilotProcessName, StringComparison.OrdinalIgnoreCase);
                current = ancestor.ParentId;
            }

            if (!attributed)
            {
                result.Add(copilot.Id);
            }
        }

        return result;
    }

    internal static List<int> FindCopilotProcesses(IReadOnlyList<ProcessEntry> processes, int hostId)
    {
        var children = processes.Where(p => p.Id != p.ParentId).ToLookup(p => p.ParentId);
        var result = new List<int>();
        var visited = new HashSet<int> { hostId };
        var pending = new Queue<int>();
        pending.Enqueue(hostId);
        while (pending.Count > 0)
        {
            foreach (var child in children[pending.Dequeue()])
            {
                if (!visited.Add(child.Id))
                {
                    continue;
                }

                if (string.Equals(child.Name, CopilotProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(child.Id);
                }

                pending.Enqueue(child.Id);
            }
        }

        return result;
    }

    internal static string HostNameOf(string processName)
    {
        if (string.Equals(processName, "devenv.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "Visual Studio";
        }

        return string.Equals(processName, "ssms.exe", StringComparison.OrdinalIgnoreCase) ? "SSMS" : null;
    }

    internal static string ParseLogDirectory(string commandLine)
    {
        if (string.IsNullOrEmpty(commandLine))
        {
            return null;
        }

        var match = LogDirArgument().Match(commandLine);
        return match.Success ? (match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["plain"].Value) : null;
    }

    private static string DefaultLogDirectory(int copilotProcessId)
    {
        var fromCommandLine = ParseLogDirectory(NativeProcesses.TryGetCommandLine(copilotProcessId));
        if (fromCommandLine != null)
        {
            return fromCommandLine;
        }

        // Fallback: the CLI names its log files process-<timestamp>-<pid>.log under %TEMP%\<IDE>GitHubCopilotLogs\<id>\cli_logs.
        try
        {
            foreach (var logs in Directory.EnumerateDirectories(Path.GetTempPath(), "*Copilot*"))
            {
                foreach (var instance in Directory.EnumerateDirectories(logs))
                {
                    var candidate = Path.Combine(instance, "cli_logs");
                    if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "process-*-" + copilotProcessId + "*.log").Any())
                    {
                        return candidate;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static Dictionary<int, ProcessEntry> Index(IReadOnlyList<ProcessEntry> processes)
    {
        var byId = new Dictionary<int, ProcessEntry>();
        foreach (var process in processes)
        {
            byId.TryAdd(process.Id, process);
        }

        return byId;
    }

    [GeneratedRegex(@"--log-dir(?:\s+|=)(?:""(?<quoted>[^""]+)""|(?<plain>\S+))")]
    private static partial Regex LogDirArgument();
}
