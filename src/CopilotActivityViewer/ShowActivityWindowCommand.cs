using CopilotActivityViewer.ToolWindow;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace CopilotActivityViewer;

/// <summary>View &gt; Other Windows &gt; Copilot Activity (also under Tools in SSMS).</summary>
[VisualStudioContribution]
internal sealed class ShowActivityWindowCommand : Command
{
    public override CommandConfiguration CommandConfiguration => new("%CopilotActivityViewer.ShowActivityWindowCommand.DisplayName%")
    {
        TooltipText = "%CopilotActivityViewer.ShowActivityWindowCommand.ToolTip%",
        Placements = [CommandPlacement.KnownPlacements.ViewOtherWindowsMenu, CommandPlacement.KnownPlacements.ToolsMenu],
        Icon = new(ImageMoniker.KnownValues.ActivityDiagram, IconSettings.IconAndText),
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken) =>
        Extensibility.Shell().ShowToolWindowAsync<ActivityToolWindow>(activate: true, cancellationToken);
}
