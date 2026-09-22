using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SecureKiosk.App.Pages;
using WinRT.Interop;

namespace SecureKiosk.App.Windows;

public sealed class KioskWindow : Window
{
    public KioskWindow()
    {
        Content = new MainPage();
        var windowHandle = WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(windowHandle));
        appWindow.Closing += (_, e) => e.Cancel = true;
        appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }
}
