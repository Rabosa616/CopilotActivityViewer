using System;
using System.Globalization;

namespace CopilotActivityViewer.Model
{
    internal static class TextFormat
    {
        public static string Duration(TimeSpan value)
        {
            if (value < TimeSpan.Zero)
            {
                value = TimeSpan.Zero;
            }

            if (value.TotalSeconds < 60)
            {
                return value.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
            }

            if (value.TotalMinutes < 60)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}m {1:00}s", (int)value.TotalMinutes, value.Seconds);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}h {1:00}m", (int)value.TotalHours, value.Minutes);
        }

        public static string Tokens(long value)
        {
            if (value >= 1_000_000)
            {
                return (value / 1_000_000d).ToString("0.0", CultureInfo.InvariantCulture) + "M";
            }

            if (value >= 1_000)
            {
                return (value / 1_000d).ToString("0.0", CultureInfo.InvariantCulture) + "k";
            }

            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string FirstLine(string text, int maxLength = 160)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length > 0)
                {
                    return Truncate(line, maxLength);
                }
            }

            return string.Empty;
        }

        public static string Truncate(string text, int maxLength)
        {
            if (text == null || text.Length <= maxLength)
            {
                return text ?? string.Empty;
            }

            return text.Substring(0, maxLength) + "…";
        }
    }
}
