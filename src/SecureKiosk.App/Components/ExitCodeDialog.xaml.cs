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
