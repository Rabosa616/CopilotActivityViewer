# 03 · Abast per instància

## Problema

Totes les instàncies de Visual Studio (i d'SSMS) desen les sessions de Copilot a la mateixa
carpeta (`%LOCALAPPDATA%\Microsoft\VisualStudio\CopilotCli\session-state`) i els fitxers de
sessió no indiquen a quina instància pertanyen. Amb dos IDE oberts, cada finestra veuria les
sessions de tots dos.

## Requisit

Cada finestra *Copilot Activity* mostra **només les sessions de la instància que l'allotja**,
tret que l'usuari marqui **All instances**.

## Com s'identifica la instància

1. **Host**: es recorre la cadena de processos pares de l'extensió (que s'executa en un host
   *out-of-process* fill de l'IDE) fins al primer `devenv.exe` (*Visual Studio*) o `Ssms.exe`
   (*SSMS*). Si no n'hi ha cap (p. ex. als tests), no s'aplica cap abast.
2. **Copilot de la instància**: es busquen els processos `copilot.exe` descendents del host.
   El seu directori de logs s'obté de l'argument `--log-dir` de la línia d'ordres. Si no s'hi
   pot accedir, es busca `%TEMP%\*Copilot*\<id>\cli_logs` amb fitxers `process-*-<pid>*.log`.
3. **Sessions**: es llegeixen els logs `*.log` d'aquest directori de manera incremental i
   s'extreuen els identificadors de sessió de:
   - `Received session.create request: {"sessionId":"<id>"…`
   - `Received session.resume request: {"sessionId":"<id>"…`
   - `"delivery_session_id":"<id>"`
4. Els identificadors s'acumulen mentre la finestra està oberta (una sessió servida una
   vegada continua sent de la instància).

### Lectura dels logs

- Incremental, per blocs d'1 MB, reprenent després de l'últim salt de línia complet.
- Copilot fa rotació dels logs (`process-….log` → `process-….1.log`…). La posició de lectura
  es guarda per l'empremta de la primera línia del fitxer, de manera que un fitxer reanomenat
  no es torna a llegir. Si un fitxer s'escurça, es torna a llegir des del principi.
- Altres GUID que apareguin als logs no es tenen en compte.

## Quan no es filtra (*fail-open*)

Abans que amagar sessions que podrien ser de la instància, es mostren totes si:

- Copilot s'està executant sota el host però no se'n troben els logs; o
- la instància encara no té cap Copilot propi i hi ha un `copilot.exe` amb `--log-dir` (estil
  IDE) que no penja de cap Visual Studio / SSMS (model d'arrencada desconegut).

Els `copilot.exe` d'altres eines (p. ex. el runtime de l'app de GitHub, sense `--log-dir`) no
afecten el filtre.

## UI

| Situació | Text d'abast | Sessions |
|----------|--------------|----------|
| Instància identificada | `This Visual Studio (PID n)` / `This SSMS (PID n)` | Només les de la instància |
| Copilot encara no ha arrencat | `… · Copilot not started` | Cap; missatge per iniciar un xat o marcar *All instances* |
| No es poden identificar | `… · sessions not identified, showing all` | Totes |
| *All instances* marcat | `All instances` | Totes |
| Fora d'un IDE | (buit; *All instances* amagat) | Totes |

- En canviar *All instances*, la llista es refà a l'instant. Si la sessió seleccionada ja no
  hi és, es selecciona la més recent de la llista (o cap).
- Una sessió d'una altra instància mai queda seleccionada amb *All instances* desmarcat.

## Criteris d'acceptació

1. El host es detecta pels avantpassats i només es tenen en compte els `copilot.exe` que en
   pengen. → `HostSessionScopeTests.DetectsTheIdeAncestorAndOnlyItsCopilotSessions`
2. SSMS es reconeix; fora d'un IDE no hi ha abast. → `HostSessionScopeTests.DetectsSsmsAndReturnsNullOutsideAnIde`
3. Copilot no iniciat → cap sessió; Copilot sense logs → sense filtre. →
   `HostSessionScopeTests.CopilotNotStartedYieldsNoSessionsAndUnknownLogsYieldNull`
4. Un Copilot estil IDE sense IDE pare desactiva el filtre només mentre la instància no en té
   cap de propi; els d'altres eines s'ignoren. →
   `HostSessionScopeTests.IdeStyleCopilotThatBelongsToNoIdeDisablesFilteringOnlyWhileThisIdeHasNone`,
   `HostSessionScopeTests.IdeStyleCopilotProcessesOnThisMachineAreAttributedToAnIde`
5. Lectura incremental amb línies partides i rotació. →
   `HostSessionScopeTests.ScannerReadsIncrementallyAndSurvivesRotation`
6. `--log-dir` amb i sense cometes i amb `=`. → `HostSessionScopeTests.ParsesTheLogDirectoryFromTheCopilotCommandLine`
7. La finestra mostra només les sessions de la instància i *All instances* les mostra totes,
   sense deixar seleccionada una sessió aliena. →
   `HostSessionScopeTests.ViewerShowsOnlyThisInstanceSessionsUnlessAllInstancesIsTicked`
8. Verificació manual: amb dues instàncies de Visual Studio obertes i un xat a cadascuna, cada
   finestra mostra només la seva sessió.
