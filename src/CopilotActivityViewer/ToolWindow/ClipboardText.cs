using System.Runtime.InteropServices;

namespace CopilotActivityViewer.ToolWindow;

/// <summary>Plain Win32 clipboard access: the out-of-process extension has no WPF/WinForms dispatcher to use.</summary>
internal static class ClipboardText
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    public static bool TrySet(string text)
    {
        text ??= string.Empty;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    return SetUnicodeText(text);
                }
                finally
                {
                    CloseClipboard();
                }
            }

            Thread.Sleep(50);
        }

        return false;
    }

    private static bool SetUnicodeText(string text)
    {
        if (!EmptyClipboard())
        {
            return false;
        }

        var bytes = (text.Length + 1) * 2;
        var handle = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var target = GlobalLock(handle);
        if (target == IntPtr.Zero)
        {
            GlobalFree(handle);
            return false;
        }

        try
        {
            Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
            Marshal.WriteInt16(target, text.Length * 2, 0);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        if (SetClipboardData(CF_UNICODETEXT, handle) == IntPtr.Zero)
        {
            GlobalFree(handle);
            return false;
        }

        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
