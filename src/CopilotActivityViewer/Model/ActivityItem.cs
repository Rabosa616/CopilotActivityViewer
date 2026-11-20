using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace CopilotActivityViewer.Model
{
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

        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case ActivityKind.User: return "USER";
                    case ActivityKind.Assistant: return "AI";
                    case ActivityKind.Tool: return "TOOL";
                    case ActivityKind.Subagent: return "AGENT";
                    case ActivityKind.Skill: return "SKILL";
                    case ActivityKind.Notification: return "NOTIFY";
                    case ActivityKind.Warning: return "WARN";
                    case ActivityKind.Error: return "ERROR";
                    case ActivityKind.Completion: return "DONE";
                    default: return "INFO";
                }
            }
        }

        public string ToolCallId { get; set; }

        public string ToolName { get; set; }

        /// <summary>Raw tool arguments (tools and the task call that started a sub-agent).</summary>
        public JObject Arguments { get; set; }

        /// <summary>Free text content (markdown) of messages, notifications, errors…</summary>
        public string Body { get; set; }

        /// <summary>Tool result or error text, set when the tool completes.</summary>
        public string ResultText { get; set; }

        public bool ResultFailed { get; set; }

        /// <summary>Extra remark such as a denied permission.</summary>
        public string Note { get; set; }

        public IList<KeyValuePair<string, string>> Properties { get; set; }

        public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");

        public string Title
        {
            get => title;
            set => Set(ref title, value);
        }

        public string Subtitle
        {
            get => subtitle;
            set => Set(ref subtitle, value);
        }

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
                    Raise(nameof(DurationText));
                    Raise(nameof(IsActive));
                }
            }
        }

        public bool IsActive => Status == ActivityStatus.Running || Status == ActivityStatus.AwaitingApproval;

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case ActivityStatus.Running: return "Running";
                    case ActivityStatus.AwaitingApproval: return "Approval";
                    case ActivityStatus.Succeeded: return "OK";
                    case ActivityStatus.Failed: return "Failed";
                    case ActivityStatus.Cancelled: return "Cancelled";
                    default: return string.Empty;
                }
            }
        }

        public TimeSpan? Duration
        {
            get => duration;
            set { if (Set(ref duration, value)) { Raise(nameof(DurationText)); } }
        }

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

        public string AgentLabel => Agent?.IsMain != false ? "main" : Agent.Label;

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
            text != null && text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
