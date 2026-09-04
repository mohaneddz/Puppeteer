using Microsoft.Win32;
using Puppeteer.Core;

namespace Puppeteer.App.Services;

/// <summary>
/// Start-with-Windows via the per-user Run key. Deliberately not a scheduled task or a service: this
/// needs no elevation, the user can see and undo it from Task Manager's Startup tab, and it removes
/// itself cleanly.
/// </summary>
public sealed class WindowsStartupService : IStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Puppeteer";

    public static string StartMinimizedArgument => "--tray";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string value && value.Length > 0;
            }
            catch { return false; }
        }
    }

    public void SetEnabled(bool enabled, bool minimized)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;
            if (!enabled) { key.DeleteValue(ValueName, throwOnMissingValue: false); return; }
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)) return;
            // Quoted: the install path routinely contains spaces.
            key.SetValue(ValueName, minimized ? $"\"{executable}\" {StartMinimizedArgument}" : $"\"{executable}\"");
        }
        catch { }
    }
}
