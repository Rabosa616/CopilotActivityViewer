# Specifications

Functional specifications of **Copilot Activity Viewer**. They describe the expected behavior of
the window; when the code and a spec disagree, fix one or the other in the same change.

| Spec | Content |
|------|---------|
| [01-activity-window.md](01-activity-window.md) | The window: layout, controls, filters, empty states and live refresh |
| [02-formatted-details.md](02-formatted-details.md) | Details pane: sections, code highlighting, markdown, JSON and copy |
| [03-per-instance-scope.md](03-per-instance-scope.md) | How each window shows only the sessions of its own Visual Studio / SSMS instance |

## Conventions

- Everything (specs, docs, code comments and UI text) is written in English.
- Each spec ends with **acceptance criteria** and the tests that cover them
  (`tests\CopilotActivityViewer.Tests`).
- Implementation: `src\CopilotActivityViewer\ToolWindow\ActivityControl.xaml` (Remote UI) and
  `ActivityViewModel.cs`.
