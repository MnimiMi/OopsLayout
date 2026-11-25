using Microsoft.Win32;

namespace OopsLayout.Windows;

/// <summary>
/// "Launch at startup" via the per-user Run key. Two registry locations matter:
///
///  • HKCU\…\CurrentVersion\Run                       — the actual autostart command
///  • HKCU\…\Explorer\StartupApproved\Run             — Windows' enable/disable
///    ledger (the on/off state Task Manager's Startup tab shows and writes)
///
/// If the user disables our item in Task Manager, the Run command stays but
/// StartupApproved marks it off — so re-writing Run alone wouldn't re-enable it.
/// We therefore read both (enabled = Run present AND not disabled in Approved)
/// and, when enabling, clear the Approved mark so Windows treats it as on.
/// </summary>
internal static class AutoStart
{
    private const string RunKey =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "OopsLayout";

    /// <summary>True when the app is set to launch at sign-in and not disabled in Task Manager.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is null) return false;

            // If Task Manager has this item, its first state byte is even when
            // enabled (0x02) and odd when disabled (0x03).
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            if (approved?.GetValue(ValueName) is byte[] { Length: > 0 } state)
                return (state[0] & 1) == 0;

            return true; // no Approved entry → Windows treats it as enabled
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool on)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on)
            {
                // Quote the path so a Program Files-style path with spaces works.
                run?.SetValue(ValueName, $"\"{ExePath}\"");

                // Drop any "disabled" mark Task Manager may have left, so the
                // item counts as enabled again.
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
                if (approved?.GetValue(ValueName) is not null)
                    approved.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                run?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Registry access can fail under locked-down policies — never crash
            // over a preference toggle.
        }
    }

    // Environment.ProcessPath is the real .exe even for a single-file publish
    // (Assembly.Location is empty there).
    private static string ExePath => Environment.ProcessPath ?? "";
}
