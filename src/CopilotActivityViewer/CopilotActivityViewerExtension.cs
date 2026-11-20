using Microsoft.VisualStudio.Extensibility;

namespace CopilotActivityViewer;

/// <summary>Entry point of the out-of-process extension (VisualStudio.Extensibility).</summary>
[VisualStudioContribution]
internal sealed class CopilotActivityViewerExtension : Extension
{
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            id: "CopilotActivityViewer.4b53dc8c-5a5f-4ddd-9c92-379bb302bab0",
            version: ExtensionAssemblyVersion,
            publisherName: "Oscar Vaquero",
            displayName: "Copilot Activity Viewer",
            description: "Live view of what GitHub Copilot and its sub-agents are doing: agent tree, activity timeline with filters and formatted tool arguments and results. Works in Visual Studio 2026 and SQL Server Management Studio 22.")
        {
            Tags = ["Copilot", "Agents", "AI", "SSMS"],
            MoreInfo = "https://github.com/Rabosa616/CopilotActivityViewer",
            DotnetTargetVersions = [DotnetTarget.Net10],
            InstallationTargetVersion = "[18.12,)",
            Preview = false,
        },
    };
}
