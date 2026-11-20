# 01 · Finestra *Copilot Activity*

## Objectiu

Veure en directe què fan l'agent de GitHub Copilot i els seus sub-agents dins de Visual Studio
2026 o SQL Server Management Studio 22: quins agents hi ha, què estan fent, quines eines criden,
amb quins arguments i amb quin resultat.

## Obertura

- Menús **View > Other Windows > Copilot Activity** i **Tools > Copilot Activity**
  (icona *ActivityDiagram*).
- S'obre a la zona de documents. Cada instància de l'IDE té la seva pròpia finestra i el seu
  propi estat; obrir-la en una instància no afecta les altres.
- En obrir-la, es carrega la sessió més recent de la instància (vegeu
  [03-abast-per-instancia.md](03-abast-per-instancia.md)).

## Disposició

```
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│ Session [ VS · [Repo] títol de la sessió ▾ ]  This Visual Studio (PID n) ☐All instances ☑Follow latest [Refresh] │
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│ ● Resum: estat · model · agents · eines · tokens · última activitat                          │
├───────────────────────┬──────────────────────────────────────────────────────────────────────┤
│ Agents                │ Activity  n of m   [Search…] [Status▾] [Agent▾] ☑Include sub-agents   │
│ ● main      Running   │                                         [Clear filters] ☑Newest first │
│   model · durada      │ (USER 3) (AI 40) (TOOL 120) (AGENT 4) (SKILL 1) (WARN 2) (ERROR 1) … │
│   eines · tokens      │ ┌──────────────────────────────────────────────────────────────────┐ │
│   ↳ activitat actual  │ │ hora │ TIPUS │ agent │ títol + subtítol        │ ● estat │ durada│ │
│   ● sub-agent  Idle   │ │ …                                                                │ │
│     …                 │ └──────────────────────────────────────────────────────────────────┘ │
│                       │ ═══════════════ (separador redimensionable) ═══════════════          │
│                       │ ┌ Detall formatat ───────────────────────────────────────── [Copy] ┐ │
│                       │ └──────────────────────────────────────────────────────────────────┘ │
└───────────────────────┴──────────────────────────────────────────────────────────────────────┘
```

- La columna d'agents (300 px, mínim 160) i la llista/detall són redimensionables amb
  separadors. El detall fa 280 px d'alçada inicial.
- Els colors i les fonts són els del tema actiu de l'IDE (clar, fosc, blau…).

## Barra de sessió

| Control | Comportament |
|---------|--------------|
| **Session** (desplegable) | Sessions ordenades de més a menys recent (màxim 100). Text: `<origen> · [<repositori o carpeta>] <títol>`; el *tooltip* mostra la ruta d'`events.jsonl`. |
| Text d'abast | Instància monitoritzada, p. ex. `This Visual Studio (PID 39400)`; vegeu spec 03. |
| **All instances** | Mostra també les sessions de les altres instàncies. Només visible quan l'extensió sap quina instància l'allotja. Per defecte, desmarcat. |
| **Follow latest** | Marcat per defecte. Canvia automàticament a la sessió més recent quan la sessió actual porta més de 15 s sense activitat i n'hi ha una de més nova. |
| **Refresh** | Torna a buscar sessions i recarrega la sessió seleccionada. Desactivat mentre s'executa. |

## Resum

Punt de color segons l'estat de l'agent principal (en curs, inactiu, error) i una línia amb:
estat, model actual, nombre d'agents i d'eines, tokens d'entrada/sortida i hora de l'últim
esdeveniment. El *tooltip* conté el resum complet.

## Agents

- Llista jeràrquica: l'agent principal (`main`) i els sub-agents, indentats per profunditat
  (inclou sub-agents niats).
- Per a cada agent: punt d'estat, nom, estat (*Running*, *Idle*, *Completed*, *Failed*…),
  tipus/model, estadístiques (eines, tokens, durada) i, si n'hi ha, l'activitat en curs en
  cursiva blava.
- Seleccionar un agent filtra la línia de temps per aquell agent (equivalent al desplegable
  *Agent*).

## Línia de temps (Activity)

Cada fila: hora · etiqueta de tipus de color · agent · títol (negreta) + subtítol (gris) ·
estat amb punt de color · durada.

| Tipus | Etiqueta | Origen |
|-------|----------|--------|
| User | `USER` | Missatges de l'usuari |
| Assistant | `AI` | Missatges de l'agent o dels sub-agents |
| Tool | `TOOL` | Crides a eines (arguments, resultat, durada, aprovacions) |
| Subagent | `AGENT` | Inici i fi de sub-agents |
| Skill | `SKILL` | Skills invocades |
| Notification | `NOTIFY` | Notificacions del sistema, compactacions… |
| Warning | `WARN` | Avisos, cancel·lacions (*Aborted*) |
| Error | `ERROR` | Errors de sessió |
| Completion | `DONE` | `task_complete` |

Estats d'una activitat: *Running*, *Approval* (esperant permís), *OK*, *Failed*, *Cancelled*.

### Ordre

- **Newest first** (per defecte): el més recent a dalt. Desmarcat: ordre cronològic.
- Les activitats noves s'afegeixen sense perdre la selecció ni el desplaçament.

### Llista limitada

Cada fila visible és un objecte que Remote UI envia a l'IDE. Per això, en sessions llargues
(milers d'esdeveniments), enviar-les totes alentia molt l'obertura de la finestra.

- Només es llisten les **250 activitats més recents** que compleixen els filtres, tant amb
  *Newest first* com en ordre cronològic.
- El recompte ho indica: `2318 events · latest 250 shown`.
- El botó **Show N older** (només visible si n'hi ha d'amagades) n'afegeix 500 més sense
  substituir la llista.
- Els filtres i la cerca sempre treballen sobre **tota** la sessió. En canviar un filtre o de
  sessió, el límit torna a 250.

### Filtres

Tots es combinen (I lògic). El text `n of m` indica les activitats visibles respecte del total.

| Filtre | Comportament |
|--------|--------------|
| **Xips de tipus** | Un xip per tipus present a la sessió, amb el recompte. Clic: mostra/amaga el tipus. Clic dret: només aquell tipus; un altre clic dret sobre el mateix xip ho torna a mostrar tot. |
| **Status** | *All*, *OK* (èxit o `DONE`), *Failed* (fallades o `ERROR`), *Running* (en curs o esperant aprovació), *Cancelled*. |
| **Agent** | Desplegable jeràrquic sincronitzat amb la llista d'agents. |
| **Include sub-agents** | Marcat per defecte: inclou l'activitat dels agents que ha llançat l'agent seleccionat. |
| **Search** | Text lliure, sense distingir majúscules, amb 250 ms de retard. Cerca al títol, subtítol, eina, agent i també dins dels arguments i resultats. |
| **Clear filters** | Restableix tots els filtres. Només actiu quan n'hi ha algun d'aplicat. |

## Detall

Mostra l'activitat seleccionada formatada; vegeu [02-detall-formatat.md](02-detall-formatat.md).
Si no hi ha selecció, queda buit i el botó **Copy** s'amaga.

## Refresc en directe

- Cada segon es llegeixen només les línies noves d'`events.jsonl` (lectura incremental; les
  línies incompletes s'esperen fins que acaben).
- Cada 5 segons es tornen a buscar sessions.
- Si el fitxer s'escurça o es reemplaça, la sessió es recarrega sencera.
- Les durades de les activitats en curs avancen en directe.
- Esdeveniments o camps desconeguts s'ignoren (el format és intern de Copilot).

## Missatges d'estat

Es mostren centrats sobre el contingut:

- Sense sessions a la instància: *No Copilot agent sessions in this Visual Studio (PID n) yet.*
  amb el suggeriment d'iniciar un xat o marcar *All instances*.
- Sense sessions enlloc: llista de carpetes on s'ha buscat.
- Errors de lectura: *Could not scan sessions…* / *Could not read events…*

Desapareixen automàticament quan apareix una sessió.

## Criteris d'acceptació

1. Amb una sessió amb sub-agents niats, la llista d'agents té `main` com a arrel i tots els
   sub-agents iniciats hi apareixen sota el seu pare. → `LocalSessionsTests.AllLocalSessionsParseAndProduceConsistentAgentTrees`,
   `SessionModelTests.BuildsNestedAgentTreeAndToolLifecycle`
2. La plantilla Remote UI real es renderitza amb dades reals: llistes enllaçades, xips de tipus
   i detall. → `LocalSessionsTests.RemoteUiTemplateRendersRealSession`
3. Els filtres de tipus, solo, estat, agent i cerca redueixen la llista correctament i
   *Clear filters* la restaura. → `DetailsAndFiltersTests.KindStatusAgentAndSearchFiltersNarrowTheActivityList`
4. Les línies noves s'incorporen sense recarregar; *Newest first* les posa a dalt; el detall
   segueix la selecció. → `DetailsAndFiltersTests.NewEventsAreMergedNewestFirstAndDetailsFollowTheSelection`
5. Les línies parcials i els caràcters multibyte partits no es processen fins que estan
   complets. → `EventLogAndCatalogTests.ReaderReturnsOnlyCompleteLinesIncludingSplitMultibyteCharacters`
6. En una sessió de més de 250 activitats, només es llisten les 250 més recents. La cerca
   troba activitats antigues, i *Show older* afegeix les anteriors sense refer la llista i es
   manté amb les actualitzacions en directe. →
   `DetailsAndFiltersTests.LongSessionsListOnlyTheLatestActivityUntilOlderIsRequested`
7. Verificació manual: la finestra s'obre ràpidament amb una sessió de més de 10 MB.
