using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SecureKiosk.Infrastructure.Windows;

/// <summary>
/// Manages Windows policies, registry lockdowns, taskbar state, and shell behavior for SecureKiosk.
/// Provides dual-layer application (direct in-process registry manipulation and out-of-process reg.exe calls)
/// to ensure policies are applied and cleaned up reliably across both packaged and unpackaged environments.
/// </summary>
public static class KioskPolicyManager
{
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

    /// <summary>
    /// Completely hides and disables the Windows Taskbar on all monitors.
    /// </summary>
    public static void HideTaskbar()
    {
        try
        {
            var hTaskbar = FindWindow("Shell_TrayWnd", null);
            if (hTaskbar != IntPtr.Zero)
            {
                ShowWindow(hTaskbar, SW_HIDE);
                EnableWindow(hTaskbar, false);
            }

            var hSecondary = FindWindow("Shell_SecondaryTrayWnd", null);
            if (hSecondary != IntPtr.Zero)
            {
                ShowWindow(hSecondary, SW_HIDE);
                EnableWindow(hSecondary, false);
            }
        }
        catch { }
    }

    /// <summary>
    /// Restores and enables the Windows Taskbar on all monitors.
    /// </summary>
    public static void ShowTaskbar()
    {
        try
        {
            var hTaskbar = FindWindow("Shell_TrayWnd", null);
            if (hTaskbar != IntPtr.Zero)
            {
                EnableWindow(hTaskbar, true);
                ShowWindow(hTaskbar, SW_SHOW);
            }

            var hSecondary = FindWindow("Shell_SecondaryTrayWnd", null);
            if (hSecondary != IntPtr.Zero)
            {
                EnableWindow(hSecondary, true);
                ShowWindow(hSecondary, SW_SHOW);
            }
        }
        catch { }
    }

    /// <summary>
    /// Synchronously applies lockdown policies:
    /// - Disables Task Manager (DisableTaskMgr = 1)
    /// - Disables Lock Workstation (DisableLockWorkstation = 1)
    /// - Disables Change Password (DisableChangePassword = 1)
    /// - Disables Sign Out (NoLogoff = 1)
    /// - Disables Run dialog (NoRun = 1)
    /// - Disables Windows hotkeys (NoWinKeys = 1)
    /// - Disables Taskbar context menu (NoTrayContextMenu = 1)
    /// - Disables Desktop context menu (NoViewContextMenu = 1)
    /// - Disables Windows Search / Find (NoFind = 1)
    /// - Hides Taskbar Search Box (SearchboxTaskbarMode = 0)
    /// - Hides and disables Taskbar window
    /// - Enforces DisallowRun list (cmd.exe, powershell.exe, pwsh.exe, taskmgr.exe, wt.exe, taskkill.exe, regedit.exe)
    /// - Disables Command Prompt (DisableCMD = 2)
    /// - Minimizes startup delays (StartupDelayInMSec = 0, WaitForIdleState = 0)
    /// - Registers fast launch in HKCU Run key if package family is available
    /// </summary>
    public static void ApplyPolicies(string? packageFamilyName = null)
    {
        if (!OperatingSystem.IsWindows()) return;

        // 1. Direct In-Process Registry Policy Application (HKCU)
        try
        {
            using var sysKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System");
            sysKey?.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
            sysKey?.SetValue("DisableLockWorkstation", 1, RegistryValueKind.DWord);
            sysKey?.SetValue("DisableChangePassword", 1, RegistryValueKind.DWord);
        }
        catch { }

        try
        {
            using var expKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer");
            expKey?.SetValue("NoLogoff", 1, RegistryValueKind.DWord);
            expKey?.SetValue("NoRun", 1, RegistryValueKind.DWord);
            expKey?.SetValue("NoWinKeys", 1, RegistryValueKind.DWord);
            expKey?.SetValue("NoTrayContextMenu", 1, RegistryValueKind.DWord);
            expKey?.SetValue("NoViewContextMenu", 1, RegistryValueKind.DWord);
            expKey?.SetValue("NoFind", 1, RegistryValueKind.DWord);
            expKey?.SetValue("NoFolderOptions", 1, RegistryValueKind.DWord);
            expKey?.SetValue("NoFileMenu", 1, RegistryValueKind.DWord);
            expKey?.SetValue("NoSetTaskbar", 1, RegistryValueKind.DWord);
            expKey?.SetValue("LockTaskbar", 1, RegistryValueKind.DWord);
            expKey?.SetValue("DisallowRun", 1, RegistryValueKind.DWord);
        }
        catch { }

        try
        {
            using var polExpKey = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\Explorer");
            polExpKey?.SetValue("NoRun", 1, RegistryValueKind.DWord);
            polExpKey?.SetValue("DisableSearchBoxSuggestions", 1, RegistryValueKind.DWord);
        }
        catch { }

        try
        {
            using var searchKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search");
            searchKey?.SetValue("SearchboxTaskbarMode", 0, RegistryValueKind.DWord);
            searchKey?.SetValue("BingSearchEnabled", 0, RegistryValueKind.DWord);
        }
        catch { }

        try
        {
            using var padKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad");
            padKey?.SetValue("FourFingerTapEnabled", 0, RegistryValueKind.DWord);
            padKey?.SetValue("FourFingerSwipeEnabled", 0, RegistryValueKind.DWord);
            padKey?.SetValue("ThreeFingerTapEnabled", 0, RegistryValueKind.DWord);
            padKey?.SetValue("ThreeFingerSwipeEnabled", 0, RegistryValueKind.DWord);
        }
        catch { }

        try
        {
            using var advKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            advKey?.SetValue("ShowTaskViewButton", 0, RegistryValueKind.DWord);
        }
        catch { }

        try
        {
            using var disKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun");
            disKey?.SetValue("1", "taskmgr.exe", RegistryValueKind.String);
            disKey?.SetValue("2", "cmd.exe", RegistryValueKind.String);
            disKey?.SetValue("3", "powershell.exe", RegistryValueKind.String);
            disKey?.SetValue("4", "pwsh.exe", RegistryValueKind.String);
            disKey?.SetValue("5", "wt.exe", RegistryValueKind.String);
            disKey?.SetValue("6", "taskkill.exe", RegistryValueKind.String);
            disKey?.SetValue("7", "regedit.exe", RegistryValueKind.String);
        }
        catch { }

        try
        {
            using var cmdKey = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\System");
            cmdKey?.SetValue("DisableCMD", 2, RegistryValueKind.DWord);
        }
        catch { }

        try
        {
            using var serKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize");
            serKey?.SetValue("StartupDelayInMSec", 0, RegistryValueKind.DWord);
            serKey?.SetValue("WaitForIdleState", 0, RegistryValueKind.DWord);
        }
        catch { }

        if (!string.IsNullOrEmpty(packageFamilyName))
        {
            try
            {
                using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                runKey?.SetValue("SecureKioskFastLaunch", $"explorer.exe shell:AppsFolder\\{packageFamilyName}!App");
            }
            catch { }
        }

        // 2. Redundant reg.exe execution for guaranteed out-of-process persistence
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr", "REG_DWORD", "1");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableLockWorkstation", "REG_DWORD", "1");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableChangePassword", "REG_DWORD", "1");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoLogoff", "REG_DWORD", "1");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRun", "REG_DWORD", "1");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoWinKeys", "REG_DWORD", "1");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoTrayContextMenu", "REG_DWORD", "1");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoViewContextMenu", "REG_DWORD", "1");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoFind", "REG_DWORD", "1");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoFolderOptions", "REG_DWORD", "1");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoFileMenu", "REG_DWORD", "1");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoSetTaskbar", "REG_DWORD", "1");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "LockTaskbar", "REG_DWORD", "1");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "DisallowRun", "REG_DWORD", "1");
        RunRegAdd(@"HKCU\Software\Policies\Microsoft\Windows\Explorer", "NoRun", "REG_DWORD", "1");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", "REG_DWORD", "0");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun", "1", "REG_SZ", "taskmgr.exe");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun", "2", "REG_SZ", "cmd.exe");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun", "3", "REG_SZ", "powershell.exe");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun", "4", "REG_SZ", "pwsh.exe");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun", "5", "REG_SZ", "wt.exe");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun", "6", "REG_SZ", "taskkill.exe");
        RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun", "7", "REG_SZ", "regedit.exe");
        RunRegAdd(@"HKCU\Software\Policies\Microsoft\Windows\System", "DisableCMD", "REG_DWORD", "2");

        if (!string.IsNullOrEmpty(packageFamilyName))
        {
            RunRegAdd(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "SecureKioskFastLaunch", "REG_SZ", $"explorer.exe shell:AppsFolder\\{packageFamilyName}!App");
        }

        // 3. Immediately hide and disable Taskbar
        HideTaskbar();

        NotifyPolicyChange();
    }

    /// <summary>
    /// Completely restores Windows policies, re-enables Taskbar, and removes kiosk restrictions upon authorized administrator exit.
    /// </summary>
    public static void RemovePolicies()
    {
        if (!OperatingSystem.IsWindows()) return;

        // 1. Direct in-process registry deletion
        try
        {
            using var sysKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System", writable: true);
            if (sysKey != null)
            {
                sysKey.DeleteValue("DisableTaskMgr", throwOnMissingValue: false);
                sysKey.DeleteValue("DisableLockWorkstation", throwOnMissingValue: false);
                sysKey.DeleteValue("DisableChangePassword", throwOnMissingValue: false);
            }
        }
        catch { }

        try
        {
            using var expKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", writable: true);
            if (expKey != null)
            {
                expKey.DeleteValue("NoLogoff", throwOnMissingValue: false);
                expKey.DeleteValue("NoRun", throwOnMissingValue: false);
                expKey.DeleteValue("NoWinKeys", throwOnMissingValue: false);
                expKey.DeleteValue("NoTrayContextMenu", throwOnMissingValue: false);
                expKey.DeleteValue("NoViewContextMenu", throwOnMissingValue: false);
                expKey.DeleteValue("NoFind", throwOnMissingValue: false);
                expKey.DeleteValue("NoFolderOptions", throwOnMissingValue: false);
                expKey.DeleteValue("NoFileMenu", throwOnMissingValue: false);
                expKey.DeleteValue("NoSetTaskbar", throwOnMissingValue: false);
                expKey.DeleteValue("LockTaskbar", throwOnMissingValue: false);
                expKey.DeleteValue("DisallowRun", throwOnMissingValue: false);
            }
        }
        catch { }

        try
        {
            using var polExpKey = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\Explorer", writable: true);
            polExpKey?.DeleteValue("NoRun", throwOnMissingValue: false);
            polExpKey?.DeleteValue("DisableSearchBoxSuggestions", throwOnMissingValue: false);
        }
        catch { }

        try
        {
            using var searchKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search", writable: true);
            searchKey?.DeleteValue("SearchboxTaskbarMode", throwOnMissingValue: false);
            searchKey?.DeleteValue("BingSearchEnabled", throwOnMissingValue: false);
        }
        catch { }

        try
        {
            using var padKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad", writable: true);
            if (padKey != null)
            {
                padKey.DeleteValue("FourFingerTapEnabled", throwOnMissingValue: false);
                padKey.DeleteValue("FourFingerSwipeEnabled", throwOnMissingValue: false);
                padKey.DeleteValue("ThreeFingerTapEnabled", throwOnMissingValue: false);
                padKey.DeleteValue("ThreeFingerSwipeEnabled", throwOnMissingValue: false);
            }
        }
        catch { }

        try
        {
            using var advKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", writable: true);
            advKey?.DeleteValue("ShowTaskViewButton", throwOnMissingValue: false);
        }
        catch { }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun", throwOnMissingSubKey: false);
        }
        catch { }

        try
        {
            using var cmdKey = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\System", writable: true);
            cmdKey?.DeleteValue("DisableCMD", throwOnMissingValue: false);
        }
        catch { }

        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            runKey?.DeleteValue("SecureKioskFastLaunch", throwOnMissingValue: false);
        }
        catch { }

        try
        {
            using var serKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", writable: true);
            if (serKey != null)
            {
                serKey.DeleteValue("StartupDelayInMSec", throwOnMissingValue: false);
                serKey.DeleteValue("WaitForIdleState", throwOnMissingValue: false);
            }
        }
        catch { }

        // 2. Redundant reg.exe deletion
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableLockWorkstation");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableChangePassword");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoLogoff");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRun");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoWinKeys");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoTrayContextMenu");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoViewContextMenu");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoFind");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoFolderOptions");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoFileMenu");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoSetTaskbar");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "LockTaskbar");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "DisallowRun");
        RunRegDelete(@"HKCU\Software\Policies\Microsoft\Windows\Explorer", "NoRun");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode");
        RunRegKeyDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun");
        RunRegDelete(@"HKCU\Software\Policies\Microsoft\Windows\System", "DisableCMD");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "SecureKioskFastLaunch");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec");
        RunRegDelete(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "WaitForIdleState");

        // 3. Delete scheduled tasks if present
        try
        {
            using var delProc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/delete /tn \"SecureKioskInstantLaunch\" /f",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            delProc?.WaitForExit(1000);
        }
        catch { }

        // 4. Run disarm helper if present
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/run /tn \"SecureKioskDisarm\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            proc?.WaitForExit(1000);
        }
        catch { }

        try
        {
            var progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var disarmScript = System.IO.Path.Combine(progData, "SecureKiosk", "disarm.cmd");
            if (System.IO.File.Exists(disarmScript))
            {
                using var cmdProc = Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{disarmScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                cmdProc?.WaitForExit(1000);
            }
        }
        catch { }

        // 5. Restore and re-enable Taskbar
        ShowTaskbar();

        // 6. Notify Windows Shell and system to refresh policies immediately
        NotifyPolicyChange();
    }

    /// <summary>
    /// Broadcasts setting change and shell refresh events to notify Windows Explorer and policy listeners immediately.
    /// </summary>
    public static void NotifyPolicyChange()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }

        try
        {
            SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "Policy", SMTO_ABORTIFHUNG, 1000, out _);
        }
        catch { }
    }

    private static void RunRegAdd(string key, string valName, string type, string value)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"add \"{key}\" /v \"{valName}\" /t {type} /d \"{value}\" /f",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(500);
        }
        catch { }
    }

    private static void RunRegDelete(string key, string valName)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"delete \"{key}\" /v \"{valName}\" /f",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(500);
        }
        catch { }
    }

    private static void RunRegKeyDelete(string key)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"delete \"{key}\" /f",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(500);
        }
        catch { }
    }
}
