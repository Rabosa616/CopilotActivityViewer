using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace CopilotActivityViewer.Model
{
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

        public string Source { get; }

        public string EventsPath => System.IO.Path.Combine(Directory, SessionCatalog.EventsFileName);

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

        public string Display
        {
            get
            {
                var folder = string.IsNullOrEmpty(Cwd) ? null : System.IO.Path.GetFileName(Cwd.TrimEnd('\\', '/'));
                var name = string.IsNullOrWhiteSpace(Title) ? Id : Title;
                return LastWriteUtc.ToLocalTime().ToString("dd/MM HH:mm") + "  " + (folder != null ? "[" + folder + "] " : string.Empty) + name;
            }
        }

        public override string ToString() => Display;
    }

    /// <summary>Discovers Copilot agent sessions stored on disk (Visual Studio and Copilot CLI).</summary>
    public sealed class SessionCatalog
    {
        public const string EventsFileName = "events.jsonl";
        private const int MaxSessions = 100;

        public SessionCatalog(IEnumerable<KeyValuePair<string, string>> roots)
        {
            Roots = roots.ToList();
        }

        /// <summary>Root folders as (source label, path) pairs.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Roots { get; }

        public static SessionCatalog CreateDefault()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new SessionCatalog(new[]
            {
                new KeyValuePair<string, string>("VS", Path.Combine(localAppData, "Microsoft", "VisualStudio", "CopilotCli", "session-state")),
                new KeyValuePair<string, string>("CLI", Path.Combine(userProfile, ".copilot", "session-state")),
            });
        }

        public List<SessionInfo> Discover()
        {
            var result = new List<SessionInfo>();
            foreach (var root in Roots)
            {
                if (!System.IO.Directory.Exists(root.Value))
                {
                    continue;
                }

                IEnumerable<string> directories;
                try
                {
                    directories = System.IO.Directory.EnumerateDirectories(root.Value).ToList();
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var directory in directories)
                {
                    var events = Path.Combine(directory, EventsFileName);
                    if (!File.Exists(events))
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
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                {
                    try
                    {
                        value = JsonConvert.DeserializeObject<string>(value);
                    }
                    catch (JsonException)
                    {
                        value = value.Substring(1, value.Length - 2);
                    }
                }
                else if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
                {
                    value = value.Substring(1, value.Length - 2).Replace("''", "'");
                }

                values[key] = value;
            }

            return values;
        }
    }
}
