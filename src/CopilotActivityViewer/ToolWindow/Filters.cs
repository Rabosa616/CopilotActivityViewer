using System.Runtime.Serialization;
using CopilotActivityViewer.Model;

namespace CopilotActivityViewer.ToolWindow;

public enum StatusFilter
{
    All,
    Ok,
    Failed,
    Running,
    Cancelled,
}

[DataContract]
public sealed class StatusFilterOption
{
    public StatusFilterOption(StatusFilter value, string label)
    {
        Value = value;
        Label = label;
    }

    public StatusFilter Value { get; }

    [DataMember]
    public string Label { get; }

    public override string ToString() => Label;

    public static bool Matches(StatusFilter filter, ActivityItem item) => filter switch
    {
        StatusFilter.Ok => item.Status == ActivityStatus.Succeeded || item.Kind == ActivityKind.Completion,
        StatusFilter.Failed => item.Status == ActivityStatus.Failed || item.Kind == ActivityKind.Error,
        StatusFilter.Running => item.IsActive,
        StatusFilter.Cancelled => item.Status == ActivityStatus.Cancelled || (item.Kind == ActivityKind.Warning && item.Title == "Aborted"),
        _ => true,
    };
}

/// <summary>Toggle chip for one <see cref="ActivityKind"/> in the activity filter bar.</summary>
[DataContract]
public sealed class KindFilter : ObservableObject
{
    private bool isChecked = true;
    private int count;

    public KindFilter(ActivityKind kind)
    {
        Kind = kind;
        Label = ActivityItem.KindLabel(kind);
        Color = Palette.For(kind);
    }

    public ActivityKind Kind { get; }

    [DataMember]
    public string Label { get; }

    [DataMember]
    public string Color { get; }

    [DataMember]
    public bool IsChecked
    {
        get => isChecked;
        internal set => Set(ref isChecked, value);
    }

    [DataMember]
    public int Count
    {
        get => count;
        set
        {
            if (Set(ref count, value))
            {
                Raise(nameof(HasItems));
            }
        }
    }

    [DataMember]
    public bool HasItems => Count > 0;
}
