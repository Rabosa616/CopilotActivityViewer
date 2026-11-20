using Microsoft.VisualStudio.Extensibility.UI;

namespace CopilotActivityViewer.ToolWindow;

/// <summary>Remote UI control: <c>ActivityControl.xaml</c> is loaded by Visual Studio / SSMS and bound to <see cref="ActivityViewModel"/>.</summary>
internal sealed class ActivityControl : RemoteUserControl
{
    public ActivityControl(ActivityViewModel dataContext)
        : base(dataContext)
    {
    }
}
