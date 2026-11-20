# 03 · Per-instance scope

## Problem

Every Visual Studio (and SSMS) instance stores its Copilot sessions in the same folder
(`%LOCALAPPDATA%\Microsoft\VisualStudio\CopilotCli\session-state`) and the session files do not
say which instance they belong to. With two IDEs open, each window would show the sessions of
both.

## Requirement

Each *Copilot Activity* window shows **only the sessions of the instance that hosts it**, unless
the user ticks **All instances**.

## How the instance is identified

1. **Host**: walk up the parent process chain of the extension (which runs in an out-of-process
   host that is a child of the IDE) up to the first `devenv.exe` (*Visual Studio*) or `Ssms.exe`
   (*SSMS*). If there is none (e.g. in tests), no scope is applied.
2. **The instance's Copilot**: look for the `copilot.exe` processes that descend from the host.
   Their log directory comes from the `--log-dir` command-line argument. If it cannot be read,
   look for `%TEMP%\*Copilot*\<id>\cli_logs` containing `process-*-<pid>*.log` files.
3. **Sessions**: read the `*.log` files of that directory incrementally and extract the session
   ids from:
   - `Received session.create request: {"sessionId":"<id>"…`
   - `Received session.resume request: {"sessionId":"<id>"…`
   - `"delivery_session_id":"<id>"`
4. The ids accumulate while the window is open (a session served once still belongs to the
   instance).

### Reading the logs

- Incremental, in 1 MB chunks, resuming after the last complete line break.
- Copilot rotates its logs (`process-….log` → `process-….1.log`…). The read position is keyed by
  a fingerprint of the file's first line, so a renamed file is not read again. If a file shrinks,
  it is read again from the start.
- Other GUIDs that appear in the logs are ignored.

## When no filtering is applied (*fail-open*)

Rather than hiding sessions that might belong to the instance, all of them are shown when:

- Copilot is running under the host but its logs cannot be found; or
- the instance has no Copilot of its own yet and there is an IDE-style `copilot.exe` (with
  `--log-dir`) that does not belong to any Visual Studio / SSMS (unknown launch model).

`copilot.exe` processes from other tools (e.g. the GitHub app runtime, without `--log-dir`) do not
affect the filter.

## UI

| Situation | Scope text | Sessions |
|-----------|------------|----------|
| Instance identified | `This Visual Studio (PID n)` / `This SSMS (PID n)` | Only the instance's |
| Copilot not started yet | `… · Copilot not started` | None; message suggesting to start a chat or tick *All instances* |
| Cannot be identified | `… · sessions not identified, showing all` | All |
| *All instances* ticked | `All instances` | All |
| Outside an IDE | (empty; *All instances* hidden) | All |

- When *All instances* changes, the list is rebuilt immediately. If the selected session is no
  longer listed, the most recent one in the list (or none) is selected.
- A session of another instance never stays selected while *All instances* is unchecked.

## Acceptance criteria

1. The host is detected from the ancestors and only the `copilot.exe` processes below it are
   considered. → `HostSessionScopeTests.DetectsTheIdeAncestorAndOnlyItsCopilotSessions`
2. SSMS is recognized; outside an IDE there is no scope. → `HostSessionScopeTests.DetectsSsmsAndReturnsNullOutsideAnIde`
3. Copilot not started → no sessions; Copilot without logs → no filter. →
   `HostSessionScopeTests.CopilotNotStartedYieldsNoSessionsAndUnknownLogsYieldNull`
4. An IDE-style Copilot without a parent IDE disables the filter only while the instance has none
   of its own; those from other tools are ignored. →
   `HostSessionScopeTests.IdeStyleCopilotThatBelongsToNoIdeDisablesFilteringOnlyWhileThisIdeHasNone`,
   `HostSessionScopeTests.IdeStyleCopilotProcessesOnThisMachineAreAttributedToAnIde`
5. Incremental reading with split lines and rotation. →
   `HostSessionScopeTests.ScannerReadsIncrementallyAndSurvivesRotation`
6. `--log-dir` with and without quotes and with `=`. → `HostSessionScopeTests.ParsesTheLogDirectoryFromTheCopilotCommandLine`
7. The window shows only the instance's sessions and *All instances* shows all of them, without
   leaving another instance's session selected. →
   `HostSessionScopeTests.ViewerShowsOnlyThisInstanceSessionsUnlessAllInstancesIsTicked`
8. Manual check: with two Visual Studio instances open and a chat in each, each window shows only
   its own session.
