using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using SecureKiosk.Core.Interfaces;
using SecureKiosk.Core.Models;

namespace SecureKiosk.App.Components;

public sealed partial class ExitCodeDialog : ContentDialog
{
    private bool _normalizingCode;

    public ExitCodeDialog() => InitializeComponent();

    public static async Task ShowAsync(Microsoft.UI.Xaml.XamlRoot? xamlRoot)
    {
        var dialog = new ExitCodeDialog { XamlRoot = xamlRoot };
        await dialog.ShowAsync();
    }

    private async void OnVerify(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var service = App.Services.GetRequiredService<IExitAuthorizationService>();
        var result = await service.AuthorizeAsync(CodeBox.Password);
        if (result.Status == ExitAuthorizationStatus.Authorized)
        {
            // Disarm startup task so the app does NOT reopen on restart/power off
            // after an authorized administrator exit.
            try
            {
                var task = await global::Windows.ApplicationModel.StartupTask.GetAsync("SecureKioskStartupTask");
                task.Disable();

                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        var disarmInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = "/run /tn \"SecureKioskDisarm\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = System.Diagnostics.Process.Start(disarmInfo);
                        proc?.WaitForExit(3000);
                    }
                    catch { }

                    try
                    {
                        var delInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = "/delete /tn \"SecureKioskInstantLaunch\" /f",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var delProc = System.Diagnostics.Process.Start(delInfo);
                        delProc?.WaitForExit(3000);
                    }
                    catch { }

                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", writable: true);
                    key?.DeleteValue("StartupDelayInMSec", throwOnMissingValue: false);
                    key?.DeleteValue("WaitForIdleState", throwOnMissingValue: false);

                    using var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                    runKey?.DeleteValue("SecureKioskFastLaunch", throwOnMissingValue: false);

                    using var policyKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System", writable: true);
                    policyKey?.DeleteValue("DisableTaskMgr", throwOnMissingValue: false);
                }
            }
            catch
            {
                // Fallback for unpackaged or unsupported environments
            }

            await App.Services.GetRequiredService<IWindowsKioskService>().RequestCleanExitAsync(ExitCodes.AuthorizedExit);
            args.Cancel = false;
            // Application.Current.Exit() requests XAML shutdown but is not guaranteed
            // to terminate the native process in WinUI 3. Environment.Exit ensures the
            // process is removed from the OS and is visible to Task Manager / Get-Process.
            Application.Current.Exit();
            Environment.Exit(ExitCodes.AuthorizedExit);
            return;
        }
        args.Cancel = true;
        ErrorText.Text = result.Status == ExitAuthorizationStatus.RateLimited
            ? "Too many attempts. Try again later."
            : "The exit code is invalid.";
        ErrorText.Visibility = Visibility.Visible;
        CodeBox.Password = string.Empty;
#if DEBUG
        // Development diagnostic only — never logs the entered code or credential material.
        System.Diagnostics.Debug.WriteLine($"[SecureKiosk] Administrator Exit: auth result = {result.Status}");
#endif
    }

    private void OnCodeChanged(object sender, RoutedEventArgs args)
    {
        if (_normalizingCode) return;
        var digits = new string(CodeBox.Password.Where(static character => character is >= '0' and <= '9').Take(4).ToArray());
        if (string.Equals(digits, CodeBox.Password, StringComparison.Ordinal)) return;
        _normalizingCode = true;
        try { CodeBox.Password = digits; }
        finally { _normalizingCode = false; }
    }
}
