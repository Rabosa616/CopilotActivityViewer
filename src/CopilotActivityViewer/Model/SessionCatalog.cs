using System.Runtime.Serialization;
using System.Text.Json;

namespace CopilotActivityViewer.Model;

[DataContract]
public sealed class SessionInfo : ObservableObject
{
    private DateTime lastWriteUtc;
    private string title;

    public SessionInfo(string id, string directory, string source)
    {
        Id = id;
        Directory = directory;
        Source = source;
    }

    public string Id { get; }

    public string Directory { get; }

    /// <summary>Where the session comes from: VS, SSMS, CLI…</summary>
    public string Source { get; }

    [DataMember]
    public string EventsPath => Path.Combine(Directory, SessionCatalog.EventsFileName);

    public string Cwd { get; internal set; }

    public string Title
    {
        get => title;
        internal set { if (Set(ref title, value)) { Raise(nameof(Display)); } }
    }

    public DateTime LastWriteUtc
    {
        get => lastWriteUtc;
        internal set { if (Set(ref lastWriteUtc, value)) { Raise(nameof(Display)); } }
    }

    [DataMember]
    public string Display
    {
        get
        {
            var folder = string.IsNullOrEmpty(Cwd) ? null : Path.GetFileName(Cwd.TrimEnd('\\', '/'));
            var name = string.IsNullOrWhiteSpace(Title) ? Id : Title;
            var source = string.IsNullOrEmpty(Source) ? string.Empty : Source + " · ";
            return LastWriteUtc.ToLocalTime().ToString("dd/MM HH:mm") + "  " + source + (folder != null ? "[" + folder + "] " : string.Empty) + name;
        }
    }

    public override string ToString() => Display;
}

/// <summary>Discovers Copilot agent sessions stored on disk (Visual Studio, SQL Server Management Studio and Copilot CLI).</summary>
public sealed class SessionCatalog
{
    public const string EventsFileName = "events.jsonl";
    private const string SessionStateRelativePath = @"CopilotCli\session-state";
    private const int MaxSessions = 100;

    private readonly Func<IEnumerable<KeyValuePair<string, string>>> rootsProvider;

    public SessionCatalog(IEnumerable<KeyValuePair<string, string>> roots)
    {
        var fixedRoots = roots.ToList();
        rootsProvider = () => fixedRoots;
    }

    public SessionCatalog(Func<IEnumerable<KeyValuePair<string, string>>> rootsProvider)
    {
        this.rootsProvider = rootsProvider ?? throw new ArgumentNullException(nameof(rootsProvider));
    }

    /// <summary>Root folders as (source label, path) pairs, evaluated on every scan so roots created later are picked up.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Roots => rootsProvider().ToList();

    /// <summary>The IDE instance hosting the viewer; <c>null</c> when not running inside Visual Studio or SSMS.</summary>
    public HostSessionScope Scope { get; init; }

    /// <summary>
    /// Every IDE that hosts the Copilot agent stores its sessions in <c>%LOCALAPPDATA%\Microsoft\&lt;app&gt;\CopilotCli\session-state</c>
    /// (<c>VisualStudio</c> for Visual Studio, <c>SSMS</c> for SQL Server Management Studio…); the Copilot CLI uses <c>~\.copilot\session-state</c>.
    /// </summary>
    public static SessionCatalog CreateDefault()
    {
        var localMicrosoft = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft");
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new SessionCatalog(() => DefaultRoots(localMicrosoft, userProfile)) { Scope = HostSessionScope.Detect() };
    }

    internal static IEnumerable<KeyValuePair<string, string>> DefaultRoots(string localMicrosoft, string userProfile)
    {
        var roots = new List<KeyValuePair<string, string>>
        {
            new("VS", Path.Combine(localMicrosoft, "VisualStudio", SessionStateRelativePath)),
            new("SSMS", Path.Combine(localMicrosoft, "SSMS", SessionStateRelativePath)),
        };

        try
        {
            foreach (var app in Directory.EnumerateDirectories(localMicrosoft))
            {
                foreach (var candidate in new[] { app }.Concat(SafeSubdirectories(app)))
                {
                    var path = Path.Combine(candidate, SessionStateRelativePath);
                    if (!roots.Any(r => string.Equals(r.Value, path, StringComparison.OrdinalIgnoreCase)) && Directory.Exists(path))
                    {
                        roots.Add(new(SourceLabel(Path.GetRelativePath(localMicrosoft, candidate)), path));
                    }
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        roots.Add(new("CLI", Path.Combine(userProfile, ".copilot", "session-state")));
        return roots;
    }

    internal static string SourceLabel(string appFolder)
    {
        var app = appFolder.Split('\\', '/')[0];
        if (app.Equals("VisualStudio", StringComparison.OrdinalIgnoreCase))
        {
            return "VS";
        }

        return app.Contains("SSMS", StringComparison.OrdinalIgnoreCase) || app.Contains("Management Studio", StringComparison.OrdinalIgnoreCase) ? "SSMS" : app;
    }

    /// <param name="include">Optional filter on the session id (the folder name).</param>
    public List<SessionInfo> Discover(Func<string, bool> include = null)
    {
        var result = new List<SessionInfo>();
        foreach (var root in Roots)
        {
            if (!Directory.Exists(root.Value))
            {
                continue;
            }

            foreach (var directory in SafeSubdirectories(root.Value))
            {
                var events = Path.Combine(directory, EventsFileName);
                if ((include != null && !include(Path.GetFileName(directory))) || !File.Exists(events))
                {
                    continue;
                }

                var info = new SessionInfo(Path.GetFileName(directory), directory, root.Key)
                {
                    LastWriteUtc = File.GetLastWriteTimeUtc(events),
                };
                ReadWorkspace(info);
                result.Add(info);
            }
        }

        return result.OrderByDescending(s => s.LastWriteUtc).Take(MaxSessions).ToList();
    }

    internal static void ReadWorkspace(SessionInfo info)
    {
        var path = Path.Combine(info.Directory, "workspace.yaml");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var values = ParseSimpleYaml(File.ReadAllLines(path));
            if (values.TryGetValue("cwd", out var cwd))
            {
                info.Cwd = cwd;
            }

            if (values.TryGetValue("name", out var name) || values.TryGetValue("summary", out name))
            {
                info.Title = TextFormat.FirstLine(name, 90);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal static Dictionary<string, string> ParseSimpleYaml(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                try
                {
                    value = JsonSerializer.Deserialize<string>(value);
                }
                catch (JsonException)
                {
                    value = value.Substring(1, value.Length - 2);
                }
            }
            else if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            {
                value = value.Substring(1, value.Length - 2).Replace("''", "'");
            }

            values[key] = value;
        }

        return values;
    }

    private static List<string> SafeSubdirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
