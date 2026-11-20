using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CopilotActivityViewer.Formatting
{
    public enum CodeLanguage
    {
        Plain,
        CSharp,
        PowerShell,
        Bash,
        Python,
        JavaScript,
        Json,
        Xml,
        Sql,
        Yaml,
        Diff,
        Markdown,
    }

    public static class CodeLanguages
    {
        public static CodeLanguage FromName(string name)
        {
            switch ((name ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "cs":
                case "c#":
                case "csharp":
                    return CodeLanguage.CSharp;
                case "ps":
                case "ps1":
                case "pwsh":
                case "powershell":
                    return CodeLanguage.PowerShell;
                case "sh":
                case "bash":
                case "zsh":
                case "shell":
                case "console":
                    return CodeLanguage.Bash;
                case "py":
                case "python":
                    return CodeLanguage.Python;
                case "js":
                case "jsx":
                case "ts":
                case "tsx":
                case "javascript":
                case "typescript":
                    return CodeLanguage.JavaScript;
                case "json":
                case "jsonc":
                    return CodeLanguage.Json;
                case "xml":
                case "xaml":
                case "html":
                case "csproj":
                    return CodeLanguage.Xml;
                case "sql":
                case "tsql":
                    return CodeLanguage.Sql;
                case "yaml":
                case "yml":
                    return CodeLanguage.Yaml;
                case "diff":
                case "patch":
                    return CodeLanguage.Diff;
                case "md":
                case "markdown":
                    return CodeLanguage.Markdown;
                default:
                    return CodeLanguage.Plain;
            }
        }

        public static CodeLanguage FromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return CodeLanguage.Plain;
            }

            string extension;
            try
            {
                extension = Path.GetExtension(path.Trim().Trim('"')).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                return CodeLanguage.Plain;
            }

            switch (extension)
            {
                case ".cs":
                case ".csx":
                    return CodeLanguage.CSharp;
                case ".ps1":
                case ".psm1":
                case ".psd1":
                    return CodeLanguage.PowerShell;
                case ".sh":
                case ".bash":
                    return CodeLanguage.Bash;
                case ".py":
                    return CodeLanguage.Python;
                case ".js":
                case ".jsx":
                case ".mjs":
                case ".cjs":
                case ".ts":
                case ".tsx":
                    return CodeLanguage.JavaScript;
                case ".json":
                    return CodeLanguage.Json;
                case ".xml":
                case ".xaml":
                case ".csproj":
                case ".vbproj":
                case ".fsproj":
                case ".props":
                case ".targets":
                case ".vsct":
                case ".vsixmanifest":
                case ".config":
                case ".resx":
                case ".nuspec":
                case ".html":
                case ".htm":
                case ".svg":
                    return CodeLanguage.Xml;
                case ".sql":
                    return CodeLanguage.Sql;
                case ".yaml":
                case ".yml":
                    return CodeLanguage.Yaml;
                case ".diff":
                case ".patch":
                    return CodeLanguage.Diff;
                case ".md":
                    return CodeLanguage.Markdown;
                default:
                    return CodeLanguage.Plain;
            }
        }

        /// <summary>Language of the script argument of shell-like tools (powershell, bash, python…).</summary>
        public static CodeLanguage FromToolName(string toolName)
        {
            var name = (toolName ?? string.Empty).ToLowerInvariant();
            if (name.Contains("powershell") || name.Contains("pwsh"))
            {
                return CodeLanguage.PowerShell;
            }

            if (name.Contains("bash") || name.Contains("shell") || name.Contains("terminal"))
            {
                return CodeLanguage.Bash;
            }

            if (name.Contains("python"))
            {
                return CodeLanguage.Python;
            }

            if (name.Contains("sql") || name.Contains("kusto"))
            {
                return CodeLanguage.Sql;
            }

            return CodeLanguage.Plain;
        }

        /// <summary>Best-effort detection for text without any other hint (tool results).</summary>
        public static CodeLanguage Guess(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return CodeLanguage.Plain;
            }

            var trimmed = text.Trim();
            if ((trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal)) ||
                (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)))
            {
                if (TryFormatJson(trimmed, out _))
                {
                    return CodeLanguage.Json;
                }
            }

            if (trimmed.StartsWith("<", StringComparison.Ordinal) && (trimmed.Contains("</") || trimmed.Contains("/>")))
            {
                return CodeLanguage.Xml;
            }

            if (trimmed.StartsWith("diff --git", StringComparison.Ordinal) || trimmed.StartsWith("@@ ", StringComparison.Ordinal) ||
                (trimmed.StartsWith("--- ", StringComparison.Ordinal) && trimmed.Contains("\n+++ ")))
            {
                return CodeLanguage.Diff;
            }

            return CodeLanguage.Plain;
        }

        public static bool TryFormatJson(string text, out string formatted)
        {
            formatted = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                formatted = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })
                    ?.ToJsonString(IndentedJson) ?? "null";
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static readonly JsonSerializerOptions IndentedJson = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }
}
