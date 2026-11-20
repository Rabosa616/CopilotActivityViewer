using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using CopilotActivityViewer.Model;

namespace CopilotActivityViewer.ToolWindow
{
    public sealed class ActivityViewModel : ObservableObject, IDisposable
    {
        private const int ScanEveryTicks = 5;
        private static readonly TimeSpan FollowSwitchIdleThreshold = TimeSpan.FromSeconds(15);

        private readonly SessionCatalog catalog;
        private readonly DispatcherTimer timer;
        private EventLogReader reader;
        private int generation;
        private bool polling;
        private bool scanning;
        private int tickCount;

        private SessionInfo selectedSession;
        private SessionModel model;
        private ListCollectionView activities;
        private AgentNode selectedAgent;
        private ActivityItem selectedActivity;
        private bool followLatest = true;
        private bool includeSubagents = true;
        private bool autoScroll = true;
        private string searchText = string.Empty;
        private string statusMessage = "Loading sessions…";
        private StatusFilterOption selectedStatus;

        public ActivityViewModel(SessionCatalog catalog, bool startTimer = true)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            foreach (var kind in new[]
            {
                ActivityKind.Assistant, ActivityKind.Tool, ActivityKind.User, ActivityKind.Subagent, ActivityKind.Skill,
                ActivityKind.Completion, ActivityKind.Warning, ActivityKind.Error, ActivityKind.Notification, ActivityKind.Info,
            })
            {
                var filter = new KindFilter(kind);
                filter.Changed += (s, e) => OnFiltersChanged();
                KindFilters.Add(filter);
            }

            StatusOptions = new[]
            {
                new StatusFilterOption(StatusFilter.All, "Any status"),
                new StatusFilterOption(StatusFilter.Ok, "OK"),
                new StatusFilterOption(StatusFilter.Failed, "Failed / errors"),
                new StatusFilterOption(StatusFilter.Running, "Running"),
                new StatusFilterOption(StatusFilter.Cancelled, "Cancelled"),
            };
            selectedStatus = StatusOptions[0];

            if (startTimer)
            {
                timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
                timer.Tick += OnTick;
                timer.Start();
            }
        }

        public event EventHandler ActivitiesAppended;

        public ObservableCollection<SessionInfo> Sessions { get; } = new ObservableCollection<SessionInfo>();

        public ObservableCollection<KindFilter> KindFilters { get; } = new ObservableCollection<KindFilter>();

        public IReadOnlyList<StatusFilterOption> StatusOptions { get; }

        /// <summary>Agents of the current session in tree order, for the agent filter.</summary>
        public ObservableCollection<AgentNode> AgentOptions { get; } = new ObservableCollection<AgentNode>();

        public StatusFilterOption SelectedStatus
        {
            get => selectedStatus;
            set
            {
                if (Set(ref selectedStatus, value ?? StatusOptions[0]))
                {
                    OnFiltersChanged();
                }
            }
        }

        public bool HasActiveFilters =>
            KindFilters.Any(f => !f.IsChecked) ||
            SelectedStatus?.Value != StatusFilter.All ||
            !string.IsNullOrWhiteSpace(SearchText) ||
            !IncludeSubagents ||
            (SelectedAgent != null && Model != null && !ReferenceEquals(SelectedAgent, Model.MainAgent));

        public SessionInfo SelectedSession
        {
            get => selectedSession;
            set
            {
                if (Set(ref selectedSession, value))
                {
                    _ = LoadSessionAsync(value);
                }
            }
        }

        public SessionModel Model
        {
            get => model;
            private set => Set(ref model, value);
        }

        public ListCollectionView Activities
        {
            get => activities;
            private set => Set(ref activities, value);
        }

        public AgentNode SelectedAgent
        {
            get => selectedAgent;
            set
            {
                if (Set(ref selectedAgent, value ?? Model?.MainAgent))
                {
                    OnFiltersChanged();
                }
            }
        }

        public ActivityItem SelectedActivity
        {
            get => selectedActivity;
            set => Set(ref selectedActivity, value);
        }

        public bool FollowLatest
        {
            get => followLatest;
            set
            {
                if (Set(ref followLatest, value) && value)
                {
                    _ = ScanSessionsAsync();
                }
            }
        }

        public bool IncludeSubagents
        {
            get => includeSubagents;
            set { if (Set(ref includeSubagents, value)) { OnFiltersChanged(); } }
        }

        public bool AutoScroll
        {
            get => autoScroll;
            set
            {
                if (Set(ref autoScroll, value) && value)
                {
                    ActivitiesAppended?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public string SearchText
        {
            get => searchText;
            set { if (Set(ref searchText, value ?? string.Empty)) { OnFiltersChanged(); } }
        }

        public string StatusMessage
        {
            get => statusMessage;
            private set
            {
                if (Set(ref statusMessage, value))
                {
                    Raise(nameof(HasStatusMessage));
                }
            }
        }

        public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

        public async Task InitializeAsync()
        {
            await ScanSessionsAsync();
            if (SelectedSession == null)
            {
                SelectedSession = Sessions.FirstOrDefault();
            }

            if (Sessions.Count == 0)
            {
                StatusMessage = "No Copilot agent sessions found in:" + Environment.NewLine +
                    string.Join(Environment.NewLine, catalog.Roots.Select(r => r.Value));
            }
        }

        public async Task RefreshAsync()
        {
            await ScanSessionsAsync();
            await LoadSessionAsync(SelectedSession);
        }

        public async Task ScanSessionsAsync()
        {
            if (scanning)
            {
                return;
            }

            scanning = true;
            try
            {
                var found = await Task.Run(() => catalog.Discover());
                MergeSessions(found);
                if (FollowLatest)
                {
                    var newest = Sessions.OrderByDescending(s => s.LastWriteUtc).FirstOrDefault();
                    var current = SelectedSession;
                    if (newest != null && !ReferenceEquals(newest, current) &&
                        (current == null || (newest.LastWriteUtc > current.LastWriteUtc && DateTime.UtcNow - current.LastWriteUtc > FollowSwitchIdleThreshold)))
                    {
                        SelectedSession = newest;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Could not scan sessions: " + ex.Message;
            }
            finally
            {
                scanning = false;
            }
        }

        public async Task PollAsync()
        {
            var currentReader = reader;
            var currentModel = Model;
            if (polling || currentReader == null || currentModel == null)
            {
                return;
            }

            polling = true;
            try
            {
                var gen = generation;
                var events = await Task.Run(() => currentReader.ReadNewLines().Select(SessionModel.ParseLine).Where(e => e != null).ToList());
                if (gen != generation)
                {
                    return;
                }

                if (currentReader.WasReset)
                {
                    await LoadSessionAsync(SelectedSession);
                    return;
                }

                if (events.Count > 0)
                {
                    foreach (var e in events)
                    {
                        currentModel.Apply(e);
                    }

                    currentModel.UpdateSummary();
                    SyncAgentOptions();
                    UpdateKindCounts();
                    ActivitiesAppended?.Invoke(this, EventArgs.Empty);
                }

                currentModel.Tick();
            }
            catch (Exception ex)
            {
                StatusMessage = "Could not read events: " + ex.Message;
            }
            finally
            {
                polling = false;
            }
        }

        public void Dispose()
        {
            if (timer != null)
            {
                timer.Stop();
                timer.Tick -= OnTick;
            }

            generation++;
            reader = null;
        }

        private async void OnTick(object sender, EventArgs e)
        {
            try
            {
                tickCount++;
                await PollAsync();
                if (tickCount % ScanEveryTicks == 0)
                {
                    await ScanSessionsAsync();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }

        private async Task LoadSessionAsync(SessionInfo session)
        {
            var gen = ++generation;
            reader = null;
            SelectedActivity = null;
            if (session == null)
            {
                Model = null;
                Activities = null;
                SyncAgentOptions();
                UpdateKindCounts();
                return;
            }

            StatusMessage = "Loading " + session.Display + "…";
            var newReader = new EventLogReader(session.EventsPath);
            SessionModel loaded;
            try
            {
                loaded = await Task.Run(() =>
                {
                    var m = new SessionModel();
                    foreach (var line in newReader.ReadNewLines())
                    {
                        m.ApplyLine(line);
                    }

                    m.UpdateSummary();
                    return m;
                });
            }
            catch (Exception ex)
            {
                if (gen == generation)
                {
                    StatusMessage = "Could not load session: " + ex.Message;
                }

                return;
            }

            if (gen != generation)
            {
                return;
            }

            reader = newReader;
            selectedAgent = loaded.MainAgent;
            Model = loaded;
            SyncAgentOptions();
            Raise(nameof(SelectedAgent));
            UpdateKindCounts();
            var view = new ListCollectionView(loaded.Activities) { Filter = FilterActivity };
            view.IsLiveFiltering = true;
            view.LiveFilteringProperties.Add(nameof(ActivityItem.Status));
            Activities = view;
            Raise(nameof(HasActiveFilters));
            StatusMessage = loaded.Activities.Count == 0 ? "No activity yet in this session." : string.Empty;
            ActivitiesAppended?.Invoke(this, EventArgs.Empty);
        }

        private void MergeSessions(List<SessionInfo> found)
        {
            var byId = Sessions.ToDictionary(s => s.Directory, StringComparer.OrdinalIgnoreCase);
            var foundIds = new HashSet<string>(found.Select(s => s.Directory), StringComparer.OrdinalIgnoreCase);

            foreach (var stale in Sessions.Where(s => !foundIds.Contains(s.Directory) && !ReferenceEquals(s, SelectedSession)).ToList())
            {
                Sessions.Remove(stale);
            }

            foreach (var session in found)
            {
                if (byId.TryGetValue(session.Directory, out var existing))
                {
                    existing.Cwd = session.Cwd;
                    existing.Title = session.Title;
                    existing.LastWriteUtc = session.LastWriteUtc;
                    continue;
                }

                var index = 0;
                while (index < Sessions.Count && Sessions[index].LastWriteUtc >= session.LastWriteUtc)
                {
                    index++;
                }

                Sessions.Insert(index, session);
            }

            if (Sessions.Count > 0 && StatusMessage != null && StatusMessage.StartsWith("No Copilot agent sessions", StringComparison.Ordinal))
            {
                StatusMessage = string.Empty;
            }
        }

        public void ResetFilters()
        {
            foreach (var filter in KindFilters)
            {
                filter.SetCheckedSilently(true);
            }

            selectedStatus = StatusOptions[0];
            Raise(nameof(SelectedStatus));
            searchText = string.Empty;
            Raise(nameof(SearchText));
            includeSubagents = true;
            Raise(nameof(IncludeSubagents));
            selectedAgent = Model?.MainAgent;
            Raise(nameof(SelectedAgent));
            OnFiltersChanged();
        }

        /// <summary>Shows only the given activity type, or every type again when it is already the only one shown.</summary>
        public void SoloKind(KindFilter only)
        {
            var alreadySolo = only.IsChecked && KindFilters.All(f => ReferenceEquals(f, only) || !f.IsChecked);
            foreach (var filter in KindFilters)
            {
                filter.SetCheckedSilently(alreadySolo || ReferenceEquals(filter, only));
            }

            OnFiltersChanged();
        }

        private void OnFiltersChanged()
        {
            Raise(nameof(HasActiveFilters));
            RefreshFilter();
        }

        private void UpdateKindCounts()
        {
            var counts = new Dictionary<ActivityKind, int>();
            if (Model != null)
            {
                foreach (var item in Model.Activities)
                {
                    counts.TryGetValue(item.Kind, out var n);
                    counts[item.Kind] = n + 1;
                }
            }

            foreach (var filter in KindFilters)
            {
                filter.Count = counts.TryGetValue(filter.Kind, out var n) ? n : 0;
            }
        }

        private void SyncAgentOptions()
        {
            var ordered = new List<AgentNode>();
            if (Model != null)
            {
                var stack = new Stack<AgentNode>();
                stack.Push(Model.MainAgent);
                while (stack.Count > 0 && ordered.Count < 1000)
                {
                    var node = stack.Pop();
                    ordered.Add(node);
                    for (var i = node.Children.Count - 1; i >= 0; i--)
                    {
                        stack.Push(node.Children[i]);
                    }
                }
            }

            for (var i = 0; i < ordered.Count; i++)
            {
                if (i < AgentOptions.Count && ReferenceEquals(AgentOptions[i], ordered[i]))
                {
                    continue;
                }

                var existing = AgentOptions.IndexOf(ordered[i]);
                if (existing >= 0)
                {
                    AgentOptions.Move(existing, i);
                }
                else
                {
                    AgentOptions.Insert(i, ordered[i]);
                }
            }

            while (AgentOptions.Count > ordered.Count)
            {
                AgentOptions.RemoveAt(AgentOptions.Count - 1);
            }
        }

        private void RefreshFilter()
        {
            Activities?.Refresh();
            ActivitiesAppended?.Invoke(this, EventArgs.Empty);
        }

        private bool FilterActivity(object obj)
        {
            if (!(obj is ActivityItem item))
            {
                return false;
            }

            foreach (var filter in KindFilters)
            {
                if (filter.Kind == item.Kind)
                {
                    if (!filter.IsChecked)
                    {
                        return false;
                    }

                    break;
                }
            }

            if (!StatusFilterOption.Matches(SelectedStatus?.Value ?? StatusFilter.All, item))
            {
                return false;
            }

            var agent = SelectedAgent ?? Model?.MainAgent;
            if (agent != null)
            {
                var visible = IncludeSubagents ? agent.IsSelfOrAncestorOf(item.Agent) : ReferenceEquals(agent, item.Agent);
                if (!visible)
                {
                    return false;
                }
            }

            return item.Matches(SearchText.Trim());
        }
    }
}
