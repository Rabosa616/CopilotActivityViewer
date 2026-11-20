using CopilotActivityViewer.Model;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace CopilotActivityViewer.ToolWindow;

[VisualStudioContribution]
internal sealed class ActivityToolWindow : Microsoft.VisualStudio.Extensibility.ToolWindows.ToolWindow
{
    private ActivityViewModel viewModel;
    private ActivityControl content;

    public ActivityToolWindow()
    {
        Title = "Copilot Activity";
    }

    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.DocumentWell,
        AllowAutoCreation = true,
    };

    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        if (content == null)
        {
            viewModel = new ActivityViewModel(SessionCatalog.CreateDefault());
            viewModel.Start();
            content = new ActivityControl(viewModel);
        }

        return Task.FromResult<IRemoteUserControl>(content);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            viewModel?.Dispose();
            content?.Dispose();
        }

        base.Dispose(disposing);
    }
}
