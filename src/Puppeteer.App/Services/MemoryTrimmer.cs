using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Puppeteer.App.Services;

/// <summary>
/// Returns Puppeteer's working set to the OS while it sits in the tray. A project launcher spends most
/// of its life hidden, and WPF holds on to a lot of decoded glyph, icon and render memory that nothing
/// is looking at once the window is gone. Compacting the managed heap and asking Windows to trim the
/// working set drops the backgrounded footprint sharply; the pages fault back in when the window returns.
/// </summary>
internal static class MemoryTrimmer
{
    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimumWorkingSetSize, IntPtr maximumWorkingSetSize);

    public static void Trim()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        // (-1, -1) is the documented signal to empty the working set down to what's currently required,
        // letting Windows page out the rest rather than pinning it to a fixed size.
        SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
    }
}
