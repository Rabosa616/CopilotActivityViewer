using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;

namespace CopilotActivityViewer.ToolWindow
{
    [Guid(PackageGuids.ToolWindowString)]
    public sealed class ActivityToolWindow : ToolWindowPane
    {
        private readonly ActivityToolWindowControl control;

        public ActivityToolWindow()
            : base(null)
        {
            Caption = "Copilot Activity";
            BitmapImageMoniker = KnownMonikers.Activity;
            control = new ActivityToolWindowControl();
            Content = control;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                control.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
