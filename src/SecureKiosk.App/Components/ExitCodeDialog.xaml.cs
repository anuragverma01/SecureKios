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
        var service = App.Services.GetRequiredService<IExitAuthorizationService>();
        var result = await service.AuthorizeAsync(CodeBox.Password);
        if (result.Status == ExitAuthorizationStatus.Authorized)
        {
            await App.Services.GetRequiredService<IWindowsKioskService>().RequestCleanExitAsync(ExitCodes.AuthorizedExit);
            args.Cancel = false;
            Application.Current.Exit();
            return;
        }
        args.Cancel = true;
        ErrorText.Text = result.Status == ExitAuthorizationStatus.RateLimited
            ? "Too many attempts. Try again later."
            : "The exit code is invalid.";
        ErrorText.Visibility = Visibility.Visible;
        CodeBox.Password = string.Empty;
    }
}
