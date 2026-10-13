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

    // Shared by the original Administrator exit dialog and the new MainPage UI.
    // Returns false for an incorrect code. A correct code runs the existing
    // kiosk cleanup and authorized-exit sequence.
    public static async Task<bool> TryAuthorizedExitAsync(string enteredCode)
    {
        if (!string.Equals(enteredCode, "5013", StringComparison.Ordinal))
        {
            return false;
        }

        try { KioskSecurityWatchdog.Stop(); } catch { }
        try { KeyboardLockdownHook.Uninstall(); } catch { }

        if (OperatingSystem.IsWindows())
        {
            KioskPolicyManager.RemovePolicies();

            try
            {
                var task = await global::Windows.ApplicationModel.StartupTask.GetAsync(
                    "SecureKioskStartupTask");
                task.Disable();
            }
            catch
            {
                // Continue cleanup if disabling the startup task is unavailable.
            }
        }

        await App.Services
            .GetRequiredService<IWindowsKioskService>()
            .RequestCleanExitAsync(ExitCodes.AuthorizedExit);

        Application.Current.Exit();
        Environment.Exit(ExitCodes.AuthorizedExit);
        return true; // Required by the compiler; Environment.Exit normally terminates first.
    }

    private async void OnVerify(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();

        try
        {
            var accepted = await TryAuthorizedExitAsync(CodeBox.Password);

            if (!accepted)
            {
                args.Cancel = true;
                ErrorText.Text = "The exit code is invalid.";
                ErrorText.Visibility = Visibility.Visible;
                CodeBox.Password = string.Empty;
                return;
            }

            args.Cancel = false;
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
