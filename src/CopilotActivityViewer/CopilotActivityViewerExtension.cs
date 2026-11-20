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
            publisherName: "Oscar Vaquero Viñes",
            displayName: "Copilot Activity Viewer",
            description: "Live view of what GitHub Copilot and its sub-agents are doing: agent tree, filterable activity timeline and formatted tool calls. For Visual Studio 2026 and SSMS 22.")
        {
            Icon = "Images\\Icon.png",
            PreviewImage = "Images\\Preview.png",
            Tags = ["Copilot", "Agents", "AI", "SSMS"],
            MoreInfo = "https://github.com/Rabosa616/CopilotActivityViewer",
            DotnetTargetVersions = [DotnetTarget.Net10],
            InstallationTargetVersion = "[18.12,)",
            Preview = false,
        },
    };
}
