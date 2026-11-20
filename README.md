# Copilot Activity Viewer

Extensió de Visual Studio (2022 / 2026) que afegeix la finestra **Copilot Activity**
(View > Other Windows > Copilot Activity) per veure en directe què fa l'agent de
GitHub Copilot i els seus sub-agents.

- **Arbre d'agents**: agent principal i sub-agents (també niats), amb estat, model,
  nombre d'eines, tokens i durada.
- **Línia de temps**: missatges, crides a eines (amb arguments, resultat, durada i
  aprovacions), errors, avisos, skills, compactacions i `task_complete`.
- **Filtres**:
  - per tipus (AI, Tool, User, Sub-agent, Skill, Warning, Error…) amb xips de colors que mostren el recompte. Clic per amagar o mostrar el tipus; Ctrl+clic o clic dret per veure només aquell tipus.
  - per estat (OK, fallat/errors, en curs, cancel·lat).
  - per agent, des d'un desplegable jeràrquic o des de l'arbre, incloent-hi o no els sub-agents.
  - per text lliure, que també cerca dins dels arguments i dels resultats.
  - el botó *Clear filters* ho restableix tot.
- **Detall formatat**:
  - scripts i codi amb ressaltat de sintaxi (PowerShell, bash, C#, Python, JS/TS, JSON, XML/XAML, SQL, YAML, diff).
  - el JSON es mostra indentat.
  - els missatges de l'AI i dels sub-agents es mostren com a markdown (títols, llistes, taules, blocs de codi).
  - els errors surten en vermell.
  - botó *Copy* per copiar el text cru.

La finestra llegeix incrementalment els fitxers `events.jsonl` de
`%LOCALAPPDATA%\Microsoft\VisualStudio\CopilotCli\session-state` i
`%USERPROFILE%\.copilot\session-state`. Aquest format és intern de Copilot i pot
canviar; el lector ignora els esdeveniments i camps desconeguts.

## Compilar

Cal MSBuild de Visual Studio (les tasques VSSDK no funcionen amb `dotnet build`):

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\amd64\MSBuild.exe" CopilotActivityViewer.sln /restore /p:Configuration=Release
dotnet test tests\CopilotActivityViewer.Tests\CopilotActivityViewer.Tests.csproj --no-build -c Release
```

El paquet queda a `src\CopilotActivityViewer\bin\Release\net472\CopilotActivityViewer.vsix`.

## Instal·lar

Tanca Visual Studio i fes doble clic al `.vsix`.
