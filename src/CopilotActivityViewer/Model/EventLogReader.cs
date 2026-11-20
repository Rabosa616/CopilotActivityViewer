using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CopilotActivityViewer.Model
{
    /// <summary>
    /// Incrementally reads complete lines appended to a file that another process is still writing.
    /// </summary>
    public sealed class EventLogReader
    {
        private readonly List<byte> pending = new List<byte>();
        private long position;

        public EventLogReader(string path)
        {
            Path = path;
        }

        public string Path { get; }

        /// <summary>True when the last read detected that the file was truncated or replaced and restarted from the beginning.</summary>
        public bool WasReset { get; private set; }

        public IReadOnlyList<string> ReadNewLines()
        {
            WasReset = false;
            var lines = new List<string>();
            var info = new FileInfo(Path);
            if (!info.Exists)
            {
                return lines;
            }

            if (info.Length < position)
            {
                position = 0;
                pending.Clear();
                WasReset = true;
            }

            if (info.Length == position)
            {
                return lines;
            }

            byte[] buffer;
            using (var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                stream.Seek(position, SeekOrigin.Begin);
                buffer = new byte[stream.Length - position];
                var total = 0;
                while (total < buffer.Length)
                {
                    var read = stream.Read(buffer, total, buffer.Length - total);
                    if (read <= 0)
                    {
                        break;
                    }

                    total += read;
                }

                position += total;
                if (total < buffer.Length)
                {
                    Array.Resize(ref buffer, total);
                }
            }

            var start = 0;
            for (var i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] != (byte)'\n')
                {
                    continue;
                }

                pending.AddRange(new ArraySegment<byte>(buffer, start, i - start));
                AddLine(lines);
                start = i + 1;
            }

            if (start < buffer.Length)
            {
                pending.AddRange(new ArraySegment<byte>(buffer, start, buffer.Length - start));
            }

            return lines;
        }

        private void AddLine(List<string> lines)
        {
            var line = Encoding.UTF8.GetString(pending.ToArray()).TrimEnd('\r');
            pending.Clear();
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }
    }
}
