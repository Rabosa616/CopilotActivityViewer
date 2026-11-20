using System.Runtime.Serialization;
using System.Text.Json.Nodes;

namespace CopilotActivityViewer.Model;

public enum ActivityKind
{
    User,
    Assistant,
    Tool,
    Subagent,
    Skill,
    Notification,
    Info,
    Warning,
    Error,
    Completion,
}

public enum ActivityStatus
{
    None,
    Running,
    AwaitingApproval,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>One row of the activity timeline. Only the display texts are <see cref="DataMemberAttribute"/>s sent to the IDE.</summary>
[DataContract]
public sealed class ActivityItem : ObservableObject
{
    private string title;
    private string subtitle;
    private string details;
    private ActivityStatus status;
    private TimeSpan? duration;

    public ActivityItem(DateTimeOffset timestamp, AgentNode agent, ActivityKind kind)
    {
        Timestamp = timestamp;
        Agent = agent;
        Kind = kind;
    }

    public DateTimeOffset Timestamp { get; }

    public AgentNode Agent { get; }

    public ActivityKind Kind { get; }

    [DataMember]
    public string KindText => KindLabel(Kind);

    [DataMember]
    public string KindColor => Palette.For(Kind);

    public string ToolCallId { get; set; }

    public string ToolName { get; set; }

    /// <summary>Raw tool arguments (tools and the task call that started a sub-agent).</summary>
    public JsonObject Arguments { get; set; }

    /// <summary>Free text content (markdown) of messages, notifications, errors…</summary>
    public string Body { get; set; }

    /// <summary>Tool result or error text, set when the tool completes.</summary>
    public string ResultText { get; set; }

    public bool ResultFailed { get; set; }

    /// <summary>Extra remark such as a denied permission.</summary>
    public string Note { get; set; }

    public IList<KeyValuePair<string, string>> Properties { get; set; }

    [DataMember]
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    [DataMember]
    public string Title
    {
        get => title;
        set => Set(ref title, value);
    }

    [DataMember]
    public string Subtitle
    {
        get => subtitle;
        set => Set(ref subtitle, value);
    }

    /// <summary>Full plain text of the item (used by search and copy).</summary>
    public string Details
    {
        get => details;
        set => Set(ref details, value);
    }

    public ActivityStatus Status
    {
        get => status;
        set
        {
            if (Set(ref status, value))
            {
                Raise(nameof(StatusText));
                Raise(nameof(StatusColor));
                Raise(nameof(DurationText));
                Raise(nameof(IsActive));
            }
        }
    }

    public bool IsActive => Status == ActivityStatus.Running || Status == ActivityStatus.AwaitingApproval;

    [DataMember]
    public string StatusText => Status switch
    {
        ActivityStatus.Running => "Running",
        ActivityStatus.AwaitingApproval => "Approval",
        ActivityStatus.Succeeded => "OK",
        ActivityStatus.Failed => "Failed",
        ActivityStatus.Cancelled => "Cancelled",
        _ => string.Empty,
    };

    [DataMember]
    public string StatusColor => Palette.For(Status);

    public TimeSpan? Duration
    {
        get => duration;
        set { if (Set(ref duration, value)) { Raise(nameof(DurationText)); } }
    }

    [DataMember]
    public string DurationText
    {
        get
        {
            if (Duration.HasValue)
            {
                return TextFormat.Duration(Duration.Value);
            }

            return IsActive ? TextFormat.Duration(DateTimeOffset.UtcNow - Timestamp) : string.Empty;
        }
    }

    [DataMember]
    public string AgentLabel => Agent?.IsMain != false ? "main" : Agent.Label;

    public static string KindLabel(ActivityKind kind) => kind switch
    {
        ActivityKind.User => "USER",
        ActivityKind.Assistant => "AI",
        ActivityKind.Tool => "TOOL",
        ActivityKind.Subagent => "AGENT",
        ActivityKind.Skill => "SKILL",
        ActivityKind.Notification => "NOTIFY",
        ActivityKind.Warning => "WARN",
        ActivityKind.Error => "ERROR",
        ActivityKind.Completion => "DONE",
        _ => "INFO",
    };

    internal void RaiseTimeDependent() => Raise(nameof(DurationText));

    public bool Matches(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return Contains(Title, search) || Contains(Subtitle, search) || Contains(ToolName, search) || Contains(AgentLabel, search) || Contains(Details, search);
    }

    private static bool Contains(string text, string search) =>
        text != null && text.Contains(search, StringComparison.OrdinalIgnoreCase);
}
