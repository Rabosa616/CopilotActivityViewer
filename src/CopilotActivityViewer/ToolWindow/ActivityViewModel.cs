using System.Runtime.Serialization;
using CopilotActivityViewer.Model;
using Microsoft.VisualStudio.Extensibility.UI;

namespace CopilotActivityViewer.ToolWindow;

/// <summary>
/// Remote UI data context of the tool window. The IDE calls setters and commands on thread-pool threads, and the
/// timer polls the event log in the background, so every state change runs one at a time through <see cref="EnqueueAsync"/>.
/// </summary>
[DataContract]
internal sealed class ActivityViewModel : ObservableObject, IDisposable
{
    private const int ScanEveryTicks = 5;
    private const int ResetThreshold = 200;
    private const string NoSessionsPrefix = "No Copilot agent sessions";
    private static readonly TimeSpan FollowSwitchIdleThreshold = TimeSpan.FromSeconds(15);

    private readonly SessionCatalog catalog;
    private readonly object queueLock = new();
    private readonly CancellationTokenSource disposed = new();
    private Task queue = Task.CompletedTask;
    private EventLogReader reader;
    private int tickCount;
    private string detailsKey;

    private SessionInfo selectedSession;
    private SessionModel model;
    private AgentNode selectedAgent;
    private ActivityItem selectedActivity;
    private StatusFilterOption selectedStatus;
    private bool followLatest = true;
    private bool allInstances;
    private string scopeText = string.Empty;
    private bool includeSubagents = true;
    private bool newestFirst = true;
    private string searchText = string.Empty;
    private string statusMessage = "Loading sessions…";
    private string summary = string.Empty;
    private string summaryColor = Palette.Neutral;
    private string summaryToolTip;
    private string countText = string.Empty;
    private XamlFragment details;

    public ActivityViewModel(SessionCatalog catalog)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        foreach (var kind in new[]
        {
            ActivityKind.Assistant, ActivityKind.Tool, ActivityKind.User, ActivityKind.Subagent, ActivityKind.Skill,
            ActivityKind.Completion, ActivityKind.Warning, ActivityKind.Error, ActivityKind.Notification, ActivityKind.Info,
        })
        {
            KindFilters.Add(new KindFilter(kind));
        }

        StatusOptions =
        [
            new StatusFilterOption(StatusFilter.All, "Any status"),
            new StatusFilterOption(StatusFilter.Ok, "OK"),
            new StatusFilterOption(StatusFilter.Failed, "Failed / errors"),
            new StatusFilterOption(StatusFilter.Running, "Running"),
            new StatusFilterOption(StatusFilter.Cancelled, "Cancelled"),
        ];
        selectedStatus = StatusOptions[0];
        details = new XamlFragment(DetailsXamlBuilder.Build(null));

        RefreshCommand = new AsyncCommand((_, _) => EnqueueAsync(Refresh));
        ToggleKindCommand = new AsyncCommand((parameter, _) => EnqueueAsync(() => ToggleKind(FindKind(parameter))));
        SoloKindCommand = new AsyncCommand((parameter, _) => EnqueueAsync(() => SoloKind(FindKind(parameter))));
        ClearFiltersCommand = new AsyncCommand((_, _) => EnqueueAsync(ResetFilters));
        CopyDetailsCommand = new AsyncCommand((_, _) => EnqueueAsync(CopyDetails));
    }

    [DataMember]
    public ObservableList<SessionInfo> Sessions { get; } = [];

    [DataMember]
    public SessionInfo SelectedSession
    {
        get => selectedSession;
        set => _ = EnqueueAsync(() => SelectSession(value));
    }

    [DataMember]
    public bool FollowLatest
    {
        get => followLatest;
        set => _ = EnqueueAsync(() =>
        {
            if (Set(ref followLatest, value, nameof(FollowLatest)) && value)
            {
                ScanSessions();
            }
        });
    }

    [DataMember]
    public string Summary
    {
        get => summary;
        private set => Set(ref summary, value);
    }

    /// <summary>Whether the viewer knows which IDE instance hosts it (and therefore can show only that instance's sessions).</summary>
    [DataMember]
    public bool HasScope => catalog.Scope != null;

    /// <summary>Shows the sessions of every Visual Studio / SSMS instance instead of only this one.</summary>
    [DataMember]
    public bool AllInstances
    {
        get => allInstances;
        set => _ = EnqueueAsync(() =>
        {
            if (Set(ref allInstances, value, nameof(AllInstances)))
            {
                ScanSessions();
                if (selectedSession == null && Sessions.Count > 0)
                {
                    SelectSession(Sessions[0]);
                }
            }
        });
    }

    [DataMember]
    public string ScopeText
    {
        get => scopeText;
        private set => Set(ref scopeText, value);
    }

    [DataMember]
    public string SummaryColor
    {
        get => summaryColor;
        private set => Set(ref summaryColor, value);
    }

    [DataMember]
    public string SummaryToolTip
    {
        get => summaryToolTip;
        private set => Set(ref summaryToolTip, value);
    }

    /// <summary>Agents of the current session in tree order (main agent first, sub-agents indented under the agent that started them).</summary>
    [DataMember]
    public ObservableList<AgentNode> Agents { get; } = [];

    [DataMember]
    public AgentNode SelectedAgent
    {
        get => selectedAgent;
        set => _ = EnqueueAsync(() =>
        {
            if (Set(ref selectedAgent, value ?? model?.MainAgent, nameof(SelectedAgent)))
            {
                OnFiltersChanged();
            }
        });
    }

    [DataMember]
    public ObservableList<KindFilter> KindFilters { get; } = [];

    [DataMember]
    public List<StatusFilterOption> StatusOptions { get; }

    [DataMember]
    public StatusFilterOption SelectedStatus
    {
        get => selectedStatus;
        set => _ = EnqueueAsync(() =>
        {
            if (Set(ref selectedStatus, value ?? StatusOptions[0], nameof(SelectedStatus)))
            {
                OnFiltersChanged();
            }
        });
    }

    [DataMember]
    public bool IncludeSubagents
    {
        get => includeSubagents;
        set => _ = EnqueueAsync(() =>
        {
            if (Set(ref includeSubagents, value, nameof(IncludeSubagents)))
            {
                OnFiltersChanged();
            }
        });
    }

    [DataMember]
    public string SearchText
    {
        get => searchText;
        set => _ = EnqueueAsync(() =>
        {
            if (Set(ref searchText, value ?? string.Empty, nameof(SearchText)))
            {
                OnFiltersChanged();
            }
        });
    }

    /// <summary>Remote UI cannot scroll a list from the extension, so the newest activity is shown on top by default.</summary>
    [DataMember]
    public bool NewestFirst
    {
        get => newestFirst;
        set => _ = EnqueueAsync(() =>
        {
            if (Set(ref newestFirst, value, nameof(NewestFirst)))
            {
                SyncVisible(reset: true);
            }
        });
    }

    [DataMember]
    public bool HasActiveFilters =>
        KindFilters.Any(f => !f.IsChecked) ||
        selectedStatus?.Value != StatusFilter.All ||
        !string.IsNullOrWhiteSpace(searchText) ||
        !includeSubagents ||
        (selectedAgent != null && model != null && !ReferenceEquals(selectedAgent, model.MainAgent));

    /// <summary>Visible (filtered) activities in display order.</summary>
    [DataMember]
    public ObservableList<ActivityItem> Activities { get; } = [];

    [DataMember]
    public string CountText
    {
        get => countText;
        private set => Set(ref countText, value);
    }

    [DataMember]
    public ActivityItem SelectedActivity
    {
        get => selectedActivity;
        set => _ = EnqueueAsync(() =>
        {
            if (Set(ref selectedActivity, value, nameof(SelectedActivity)))
            {
                Raise(nameof(HasSelectedActivity));
                UpdateDetails(force: true);
            }
        });
    }

    [DataMember]
    public bool HasSelectedActivity => selectedActivity != null;

    /// <summary>FlowDocument XAML, materialized by the IDE.</summary>
    [DataMember]
    public XamlFragment Details
    {
        get => details;
        private set => Set(ref details, value);
    }

    [DataMember]
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

    [DataMember]
    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    [DataMember]
    public IAsyncCommand RefreshCommand { get; }

    [DataMember]
    public IAsyncCommand ToggleKindCommand { get; }

    [DataMember]
    public IAsyncCommand SoloKindCommand { get; }

    [DataMember]
    public IAsyncCommand ClearFiltersCommand { get; }

    [DataMember]
    public IAsyncCommand CopyDetailsCommand { get; }

    internal SessionModel Model => model;

    internal string DetailsXaml => details?.Xaml;

    internal Func<string, bool> ClipboardWriter { get; set; } = ClipboardText.TrySet;

    /// <summary>Loads the sessions and starts polling the selected event log every second.</summary>
    public void Start()
    {
        _ = EnqueueAsync(Initialize);
        _ = RunTimerAsync(disposed.Token);
    }

    /// <summary>Completes when every queued change has been applied (used by tests).</summary>
    public Task WhenIdleAsync() => EnqueueAsync(() => { });

    public Task InitializeAsync() => EnqueueAsync(Initialize);

    public Task PollAsync() => EnqueueAsync(Poll);

    public Task ScanSessionsAsync() => EnqueueAsync(ScanSessions);

    public void Dispose()
    {
        disposed.Cancel();
        _ = EnqueueAsync(() =>
        {
            reader = null;
        });
    }

    internal Task EnqueueAsync(Action work)
    {
        lock (queueLock)
        {
            queue = queue.ContinueWith(
                _ =>
                {
                    try
                    {
                        work();
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = ex.Message;
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
            return queue;
        }
    }

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await EnqueueAsync(() =>
                {
                    tickCount++;
                    Poll();
                    if (tickCount % ScanEveryTicks == 0)
                    {
                        ScanSessions();
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Initialize()
    {
        ScanSessions();
        if (selectedSession == null && Sessions.Count > 0)
        {
            SelectSession(Sessions[0]);
        }

        if (Sessions.Count == 0)
        {
            StatusMessage = NoSessionsMessage();
        }
    }

    private void Refresh()
    {
        ScanSessions();
        LoadSession(selectedSession);
    }

    private void SelectSession(SessionInfo session)
    {
        if (Set(ref selectedSession, session, nameof(SelectedSession)))
        {
            LoadSession(session);
        }
    }

    private void ScanSessions()
    {
        try
        {
            MergeSessions(catalog.Discover(SessionFilter()));
            if (followLatest)
            {
                var newest = Sessions.OrderByDescending(s => s.LastWriteUtc).FirstOrDefault();
                var current = selectedSession;
                if (newest != null && !ReferenceEquals(newest, current) &&
                    (current == null || (newest.LastWriteUtc > current.LastWriteUtc && DateTime.UtcNow - current.LastWriteUtc > FollowSwitchIdleThreshold)))
                {
                    SelectSession(newest);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Could not scan sessions: " + ex.Message;
        }
    }

    private void Poll()
    {
        var currentReader = reader;
        var currentModel = model;
        if (currentReader == null || currentModel == null)
        {
            return;
        }

        try
        {
            var lines = currentReader.ReadNewLines();
            if (currentReader.WasReset)
            {
                LoadSession(selectedSession);
                return;
            }

            if (lines.Count > 0)
            {
                foreach (var line in lines)
                {
                    currentModel.ApplyLine(line);
                }

                currentModel.UpdateSummary();
                UpdateSummary();
                SyncAgents();
                UpdateKindCounts();
                SyncVisible(reset: false);
                UpdateDetails(force: false);
            }

            currentModel.Tick();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Could not read events: " + ex.Message;
        }
    }

    private void LoadSession(SessionInfo session)
    {
        reader = null;
        if (selectedActivity != null)
        {
            selectedActivity = null;
            Raise(nameof(SelectedActivity));
            Raise(nameof(HasSelectedActivity));
        }

        if (session == null)
        {
            model = null;
            selectedAgent = null;
            Raise(nameof(SelectedAgent));
            UpdateSummary();
            SyncAgents();
            UpdateKindCounts();
            SyncVisible(reset: true);
            UpdateDetails(force: true);
            return;
        }

        StatusMessage = "Loading " + session.Display + "…";
        var newReader = new EventLogReader(session.EventsPath);
        var loaded = new SessionModel();
        try
        {
            foreach (var line in newReader.ReadNewLines())
            {
                loaded.ApplyLine(line);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Could not load session: " + ex.Message;
            return;
        }

        loaded.UpdateSummary();
        reader = newReader;
        model = loaded;
        selectedAgent = loaded.MainAgent;
        UpdateSummary();
        SyncAgents();
        Raise(nameof(SelectedAgent));
        UpdateKindCounts();
        SyncVisible(reset: true);
        UpdateDetails(force: true);
        Raise(nameof(HasActiveFilters));
        StatusMessage = loaded.Activities.Count == 0 ? "No activity yet in this session." : string.Empty;
    }

    /// <summary>Restricts the sessions to the ones served by this IDE instance (unless every instance is requested).</summary>
    private Func<string, bool> SessionFilter()
    {
        var scope = catalog.Scope;
        if (scope == null)
        {
            ScopeText = string.Empty;
            return null;
        }

        if (allInstances)
        {
            ScopeText = "All instances";
            return null;
        }

        var ids = scope.Refresh();
        if (ids == null)
        {
            ScopeText = scope.Description + " · sessions not identified, showing all";
            return null;
        }

        ScopeText = "This " + scope.Description + (scope.CopilotRunning ? string.Empty : " · Copilot not started");
        var set = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    private string NoSessionsMessage()
    {
        var scope = catalog.Scope;
        if (scope != null && !allInstances)
        {
            return NoSessionsPrefix + " in this " + scope.Description + " yet." + Environment.NewLine +
                "Start a Copilot chat in this instance to see its activity here, or tick \"All instances\".";
        }

        return NoSessionsPrefix + " in:" + Environment.NewLine + string.Join(Environment.NewLine, catalog.Roots.Select(r => r.Value));
    }

    private void MergeSessions(List<SessionInfo> found)
    {
        var byId = Sessions.ToDictionary(s => s.Directory, StringComparer.OrdinalIgnoreCase);
        var foundIds = new HashSet<string>(found.Select(s => s.Directory), StringComparer.OrdinalIgnoreCase);

        foreach (var stale in Sessions.Where(s => !foundIds.Contains(s.Directory)).ToList())
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

        if (selectedSession != null && !Sessions.Contains(selectedSession))
        {
            SelectSession(Sessions.FirstOrDefault());
        }

        if (Sessions.Count > 0 && StatusMessage != null && StatusMessage.StartsWith(NoSessionsPrefix, StringComparison.Ordinal))
        {
            StatusMessage = string.Empty;
        }
        else if (Sessions.Count == 0 && selectedSession == null)
        {
            StatusMessage = NoSessionsMessage();
        }
    }

    private void UpdateSummary()
    {
        Summary = model?.Summary ?? string.Empty;
        SummaryColor = model != null ? model.MainAgent.StatusColor : Palette.Neutral;
        SummaryToolTip = model?.Cwd;
    }

    internal void ResetFilters()
    {
        foreach (var filter in KindFilters)
        {
            filter.IsChecked = true;
        }

        Set(ref selectedStatus, StatusOptions[0], nameof(SelectedStatus));
        Set(ref searchText, string.Empty, nameof(SearchText));
        Set(ref includeSubagents, true, nameof(IncludeSubagents));
        Set(ref selectedAgent, model?.MainAgent, nameof(SelectedAgent));
        OnFiltersChanged();
    }

    internal void ToggleKind(KindFilter filter)
    {
        if (filter != null)
        {
            filter.IsChecked = !filter.IsChecked;
            OnFiltersChanged();
        }
    }

    /// <summary>Shows only the given activity type, or every type again when it is already the only one shown.</summary>
    internal void SoloKind(KindFilter only)
    {
        if (only == null)
        {
            return;
        }

        var alreadySolo = only.IsChecked && KindFilters.All(f => ReferenceEquals(f, only) || !f.IsChecked);
        foreach (var filter in KindFilters)
        {
            filter.IsChecked = alreadySolo || ReferenceEquals(filter, only);
        }

        OnFiltersChanged();
    }

    private KindFilter FindKind(object parameter) => parameter switch
    {
        KindFilter filter => KindFilters.FirstOrDefault(f => f.Kind == filter.Kind),
        string label => KindFilters.FirstOrDefault(f => string.Equals(f.Label, label, StringComparison.OrdinalIgnoreCase)),
        _ => null,
    };

    private void CopyDetails()
    {
        var text = selectedActivity?.Details;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!ClipboardWriter(text))
        {
            StatusMessage = "Could not copy to the clipboard.";
        }
    }

    private void OnFiltersChanged()
    {
        Raise(nameof(HasActiveFilters));
        SyncVisible(reset: true);
    }

    private void UpdateKindCounts()
    {
        var counts = new Dictionary<ActivityKind, int>();
        if (model != null)
        {
            foreach (var item in model.Activities)
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

    private void SyncAgents()
    {
        var ordered = new List<AgentNode>();
        if (model != null)
        {
            var stack = new Stack<AgentNode>();
            stack.Push(model.MainAgent);
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

        if (!ordered.SequenceEqual(Agents))
        {
            Agents.Clear();
            Agents.AddRange(ordered);
        }
    }

    /// <summary>
    /// Brings <see cref="Activities"/> in line with the filters. New events are appended (or prepended when newest first)
    /// so the IDE only receives the changes; a filter change replaces the whole list.
    /// </summary>
    private void SyncVisible(bool reset)
    {
        var all = model?.Activities;
        var total = all?.Count ?? 0;
        var desired = new List<ActivityItem>();
        if (all != null)
        {
            foreach (var item in all)
            {
                if (FilterActivity(item))
                {
                    desired.Add(item);
                }
            }
        }

        if (newestFirst)
        {
            desired.Reverse();
        }

        if (reset || !TryApplyIncrementally(desired))
        {
            Activities.Clear();
            Activities.AddRange(desired);
        }

        CountText = desired.Count == total ? total + " events" : desired.Count + " of " + total + " events";
        if (selectedActivity != null && !desired.Contains(selectedActivity))
        {
            selectedActivity = null;
            Raise(nameof(SelectedActivity));
            Raise(nameof(HasSelectedActivity));
            UpdateDetails(force: true);
        }
    }

    /// <summary>Removes and inserts only the differences; <paramref name="desired"/> and the current list are both ordered subsets of the timeline.</summary>
    private bool TryApplyIncrementally(List<ActivityItem> desired)
    {
        var keep = new HashSet<ActivityItem>(desired);
        var removals = 0;
        for (var i = Activities.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Activities[i]) && ++removals > ResetThreshold)
            {
                return false;
            }
        }

        for (var i = Activities.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Activities[i]))
            {
                Activities.RemoveAt(i);
            }
        }

        var current = 0;
        var next = 0;
        while (next < desired.Count)
        {
            if (current < Activities.Count && ReferenceEquals(Activities[current], desired[next]))
            {
                current++;
                next++;
                continue;
            }

            var anchor = current < Activities.Count ? Activities[current] : null;
            var run = new List<ActivityItem>();
            while (next < desired.Count && !ReferenceEquals(desired[next], anchor))
            {
                run.Add(desired[next]);
                next++;
            }

            Activities.InsertRange(current, run);
            current += run.Count;
        }

        return Activities.Count == desired.Count;
    }

    private bool FilterActivity(ActivityItem item)
    {
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

        if (!StatusFilterOption.Matches(selectedStatus?.Value ?? StatusFilter.All, item))
        {
            return false;
        }

        var agent = selectedAgent ?? model?.MainAgent;
        if (agent != null)
        {
            var visible = includeSubagents ? agent.IsSelfOrAncestorOf(item.Agent) : ReferenceEquals(agent, item.Agent);
            if (!visible)
            {
                return false;
            }
        }

        return item.Matches(searchText.Trim());
    }

    private void UpdateDetails(bool force)
    {
        var item = selectedActivity;
        var key = item == null ? null : item.StatusText + "|" + item.DurationText.Length + "|" + (item.Details?.Length ?? 0) + "|" + (item.ResultText?.Length ?? 0) + "|" + item.Note;
        if (!force && key == detailsKey)
        {
            return;
        }

        detailsKey = key;
        Details = new XamlFragment(DetailsXamlBuilder.Build(item));
    }
}
