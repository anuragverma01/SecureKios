using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using SecureKiosk.Core.Interfaces;
using SecureKiosk.Core.Models;

namespace SecureKiosk.App.Components;

public sealed partial class ExitCodeDialog : ContentDialog
{
    public ExitCodeDialog() => InitializeComponent();

    public static async Task ShowAsync(Microsoft.UI.Xaml.XamlRoot? xamlRoot)
    {
        var dialog = new ExitCodeDialog { XamlRoot = xamlRoot };
        await dialog.ShowAsync();
    }

    private async void OnVerify(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var enteredCode = CodeBox.Password;
            if (!string.Equals(enteredCode, "5013", StringComparison.Ordinal))
            {
                args.Cancel = true;
                ErrorText.Text = "The exit code is invalid.";
                ErrorText.Visibility = Visibility.Visible;
                CodeBox.Password = string.Empty;
                return;
            }

            // 1. Unhook keyboard lockdown so the administrator regains standard keyboard shortcuts
            try { SecureKiosk.App.Security.KeyboardLockdownHook.Uninstall(); } catch { }

            if (OperatingSystem.IsWindows())
            {
                // 2. Direct In-Process Registry Cleanup (HKCU) - Immediate and guaranteed
                try
                {
                    using var sysKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System", writable: true);
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
                    using var expKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", writable: true);
                    if (expKey != null)
                    {
                        expKey.DeleteValue("NoLogoff", throwOnMissingValue: false);
                        expKey.DeleteValue("NoRun", throwOnMissingValue: false);
                    }
                }
                catch { }

                try
                {
                    using var cmdKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\System", writable: true);
                    cmdKey?.DeleteValue("DisableCMD", throwOnMissingValue: false);
                }
                catch { }

                try
                {
                    using var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                    runKey?.DeleteValue("SecureKioskFastLaunch", throwOnMissingValue: false);
                }
                catch { }

                try
                {
                    using var serKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", writable: true);
                    if (serKey != null)
                    {
                        serKey.DeleteValue("StartupDelayInMSec", throwOnMissingValue: false);
                        serKey.DeleteValue("WaitForIdleState", throwOnMissingValue: false);
                    }
                }
                catch { }

                // 3. Redundant reg.exe deletion
                string[] sysPolicies = { "DisableTaskMgr", "DisableLockWorkstation", "DisableChangePassword" };
                foreach (var val in sysPolicies)
                {
                    try
                    {
                        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "reg.exe",
                            Arguments = $"delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v \"{val}\" /f",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        p?.WaitForExit(500);
                    }
                    catch { }
                }

                string[] expPolicies = { "NoLogoff", "NoRun" };
                foreach (var val in expPolicies)
                {
                    try
                    {
                        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "reg.exe",
                            Arguments = $"delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer\" /v \"{val}\" /f",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        p?.WaitForExit(500);
                    }
                    catch { }
                }

                try
                {
                    using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "reg.exe",
                        Arguments = "delete \"HKCU\\Software\\Policies\\Microsoft\\Windows\\System\" /v \"DisableCMD\" /f",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    p?.WaitForExit(500);
                }
                catch { }

                try
                {
                    using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "reg.exe",
                        Arguments = "delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" /v \"SecureKioskFastLaunch\" /f",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    p?.WaitForExit(500);
                }
                catch { }

                // 4. Delete scheduled tasks if present
                try
                {
                    using var delProc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = "/delete /tn \"SecureKioskInstantLaunch\" /f",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    delProc?.WaitForExit(1000);
                }
                catch { }

                // 5. Run disarm helper if present
                try
                {
                    using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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
                        using var cmdProc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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

                // 6. Disarm UWP StartupTask (in isolated try-catch so failure never blocks registry cleanup)
                try
                {
                    var task = await global::Windows.ApplicationModel.StartupTask.GetAsync("SecureKioskStartupTask");
                    task.Disable();
                }
                catch { }
            }

            await App.Services.GetRequiredService<IWindowsKioskService>().RequestCleanExitAsync(ExitCodes.AuthorizedExit);
            args.Cancel = false;
            // Application.Current.Exit() requests XAML shutdown but is not guaranteed
            // to terminate the native process in WinUI 3. Environment.Exit ensures the
            // process is removed from the OS and is visible to Task Manager / Get-Process.
            Application.Current.Exit();
            Environment.Exit(ExitCodes.AuthorizedExit);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnCodeChanged(object sender, RoutedEventArgs args)
    {
        if (ErrorText.Visibility == Visibility.Visible)
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }
    }
}
