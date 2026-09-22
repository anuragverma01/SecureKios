using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SecureKiosk.App.Windows;
using SecureKiosk.Core.Interfaces;
using SecureKiosk.Core.Security;
using SecureKiosk.Infrastructure.Logging;
using SecureKiosk.Infrastructure.Security;
using SecureKiosk.Infrastructure.Windows;

namespace SecureKiosk.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private Window? _window;

    public App()
    {
        InitializeComponent();
        var services = new ServiceCollection();
        var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SecureKiosk");
        services.AddSingleton<ISecureCredentialStore>(_ => new DpapiCredentialStore(Path.Combine(dataRoot, "credential.bin")));
        services.AddSingleton<IAuditService>(_ => new JsonLineAuditService(Path.Combine(dataRoot, "audit.jsonl")));
        services.AddSingleton<IExitAuthorizationService, ExitAuthorizationService>();
        services.AddSingleton<IWindowsKioskService, WindowsKioskService>();
        Services = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new KioskWindow();
        _window.Activate();
    }
}
