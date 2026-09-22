using System.Windows.Input;
using Microsoft.UI.Xaml;
using SecureKiosk.App.Components;
using SecureKiosk.App.Pages;

namespace SecureKiosk.App.ViewModels;

public sealed class MainViewModel
{
    public bool IsBusy => false;
    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    public ICommand ShowExitCommand { get; }

    public MainViewModel(MainPage page) => ShowExitCommand = new DelegateCommand(async () => await ExitCodeDialog.ShowAsync(page.XamlRoot));
}

public sealed class DelegateCommand(Func<Task> execute) : ICommand
{
    // This command is permanently enabled, so WinUI never needs a CanExecute refresh.
    // ICommand still requires the event; explicit no-op accessors avoid an unused backing field.
    event EventHandler? ICommand.CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter) => await execute();
}
