namespace CopilotActivityViewer.Model;

/// <summary>Theme-neutral accent colors (readable on dark, light and blue themes), as strings so Remote UI can bind them to brushes.</summary>
internal static class Palette
{
    public const string Running = "#3B8EEA";
    public const string Waiting = "#E0A000";
    public const string Success = "#3FA34D";
    public const string Failure = "#E5484D";
    public const string Neutral = "#8C8C8C";

    public static string For(ActivityStatus status) => status switch
    {
        ActivityStatus.Running => Running,
        ActivityStatus.AwaitingApproval => Waiting,
        ActivityStatus.Succeeded => Success,
        ActivityStatus.Failed => Failure,
        ActivityStatus.Cancelled => Waiting,
        _ => "Transparent",
    };

    public static string For(AgentStatus status) => status switch
    {
        AgentStatus.Running => Running,
        AgentStatus.Completed => Success,
        AgentStatus.Failed => Failure,
        AgentStatus.Aborted => Waiting,
        _ => Neutral,
    };

    public static string For(ActivityKind kind) => kind switch
    {
        ActivityKind.User => "#8A63D2",
        ActivityKind.Assistant => "#3B8EEA",
        ActivityKind.Tool => "#6E7681",
        ActivityKind.Subagent => "#D97706",
        ActivityKind.Skill => "#0E9F6E",
        ActivityKind.Completion => Success,
        ActivityKind.Warning => Waiting,
        ActivityKind.Error => Failure,
        ActivityKind.Notification => "#5B6B7F",
        _ => "#5B6B7F",
    };
}
