using Microsoft.UI.Xaml.Controls;
using SecureKiosk.App.ViewModels;

namespace SecureKiosk.App.Pages;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        DataContext = new MainViewModel(this);
    }
}
