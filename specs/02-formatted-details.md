# 02 · Formatted details

## Goal

Make the content of each activity (scripts, code, JSON, markdown answers, errors) readable
without copying it into another editor.

## Structure

1. **Title** of the activity (larger, bold).
2. **Metadata**: kind, agent, tool, time, duration, status, approvals.
3. **Arguments** (tools) or **Task** (sub-agents).
4. **Result**, or **Error** in red when it failed.
5. For AI, user and sub-agent messages: the text rendered as markdown.

The text of each section is limited to 12,000 characters so the UI is never blocked.

## Code and scripts

- Syntax highlighting for PowerShell, bash, C#, Python, JavaScript/TypeScript, JSON, XML/XAML,
  SQL, YAML and diff.
- The language is detected, in order, from: the tool name (`powershell`, `bash`…), the file
  extension in the arguments (`path`, `filePath`…), the markdown code fence tag and, finally,
  the content.
- Code blocks use a monospaced font, a distinct background and no forced wrapping (they can be
  scrolled).
- Token colors work with both light and dark themes.

## JSON

- JSON arguments and results are pretty-printed.
- Well-known arguments are shown as name and value (e.g. `command`, `path`, `description`); the
  rest as JSON.
- Name/value tables (tool arguments, sub-agent Agent/Type/Model/Description) keep each value
  next to its name and wrap it across the remaining width of the pane.

## Markdown

Headings, paragraphs, bold, italics, inline code, links, lists (also nested), quotes, tables and
highlighted code blocks.

## Robustness

- XAML special characters (`{`, `}`, `<`, `>`, `&`) and control characters are shown literally;
  they never break the document.
- Emojis and non-Latin characters are displayed correctly.

## Copy

The **Copy** button (in the **Details** header, above the document, so it never covers the text)
copies the raw text of the activity to the clipboard.

## Acceptance criteria

1. A PowerShell script and a JSON result are highlighted, with the JSON indented, using the
   palette colors. → `DetailsAndFiltersTests.DetailsXamlHighlightsScriptsAndPrettyPrintsJsonResults`
2. Text with braces, XAML tags, `&amp;`, control characters and emojis is shown as is. →
   `DetailsAndFiltersTests.DetailsXamlEscapesMarkupBracesAndControlCharacters`
3. PowerShell, C#, JSON, XML and diff tokens are classified correctly and languages are detected
   from names, paths, tools and content. → `FormattingTests`
4. Markdown (headings, lists, tables, blocks) is parsed into blocks and inlines. →
   `FormattingTests.MarkdownBlocksAndInlinesAreParsed`
5. In name/value tables the value starts next to its name and a short value stays on one line. →
   `DetailsAndFiltersTests.KeyValueTablesKeepValuesNextToTheirKeysOnOneLine`
