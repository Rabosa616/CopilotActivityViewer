using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using CopilotActivityViewer.ToolWindow;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace CopilotActivityViewer
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("Copilot Activity Viewer", "Live view of what Copilot agent mode and its sub-agents are doing.", "1.0.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(ActivityToolWindow), Style = VsDockStyle.Tabbed, Window = OutputWindowGuid, Orientation = ToolWindowOrientation.Bottom)]
    [Guid(PackageGuids.PackageString)]
    public sealed class CopilotActivityViewerPackage : AsyncPackage
    {
        private const string OutputWindowGuid = "34e76e81-ee4a-11d0-ae2e-00a0c90fffc3";

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
            {
                var id = new CommandID(PackageGuids.CommandSet, PackageIds.ShowActivityWindowCommandId);
                commandService.AddCommand(new MenuCommand(OnShowToolWindow, id));
            }
        }

        private void OnShowToolWindow(object sender, EventArgs e)
        {
            _ = JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    var window = await ShowToolWindowAsync(typeof(ActivityToolWindow), 0, create: true, cancellationToken: DisposalToken);
                    if (window?.Frame == null)
                    {
                        throw new NotSupportedException("Cannot create the Copilot Activity tool window.");
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    await JoinableTaskFactory.SwitchToMainThreadAsync();
                    ActivityLog.TryLogError(nameof(CopilotActivityViewerPackage), ex.ToString());
                    VsShellUtilities.ShowMessageBox(
                        this,
                        ex.ToString(),
                        "Copilot Activity",
                        OLEMSGICON.OLEMSGICON_CRITICAL,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                }
            });
        }
    }
}
