# Especificacions

Especificacions funcionals de **Copilot Activity Viewer**. Descriuen el comportament esperat
de la finestra; quan el codi i una spec no coincideixen, cal corregir l'un o l'altre en el
mateix canvi.

| Spec | Contingut |
|------|-----------|
| [01-finestra-copilot-activity.md](01-finestra-copilot-activity.md) | Pantalla: disposició, controls, filtres, estats buits i refresc en directe |
| [02-detall-formatat.md](02-detall-formatat.md) | Panell de detall: seccions, ressaltat de codi, markdown, JSON i còpia |
| [03-abast-per-instancia.md](03-abast-per-instancia.md) | Com cada finestra mostra només les sessions de la seva instància de Visual Studio / SSMS |

## Convencions

- Els textos de la UI són en anglès (com la resta de l'IDE); les specs, en català.
- Cada spec acaba amb **criteris d'acceptació** i els tests que els cobreixen
  (`tests\CopilotActivityViewer.Tests`).
- Implementació: `src\CopilotActivityViewer\ToolWindow\ActivityControl.xaml` (Remote UI) i
  `ActivityViewModel.cs`.
