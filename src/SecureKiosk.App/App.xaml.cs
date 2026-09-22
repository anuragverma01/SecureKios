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
        Services = ConfigureServices();
    }

    public static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
#if DEBUG
        var developmentCode = Environment.GetEnvironmentVariable(DevelopmentExitCredential.EnvironmentVariableName);
        if (DevelopmentExitCredential.TryGetConfiguredCode(true, developmentCode, out var configuredDevelopmentCode))
        {
            services.AddSingleton<ISecureCredentialStore>(_ => new DevelopmentCredentialStore(configuredDevelopmentCode));
        }
        else
#endif
        {
            services.AddSingleton<ISecureCredentialStore>(_ => new DpapiCredentialStore(CredentialPaths.GetDefaultPath()));
        }

        var dataRoot = Path.GetDirectoryName(CredentialPaths.GetDefaultPath())!;
        services.AddSingleton<IAuditService>(_ => new JsonLineAuditService(Path.Combine(dataRoot, "audit.jsonl")));
        services.AddSingleton<IExitAuthorizationService, ExitAuthorizationService>();
        services.AddSingleton<IWindowsKioskService, WindowsKioskService>();
        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new KioskWindow();
        _window.Activate();
    }
}
