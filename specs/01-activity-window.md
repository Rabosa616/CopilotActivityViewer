# 01 · *Copilot Activity* window

## Goal

See, live, what the GitHub Copilot agent and its sub-agents are doing inside Visual Studio 2026
or SQL Server Management Studio 22: which agents exist, what they are doing, which tools they
call, with which arguments and with which result.

## Opening

- Menus **View > Other Windows > Copilot Activity** and **Tools > Copilot Activity**
  (*ActivityDiagram* icon).
- Opens in the document well. Each IDE instance has its own window and its own state; opening it
  in one instance does not affect the others.
- On opening, the most recent session of the instance is loaded (see
  [03-per-instance-scope.md](03-per-instance-scope.md)).

## Layout

```
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│ Session [ VS · [Repo] session title ▾ ]  This Visual Studio (PID n) ☐All instances ☑Follow latest [Refresh] │
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│ ● Summary: status · model · agents · tools · tokens · last activity                          │
├───────────────────────┬──────────────────────────────────────────────────────────────────────┤
│ Agents                │ Activity  n of m [Show older] [Search…] [Status▾] [Agent▾] ☑Include sub-agents │
│ ● main      Running   │                                         [Clear filters] ☑Newest first │
│   model · duration    │ (USER 3) (AI 40) (TOOL 120) (AGENT 4) (SKILL 1) (WARN 2) (ERROR 1) … │
│   tools · tokens      │ ┌──────────────────────────────────────────────────────────────────┐ │
│   ↳ current activity  │ │ time │ KIND │ agent │ title + subtitle         │ ● status │ duration│ │
│   ● sub-agent  Idle   │ │ …                                                                │ │
│     …                 │ └──────────────────────────────────────────────────────────────────┘ │
│                       │ ═══════════════════ (resizable splitter) ═══════════════════         │
│                       │ Details                                                       [Copy] │
│                       │ ┌ Formatted details ──────────────────────────────────────────────┐ │
│                       │ └──────────────────────────────────────────────────────────────────┘ │
└───────────────────────┴──────────────────────────────────────────────────────────────────────┘
```

- The agents column (300 px, minimum 160) and the list/details area are resizable with
  splitters. The details pane starts at 280 px high.
- Colors and fonts follow the active IDE theme (light, dark, blue…).

## Session bar

| Control | Behavior |
|---------|----------|
| **Session** (drop-down) | Sessions from newest to oldest (at most 100). Text: `<source> · [<repository or folder>] <title>`; the tooltip shows the `events.jsonl` path. |
| Scope text | The monitored instance, e.g. `This Visual Studio (PID 39400)`; see spec 03. |
| **All instances** | Also shows the sessions of the other instances. Only visible when the extension knows which instance hosts it. Unchecked by default. |
| **Follow latest** | Checked by default. Automatically switches to the most recent session when the current one has been idle for more than 15 s and a newer one exists. |
| **Refresh** | Searches for sessions again and reloads the selected one. Disabled while running. |

## Summary

A colored dot for the status of the main agent (running, idle, error) and a line with: status,
current model, number of agents and tools, input/output tokens and the time of the last event.
The tooltip contains the full summary.

## Agents

- Hierarchical list: the main agent (`main`) and its sub-agents, indented by depth (nested
  sub-agents included).
- For each agent: status dot, name, status (*Running*, *Idle*, *Completed*, *Failed*…),
  type/model, statistics (tools, tokens, duration) and, if any, the current activity in blue
  italics.
- Selecting an agent filters the timeline to that agent (same as the *Agent* drop-down).

## Timeline (Activity)

Each row: time · colored kind badge · agent · title (bold) + subtitle (gray) · status with a
colored dot · duration.

| Kind | Badge | Source |
|------|-------|--------|
| User | `USER` | User messages |
| Assistant | `AI` | Messages from the agent or the sub-agents |
| Tool | `TOOL` | Tool calls (arguments, result, duration, approvals) |
| Subagent | `AGENT` | Sub-agent start and end |
| Skill | `SKILL` | Invoked skills |
| Notification | `NOTIFY` | System notifications, compactions… |
| Warning | `WARN` | Warnings, cancellations (*Aborted*) |
| Error | `ERROR` | Session errors |
| Completion | `DONE` | `task_complete` |

Activity statuses: *Running*, *Approval* (waiting for permission), *OK*, *Failed*, *Cancelled*.

### Order

- **Newest first** (default): most recent on top. Unchecked: chronological order.
- New activity is added without losing the selection or the scroll position.

### Capped list

Every visible row is an object that Remote UI marshals to the IDE, so sending all of them for
long sessions (thousands of events) made the window very slow to open.

- Only the **250 most recent activities** that match the filters are listed, both with
  *Newest first* and in chronological order.
- The count says so: `2318 events · latest 250 shown`.
- The **Show N older** button (only visible when some are hidden) adds 500 more without
  replacing the list.
- Filters and search always work on the **whole** session. Changing a filter or the session
  resets the cap to 250.

### Filters

All of them are combined (logical AND). The `n of m` text shows the visible activities against
the total.

| Filter | Behavior |
|--------|----------|
| **Kind chips** | One chip per kind present in the session, with its count. Click: show/hide the kind. Right-click: only that kind; right-clicking the same chip again shows every kind. |
| **Status** | *Any status*, *OK* (success or `DONE`), *Failed / errors* (failures or `ERROR`), *Running* (running or waiting for approval), *Cancelled*. |
| **Agent** | Hierarchical drop-down kept in sync with the agents list. |
| **Include sub-agents** | Checked by default: includes the activity of the agents started by the selected agent. |
| **Search** | Free text, case-insensitive, with a 250 ms delay. Searches title, subtitle, tool, agent and also inside arguments and results. |
| **Clear filters** | Resets every filter. Only enabled when a filter is applied. |

## Details

Shows the selected activity formatted; see [02-formatted-details.md](02-formatted-details.md).
The pane has a **Details** header with the **Copy** button, so the button never covers the text.
With no selection it shows "Select an activity to see its details." and **Copy** is hidden.

The hint "Select an agent to show only its activity" is the tooltip of the **Agents** header, not of
the whole list, so it does not float over the empty part of the list.

## Live refresh

- Every second only the new lines of `events.jsonl` are read (incremental reading; incomplete
  lines wait until they are complete).
- Every 5 seconds sessions are searched again.
- If the file shrinks or is replaced, the session is fully reloaded.
- Durations of running activities advance live.
- Unknown events or fields are ignored (the format is internal to Copilot).

## Status messages

Shown centered over the content:

- No sessions in the instance: *No Copilot agent sessions in this Visual Studio (PID n) yet.*
  with a hint to start a chat or tick *All instances*.
- No sessions anywhere: the list of folders that were searched.
- Read errors: *Could not scan sessions…* / *Could not read events…*

They disappear automatically when a session shows up.

## Acceptance criteria

1. With a session with nested sub-agents, the agents list has `main` as root and every started
   sub-agent appears under its parent. → `LocalSessionsTests.AllLocalSessionsParseAndProduceConsistentAgentTrees`,
   `SessionModelTests.BuildsNestedAgentTreeAndToolLifecycle`
2. The real Remote UI template renders with real data: bound lists, kind chips and details. →
   `LocalSessionsTests.RemoteUiTemplateRendersRealSession`
3. Kind, solo, status, agent and search filters narrow the list correctly and *Clear filters*
   restores it. → `DetailsAndFiltersTests.KindStatusAgentAndSearchFiltersNarrowTheActivityList`
4. New lines are merged without reloading; *Newest first* puts them on top; the details follow
   the selection. → `DetailsAndFiltersTests.NewEventsAreMergedNewestFirstAndDetailsFollowTheSelection`
5. Partial lines and split multibyte characters are not processed until they are complete. →
   `EventLogAndCatalogTests.ReaderReturnsOnlyCompleteLinesIncludingSplitMultibyteCharacters`
6. In a session with more than 250 activities only the latest 250 are listed. Search finds old
   activities, and *Show older* adds the earlier ones without rebuilding the list and survives
   live updates. → `DetailsAndFiltersTests.LongSessionsListOnlyTheLatestActivityUntilOlderIsRequested`
7. Manual check: the window opens quickly with a session larger than 10 MB.
