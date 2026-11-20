# 02 · Detall formatat

## Objectiu

Que el contingut de cada activitat (scripts, codi, JSON, respostes en markdown, errors) sigui
llegible sense haver de copiar-lo a un altre editor.

## Estructura

1. **Títol** de l'activitat (mida més gran, negreta).
2. **Metadades**: tipus, agent, eina, hora, durada, estat, aprovacions.
3. **Arguments** (eines) o **Task** (sub-agents).
4. **Result**, o **Error** en vermell si ha fallat.
5. Per als missatges de l'AI, de l'usuari i dels sub-agents: el text com a markdown.

El text de cada secció es limita a 12.000 caràcters per no bloquejar la UI.

## Codi i scripts

- Ressaltat de sintaxi per a PowerShell, bash, C#, Python, JavaScript/TypeScript, JSON,
  XML/XAML, SQL, YAML i diff.
- El llenguatge es detecta, per ordre: nom de l'eina (`powershell`, `bash`…), extensió del
  fitxer dels arguments (`path`, `filePath`…), etiqueta del bloc de markdown i, finalment,
  el contingut.
- Blocs de codi en font monoespaiada, amb fons diferenciat i sense ajust de línia forçat
  (es pot desplaçar).
- Els colors dels tokens funcionen amb tema clar i fosc.

## JSON

- Els arguments i els resultats JSON es mostren indentats.
- Els arguments coneguts es presenten amb nom i valor (p. ex. `command`, `path`,
  `description`); la resta, com a JSON.

## Markdown

Títols, paràgrafs, negreta, cursiva, codi en línia, enllaços, llistes (també niades), cites,
taules i blocs de codi amb ressaltat.

## Robustesa

- Els caràcters especials de XAML (`{`, `}`, `<`, `>`, `&`) i els caràcters de control es
  mostren literalment; mai trenquen el document.
- Els emojis i els caràcters no llatins es mostren correctament.

## Copy

El botó **Copy** (a dalt a la dreta del detall) copia el text cru de l'activitat al porta-retalls.

## Criteris d'acceptació

1. Un script de PowerShell i un resultat JSON surten ressaltats i el JSON indentat, amb els
   colors de la paleta. → `DetailsAndFiltersTests.DetailsXamlHighlightsScriptsAndPrettyPrintsJsonResults`
2. Text amb claus, etiquetes XAML, `&amp;`, caràcters de control i emojis es mostra tal
   qual. → `DetailsAndFiltersTests.DetailsXamlEscapesMarkupBracesAndControlCharacters`
3. Els tokens de PowerShell, C#, JSON, XML i diff es classifiquen correctament i els
   llenguatges es detecten per nom, ruta, eina i contingut. → `FormattingTests`
4. El markdown (títols, llistes, taules, blocs) es parseja en blocs i elements en línia. →
   `FormattingTests.MarkdownBlocksAndInlinesAreParsed`
