using Microsoft.Win32;

namespace ZapperRadio.Shell;

/// <summary>
/// Whether ZapperRadio launches when the user signs in, through the per-user Run key so no admin rights
/// are needed. The registry is the source of truth: it also reflects a user turning this off from
/// Windows' own Startup Apps settings instead of from here.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ZapperRadio";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
