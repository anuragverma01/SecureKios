using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SecureKiosk.App.Security;
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
        var credentialPath = CredentialPaths.GetDefaultPath();

        // PRODUCTION path: if a DPAPI credential file has been provisioned via
        //   SecureKiosk.exe --provision-exit-code
        // then always use it — this is the secure, machine-bound credential store.
        if (File.Exists(credentialPath))
        {
            services.AddSingleton<ISecureCredentialStore>(_ => new DpapiCredentialStore(credentialPath));
        }
        else
        {
            // DEVELOPMENT / FIRST-RUN path: no credential file exists yet.
            // Check for a development exit code (env var or built-in fallback).
            // This allows the Administrator Exit to work on dev/test machines
            // without first running --provision-exit-code.
            var envCode = Environment.GetEnvironmentVariable(DevelopmentExitCredential.EnvironmentVariableName);
            if (DevelopmentExitCredential.TryGetConfiguredCode(true, envCode, out var devCode))
            {
                services.AddSingleton<ISecureCredentialStore>(_ => new DevelopmentCredentialStore(devCode));
            }
            else
            {
                // No credential file and no development fallback — register the DPAPI store
                // anyway. All codes will be rejected (ReadAsync returns null).
                services.AddSingleton<ISecureCredentialStore>(_ => new DpapiCredentialStore(credentialPath));
            }
        }

        var dataRoot = Path.GetDirectoryName(credentialPath)!;
        services.AddSingleton<IAuditService>(_ => new JsonLineAuditService(Path.Combine(dataRoot, "audit.jsonl")));
        services.AddSingleton<IExitAuthorizationService, ExitAuthorizationService>();
        services.AddSingleton<IWindowsKioskService, WindowsKioskService>();
        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string? pkgFamily = null;
        try
        {
            pkgFamily = global::Windows.ApplicationModel.Package.Current?.Id?.FamilyName;
        }
        catch { }

        // 1. Immediately apply synchronous registry lockdown and Windows policies
        KioskPolicyManager.ApplyPolicies(pkgFamily);

        // 2. Start continuous process watchdog to terminate unauthorized apps (taskmgr, cmd, powershell, etc.)
        KioskSecurityWatchdog.Start();

        // 3. Create and activate fullscreen topmost kiosk window
        _window = new KioskWindow();
        _window.Activate();

        // 4. Request UWP startup task enablement in isolated background task
        _ = EnsureStartupTaskEnabledAsync();
    }

    private static async Task EnsureStartupTaskEnabledAsync()
    {
        try
        {
            var task = await global::Windows.ApplicationModel.StartupTask.GetAsync("SecureKioskStartupTask");
            if (task.State != global::Windows.ApplicationModel.StartupTaskState.Enabled &&
                task.State != global::Windows.ApplicationModel.StartupTaskState.DisabledByPolicy)
            {
                await task.RequestEnableAsync();
            }
        }
        catch
        {
            // Ignore in unpackaged test environments or unsupported hosts
        }
    }
}
