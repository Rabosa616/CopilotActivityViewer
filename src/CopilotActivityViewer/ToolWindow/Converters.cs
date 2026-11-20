using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CopilotActivityViewer.Model;

namespace CopilotActivityViewer.ToolWindow
{
    /// <summary>Maps <see cref="AgentStatus"/>, <see cref="ActivityStatus"/> and <see cref="ActivityKind"/> values to theme-neutral accent brushes.</summary>
    public sealed class StatusBrushConverter : IValueConverter
    {
        private static readonly Brush Running = Freeze(0x3B, 0x8E, 0xEA);
        private static readonly Brush Waiting = Freeze(0xE0, 0xA0, 0x00);
        private static readonly Brush Success = Freeze(0x3F, 0xA3, 0x4D);
        private static readonly Brush Failure = Freeze(0xE5, 0x48, 0x4D);
        private static readonly Brush Neutral = Freeze(0x8C, 0x8C, 0x8C);
        private static readonly Brush Purple = Freeze(0x8A, 0x63, 0xD2);
        private static readonly Brush Orange = Freeze(0xD9, 0x77, 0x06);
        private static readonly Brush Teal = Freeze(0x0E, 0x9F, 0x6E);
        private static readonly Brush Slate = Freeze(0x5B, 0x6B, 0x7F);
        private static readonly Brush Gray = Freeze(0x6E, 0x76, 0x81);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value)
            {
                case AgentStatus agent:
                    switch (agent)
                    {
                        case AgentStatus.Running: return Running;
                        case AgentStatus.Completed: return Success;
                        case AgentStatus.Failed: return Failure;
                        case AgentStatus.Aborted: return Waiting;
                        default: return Neutral;
                    }

                case ActivityStatus activity:
                    switch (activity)
                    {
                        case ActivityStatus.Running: return Running;
                        case ActivityStatus.AwaitingApproval: return Waiting;
                        case ActivityStatus.Succeeded: return Success;
                        case ActivityStatus.Failed: return Failure;
                        case ActivityStatus.Cancelled: return Neutral;
                        default: return Brushes.Transparent;
                    }

                case ActivityKind kind:
                    switch (kind)
                    {
                        case ActivityKind.User: return Purple;
                        case ActivityKind.Assistant: return Running;
                        case ActivityKind.Tool: return Gray;
                        case ActivityKind.Subagent: return Orange;
                        case ActivityKind.Skill: return Teal;
                        case ActivityKind.Warning: return Waiting;
                        case ActivityKind.Error: return Failure;
                        case ActivityKind.Completion: return Success;
                        default: return Slate;
                    }

                default:
                    return Neutral;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

        private static Brush Freeze(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    public sealed class EmptyToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var visible = value is bool b ? b : value is int i ? i > 0 : !string.IsNullOrEmpty(value as string);
            if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
            {
                visible = !visible;
            }

            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Converts an agent depth into a left margin so the agent filter list looks like the tree.</summary>
    public sealed class DepthToIndentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            new Thickness((value is int depth ? depth : 0) * 14, 0, 0, 0);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
