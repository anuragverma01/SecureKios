using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SecureKiosk.App.Security;
using SecureKiosk.Core.Interfaces;
using SecureKiosk.Core.Models;
using SecureKiosk.Infrastructure.Windows;

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

            // 1. Stop background watchdog and unhook keyboard lockdown
            try { KioskSecurityWatchdog.Stop(); } catch { }
            try { KeyboardLockdownHook.Uninstall(); } catch { }

            if (OperatingSystem.IsWindows())
            {
                // 2. Direct In-Process and reg.exe Registry Cleanup & Shell Notification
                KioskPolicyManager.RemovePolicies();

                // 3. Disarm UWP StartupTask (in isolated try-catch so failure never blocks registry cleanup)
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
