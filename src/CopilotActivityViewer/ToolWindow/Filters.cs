using System;
using CopilotActivityViewer.Model;

namespace CopilotActivityViewer.ToolWindow
{
    public enum StatusFilter
    {
        All,
        Ok,
        Failed,
        Running,
        Cancelled,
    }

    public sealed class StatusFilterOption
    {
        public StatusFilterOption(StatusFilter value, string label)
        {
            Value = value;
            Label = label;
        }

        public StatusFilter Value { get; }

        public string Label { get; }

        public override string ToString() => Label;

        public static bool Matches(StatusFilter filter, ActivityItem item)
        {
            switch (filter)
            {
                case StatusFilter.Ok:
                    return item.Status == ActivityStatus.Succeeded || item.Kind == ActivityKind.Completion;
                case StatusFilter.Failed:
                    return item.Status == ActivityStatus.Failed || item.Kind == ActivityKind.Error;
                case StatusFilter.Running:
                    return item.IsActive;
                case StatusFilter.Cancelled:
                    return item.Status == ActivityStatus.Cancelled || (item.Kind == ActivityKind.Warning && item.Title == "Aborted");
                default:
                    return true;
            }
        }
    }

    /// <summary>Toggle chip for one <see cref="ActivityKind"/> in the activity filter bar.</summary>
    public sealed class KindFilter : ObservableObject
    {
        private bool isChecked = true;
        private int count;

        public KindFilter(ActivityKind kind)
        {
            Kind = kind;
            Label = new ActivityItem(DateTimeOffset.MinValue, null, kind).KindText;
        }

        public event EventHandler Changed;

        public ActivityKind Kind { get; }

        public string Label { get; }

        public bool IsChecked
        {
            get => isChecked;
            set
            {
                if (Set(ref isChecked, value))
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public int Count
        {
            get => count;
            set => Set(ref count, value);
        }

        internal void SetCheckedSilently(bool value)
        {
            if (isChecked != value)
            {
                isChecked = value;
                Raise(nameof(IsChecked));
            }
        }
    }
}
