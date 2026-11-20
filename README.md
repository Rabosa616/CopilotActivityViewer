# Copilot Activity Viewer

[![CI](https://github.com/Rabosa616/CopilotActivityViewer/actions/workflows/ci.yml/badge.svg)](https://github.com/Rabosa616/CopilotActivityViewer/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Rabosa616/CopilotActivityViewer)](https://github.com/Rabosa616/CopilotActivityViewer/releases/latest)

Extensió per a **Visual Studio 2026** i **SQL Server Management Studio 22** que afegeix la
finestra **Copilot Activity** (View > Other Windows > Copilot Activity, o Tools > Copilot Activity)
per veure en directe què fa l'agent de GitHub Copilot i els seus sub-agents.

- **Agents**: agent principal i sub-agents (també niats), amb estat, model, nombre d'eines,
  tokens i durada.
- **Línia de temps**: missatges, crides a eines (amb arguments, resultat, durada i
  aprovacions), errors, avisos, skills, compactacions i `task_complete`. Per defecte les més
  recents surten a dalt (*Newest first*).
- **Filtres**:
  - per tipus (AI, Tool, User, Sub-agent, Skill, Warning, Error…) amb xips de colors que mostren
    el recompte. Clic per amagar o mostrar el tipus; clic dret per veure només aquell tipus.
  - per estat (OK, fallat/errors, en curs, cancel·lat).
  - per agent, des del desplegable o des de la llista d'agents, incloent-hi o no els sub-agents.
  - per text lliure, que també cerca dins dels arguments i dels resultats.
  - el botó *Clear filters* ho restableix tot.
- **Detall formatat**:
  - scripts i codi amb ressaltat de sintaxi (PowerShell, bash, C#, Python, JS/TS, JSON,
    XML/XAML, SQL, YAML, diff).
  - el JSON es mostra indentat.
  - els missatges de l'AI i dels sub-agents es mostren com a markdown (títols, llistes, taules,
    blocs de codi).
  - els errors surten en vermell.
  - botó *Copy* per copiar el text cru.

Les especificacions funcionals de la finestra són a [`specs/`](specs/README.md).

## Una finestra per instància

Totes les instàncies de Visual Studio i SSMS desen les sessions al mateix lloc, però cada
finestra **només mostra les sessions de la seva pròpia instància**:

1. l'extensió busca el procés `devenv.exe` / `Ssms.exe` que l'allotja;
2. troba el `copilot.exe` que aquella instància ha arrencat i el seu directori de logs
   (`--log-dir`);
3. llegeix aquests logs incrementalment (també després de la rotació) per saber quines
   sessions serveix.

A dalt a la dreta es mostra la instància monitoritzada (p. ex. *This Visual Studio (PID 39400)*).
Marca **All instances** per veure les sessions de totes les instàncies. Si encara no s'ha obert
cap xat de Copilot en aquella instància, la finestra ho indica. Si no es poden identificar les
sessions (per exemple, si Copilot canvia el format dels logs), es mostren totes.

## Tecnologia

- [VisualStudio.Extensibility](https://learn.microsoft.com/visualstudio/extensibility/visualstudio.extensibility/)
  *out-of-process* sobre **.NET 10**. L'extensió s'executa fora del procés de l'IDE, de manera que
  no el pot bloquejar ni fer-lo caure.
- UI amb **Remote UI**: `ToolWindow\ActivityControl.xaml` (un `DataTemplate` sense code-behind)
  enllaçat a `ActivityViewModel`. El detall es genera com a `FlowDocument` XAML
  (`DetailsXamlBuilder`).
- `System.Text.Json` per llegir els `events.jsonl`.

La finestra llegeix incrementalment els fitxers `events.jsonl` de
`%LOCALAPPDATA%\Microsoft\VisualStudio\CopilotCli\session-state` (i també dels directoris
equivalents d'SSMS, d'altres hosts i de `%USERPROFILE%\.copilot\session-state`). Aquest format és
intern de Copilot i pot canviar; el lector ignora els esdeveniments i camps desconeguts.

## Compilar i provar

Cal el SDK de .NET 10 o posterior:

```powershell
dotnet build CopilotActivityViewer.slnx -c Release
dotnet test CopilotActivityViewer.slnx -c Release --no-build
```

El paquet queda a `src\CopilotActivityViewer\bin\Release\net10.0-windows8.0\CopilotActivityViewer.vsix`.

## Instal·lar

Requereix Visual Studio 2026 18.12 o posterior, o SSMS 22.

1. Si tens instal·lada la versió 1.x (VSSDK), desinstal·la-la primer des de
   Extensions > Manage Extensions.
2. Tanca l'IDE i fes doble clic al `.vsix`. Per a SSMS també pots fer servir
   `"C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe" CopilotActivityViewer.vsix`.

El `.vsix` de cada versió es pot descarregar de
[Releases](https://github.com/Rabosa616/CopilotActivityViewer/releases).

## Publicar

Tot està automatitzat amb GitHub Actions:

- **CI** (`.github/workflows/ci.yml`): a cada *push* a `master` i a cada *pull request*
  compila, executa els tests i deixa el `.vsix` com a artefacte.
- **Release** (`.github/workflows/release.yml`): en pujar un tag `vX.Y.Z`, compila amb aquella
  versió, executa els tests, crea la *release* de GitHub amb el `.vsix` i, si existeix el
  secret `VS_MARKETPLACE_PAT`, el publica al Visual Studio Marketplace amb `VsixPublisher.exe`
  (paquet NuGet `Microsoft.VSSDK.BuildTools`).

```powershell
git tag v2.1.0
git push origin v2.1.0
```

Configuració inicial del Marketplace (una sola vegada):

1. Crea un *publisher* a <https://marketplace.visualstudio.com/manage> amb l'ID `rabosa616` i
   el nom visible `Oscar Vaquero` (ha de coincidir amb `publisherName` de
   `CopilotActivityViewerExtension.cs`; si tries uns altres valors, actualitza aquest fitxer i
   `marketplace/publishManifest.json`).
2. Crea un *Personal Access Token* a <https://dev.azure.com> amb **All accessible
   organizations** i l'àmbit **Marketplace > Manage**.
3. Desa'l com a secret del repositori:
   `gh secret set VS_MARKETPLACE_PAT --repo Rabosa616/CopilotActivityViewer`.

La fitxa del Marketplace es genera a partir de `marketplace/overview.md` i
`marketplace/publishManifest.json`. El Marketplace publica l'extensió per a Visual Studio; per a
SSMS, instal·la el `.vsix` de la *release* tal com s'explica a [Instal·lar](#installar).

## Llicència

[MIT](LICENSE)
