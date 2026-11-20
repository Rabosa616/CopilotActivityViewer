using System;

namespace CopilotActivityViewer
{
    internal static class PackageGuids
    {
        public const string PackageString = "4b53dc8c-5a5f-4ddd-9c92-379bb302bab0";
        public const string CommandSetString = "b49222af-cff2-49c7-b565-6fd5e581a6c5";
        public const string ToolWindowString = "639f55b2-9a9b-4daa-8b9a-24f16d12e414";

        public static readonly Guid CommandSet = new Guid(CommandSetString);
    }

    internal static class PackageIds
    {
        public const int ShowActivityWindowCommandId = 0x0100;
    }
}
