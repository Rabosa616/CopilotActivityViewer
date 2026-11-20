using System.Text;
using System.Text.RegularExpressions;

namespace CopilotActivityViewer.Model;

/// <summary>
/// Incrementally reads the logs of a Copilot CLI process (<c>--log-dir</c>) and collects the ids of the sessions it serves.
/// Files are tracked by their first line, so the read position survives log rotation (renames).
/// </summary>
internal sealed partial class CopilotLogScanner
{
    private const int ChunkSize = 1 << 20;
    private const int Overlap = 256;
    private const int FingerprintBytes = 512;

    private readonly Dictionary<string, long> offsets = new(StringComparer.Ordinal);

    public CopilotLogScanner(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    /// <summary>Adds to <paramref name="sessionIds"/> every session id found in the log lines written since the last scan.</summary>
    public void Scan(ISet<string> sessionIds)
    {
        IEnumerable<string> files;
        try
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return;
            }

            files = System.IO.Directory.EnumerateFiles(Directory, "*.log").ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            try
            {
                ScanFile(file, sessionIds);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private void ScanFile(string path, ISet<string> sessionIds)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var fingerprint = Fingerprint(stream);
        if (fingerprint == null)
        {
            return;
        }

        offsets.TryGetValue(fingerprint, out var offset);
        var length = stream.Length;
        if (length < offset)
        {
            offset = 0;
        }

        if (length == offset)
        {
            return;
        }

        stream.Position = offset;
        var buffer = new byte[ChunkSize];
        var tail = string.Empty;
        var position = offset;
        var consumed = offset;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            // Latin1 maps bytes 1:1 to chars; the ids are ASCII so multi-byte UTF-8 elsewhere doesn't matter.
            var text = tail + Encoding.Latin1.GetString(buffer, 0, read);
            foreach (Match match in SessionPattern().Matches(text))
            {
                sessionIds.Add(match.Groups["id"].Value.ToLowerInvariant());
            }

            var lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
            if (lastNewline >= 0)
            {
                consumed = position + lastNewline + 1;
            }

            position += read;
            tail = text.Length > Overlap ? text[^Overlap..] : text;
        }

        // Resume after the last complete line: a line still being written is read again next time.
        offsets[fingerprint] = consumed;
    }

    private static string Fingerprint(FileStream stream)
    {
        var buffer = new byte[FingerprintBytes];
        var read = stream.Read(buffer, 0, buffer.Length);
        var newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
        return newline < 0 ? null : Encoding.Latin1.GetString(buffer, 0, newline);
    }

    [GeneratedRegex(@"(?:""delivery_session_id"":""|Received session\.(?:create|resume) request: \{""sessionId"":"")(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})")]
    private static partial Regex SessionPattern();
}
