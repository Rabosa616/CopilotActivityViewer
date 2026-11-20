using System;
using System.Collections.ObjectModel;
using System.Text;

namespace CopilotActivityViewer.Model
{
    public enum AgentStatus
    {
        Idle,
        Running,
        Completed,
        Failed,
        Aborted,
    }

    public sealed class AgentNode : ObservableObject
    {
        private string name;
        private string displayName;
        private string description;
        private string agentType;
        private string model;
        private string executionMode;
        private AgentStatus status;
        private DateTimeOffset? startTime;
        private DateTimeOffset? endTime;
        private int toolCalls;
        private int runningTools;
        private long inputTokens;
        private long outputTokens;
        private long? reportedTotalTokens;
        private string currentActivity;
        private bool isExpanded = true;

        public AgentNode(string id, AgentNode parent)
        {
            Id = id ?? string.Empty;
            Parent = parent;
        }

        public string Id { get; }

        public AgentNode Parent { get; internal set; }

        public bool IsMain => Id.Length == 0;

        public int Depth
        {
            get
            {
                var depth = 0;
                for (var current = Parent; current != null && depth < 32; current = current.Parent)
                {
                    depth++;
                }

                return depth;
            }
        }

        public ObservableCollection<AgentNode> Children { get; } = new ObservableCollection<AgentNode>();

        internal bool LastMessageHadToolRequests { get; set; }

        public string Name
        {
            get => name;
            set { if (Set(ref name, value)) { Raise(nameof(Label)); } }
        }

        public string DisplayName
        {
            get => displayName;
            set { if (Set(ref displayName, value)) { Raise(nameof(Label)); } }
        }

        public string Label => !string.IsNullOrEmpty(DisplayName) ? DisplayName : (!string.IsNullOrEmpty(Name) ? Name : ShortId);

        public string ShortId => Id.Length > 8 ? Id.Substring(0, 8) : Id;

        public string Description
        {
            get => description;
            set => Set(ref description, value);
        }

        public string AgentType
        {
            get => agentType;
            set { if (Set(ref agentType, value)) { Raise(nameof(Subtitle)); } }
        }

        public string Model
        {
            get => model;
            set { if (Set(ref model, value)) { Raise(nameof(Subtitle)); } }
        }

        public string ExecutionMode
        {
            get => executionMode;
            set { if (Set(ref executionMode, value)) { Raise(nameof(Subtitle)); } }
        }

        public AgentStatus Status
        {
            get => status;
            set
            {
                if (Set(ref status, value))
                {
                    Raise(nameof(StatusText));
                    Raise(nameof(IsRunning));
                    Raise(nameof(Stats));
                }
            }
        }

        public bool IsRunning => Status == AgentStatus.Running;

        public string StatusText => Status.ToString();

        public DateTimeOffset? StartTime
        {
            get => startTime;
            set { if (Set(ref startTime, value)) { Raise(nameof(Stats)); } }
        }

        public DateTimeOffset? EndTime
        {
            get => endTime;
            set { if (Set(ref endTime, value)) { Raise(nameof(Stats)); } }
        }

        public int ToolCalls
        {
            get => toolCalls;
            set { if (Set(ref toolCalls, value)) { Raise(nameof(Stats)); } }
        }

        public int RunningTools
        {
            get => runningTools;
            set => Set(ref runningTools, Math.Max(0, value));
        }

        public long InputTokens
        {
            get => inputTokens;
            set { if (Set(ref inputTokens, value)) { Raise(nameof(Stats)); } }
        }

        public long OutputTokens
        {
            get => outputTokens;
            set { if (Set(ref outputTokens, value)) { Raise(nameof(Stats)); } }
        }

        public long? ReportedTotalTokens
        {
            get => reportedTotalTokens;
            set { if (Set(ref reportedTotalTokens, value)) { Raise(nameof(Stats)); } }
        }

        public string CurrentActivity
        {
            get => currentActivity;
            set => Set(ref currentActivity, value);
        }

        public bool IsExpanded
        {
            get => isExpanded;
            set => Set(ref isExpanded, value);
        }

        public string Subtitle
        {
            get
            {
                var sb = new StringBuilder();
                Append(sb, AgentType);
                Append(sb, Model);
                if (!string.IsNullOrEmpty(ExecutionMode) && ExecutionMode != "sync")
                {
                    Append(sb, ExecutionMode);
                }

                return sb.ToString();
            }
        }

        public string Stats
        {
            get
            {
                var sb = new StringBuilder();
                Append(sb, ToolCalls == 1 ? "1 tool" : ToolCalls + " tools");
                var tokens = ReportedTotalTokens ?? (InputTokens + OutputTokens);
                if (tokens > 0)
                {
                    Append(sb, TextFormat.Tokens(tokens) + " tokens");
                }

                var duration = Duration;
                if (duration.HasValue)
                {
                    Append(sb, TextFormat.Duration(duration.Value));
                }

                return sb.ToString();
            }
        }

        public TimeSpan? Duration
        {
            get
            {
                if (!StartTime.HasValue || IsMain)
                {
                    return null;
                }

                var end = EndTime ?? (IsRunning ? DateTimeOffset.UtcNow : (DateTimeOffset?)null);
                return end.HasValue ? end.Value - StartTime.Value : (TimeSpan?)null;
            }
        }

        public bool IsSelfOrAncestorOf(AgentNode other)
        {
            for (var current = other; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, this))
                {
                    return true;
                }
            }

            return false;
        }

        internal void RaiseTimeDependent() => Raise(nameof(Stats));

        private static void Append(StringBuilder sb, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            if (sb.Length > 0)
            {
                sb.Append(" · ");
            }

            sb.Append(value);
        }
    }
}
