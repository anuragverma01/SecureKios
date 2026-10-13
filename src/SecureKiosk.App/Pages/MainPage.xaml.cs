using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SecureKiosk.App.Components;
using SecureKiosk.App.ViewModels;

namespace SecureKiosk.App.Pages;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        DataContext = new MainViewModel(this);
    }

    private async void OnMainExitSubmit(object sender, RoutedEventArgs e)
    {
        var enteredCode = MainExitCodeBox.Password;

        MainExitErrorText.Visibility = Visibility.Collapsed;

        // The existing exit dialog owns the kiosk cleanup and authorized-exit flow.
        // Verify via the same shared routine, rather than duplicating cleanup here.
        var accepted = await ExitCodeDialog.TryAuthorizedExitAsync(enteredCode);

        if (!accepted)
        {
            MainExitErrorText.Text = "The administrator exit code is invalid.";
            MainExitErrorText.Visibility = Visibility.Visible;
            MainExitCodeBox.Password = string.Empty;
            MainExitCodeBox.Focus(FocusState.Programmatic);
        }
    }
}
