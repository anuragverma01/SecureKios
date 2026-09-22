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
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter) => await execute();
}
