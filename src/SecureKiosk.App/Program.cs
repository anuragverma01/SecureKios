using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SecureKiosk.Core.Interfaces;
using SecureKiosk.Core.Security;
using SecureKiosk.Infrastructure.Security;

namespace SecureKiosk.App;

public static partial class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    private const int ATTACH_PARENT_PROCESS = -1;

    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal) && args[0] != "--dev-test")
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            try
            {
                return await RunCliAsync(args).ConfigureAwait(false);
            }
            finally
            {
                FreeConsole();
            }
        }

        // Initialize WinUI 3 XAML Application
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return Environment.ExitCode;
    }

    private static async Task<int> RunCliAsync(string[] args)
    {
        var services = App.ConfigureServices();
        var kioskService = services.GetRequiredService<IWindowsKioskService>();
        var credentialStore = services.GetRequiredService<ISecureCredentialStore>();

        string command = args[0].ToLowerInvariant();
        switch (command)
        {
            case "--status":
                return await ShowStatusAsync(kioskService, credentialStore).ConfigureAwait(false);

            case "--configure-kiosk":
                string user = GetArgument(args, "--user") ?? Environment.UserName;
                return await ConfigureKioskAsync(kioskService, credentialStore, user).ConfigureAwait(false);

            case "--remove-kiosk":
                string? targetUser = GetArgument(args, "--user");
                return await RemoveKioskAsync(kioskService, targetUser).ConfigureAwait(false);

            case "--provision-exit-code":
                return await ProvisionExitCodeAsync(credentialStore).ConfigureAwait(false);

            case "--help":
            case "-h":
            case "/?":
                ShowHelp();
                return 0;

            default:
                Console.Error.WriteLine($"Unknown command: {args[0]}");
                ShowHelp();
                return 1;
        }
    }

    private static async Task<int> ShowStatusAsync(IWindowsKioskService kioskService, ISecureCredentialStore credentialStore)
    {
        var status = await kioskService.GetStatusAsync().ConfigureAwait(false);
        var cred = await credentialStore.ReadAsync().ConfigureAwait(false);

        Console.WriteLine("=================================================");
        Console.WriteLine("          SecureKiosk Configuration Status       ");
        Console.WriteLine("=================================================");
        Console.WriteLine($"Windows Edition            : {status.WindowsEdition}");
        Console.WriteLine($"Kiosk Configured           : {(status.IsConfigured ? "YES" : "NO")}");
        Console.WriteLine($"Kiosk Shell Type           : {status.ShellType ?? "None"}");
        Console.WriteLine($"Configured User            : {status.ConfiguredUser ?? "None"}");
        Console.WriteLine($"Shell Path                 : {status.ShellPath ?? "None"}");
        Console.WriteLine($"Admin Credential Provisioned: {(cred is not null ? "YES" : "NO (Exit will be rejected)")}");
        Console.WriteLine($"Status Details             : {status.Message}");
        Console.WriteLine("=================================================");
        return 0;
    }

    private static async Task<int> ConfigureKioskAsync(IWindowsKioskService kioskService, ISecureCredentialStore credentialStore, string user)
    {
        if (!IsAdministrator())
        {
            Console.Error.WriteLine("ERROR: Administrator privileges are required to configure Windows Kiosk mode.");
            Console.Error.WriteLine("Please re-run this command from an elevated PowerShell or Command Prompt.");
            return 1;
        }

        // Check if an administrator exit credential exists; if not, prompt immediately
        var cred = await credentialStore.ReadAsync().ConfigureAwait(false);
        if (cred is null)
        {
            Console.WriteLine("Notice: No administrator exit credential is provisioned.");
            Console.WriteLine("You must set a 4-digit administrator exit code before enabling kiosk mode.");
            Console.WriteLine();
            int provisionResult = await ProvisionExitCodeAsync(credentialStore).ConfigureAwait(false);
            if (provisionResult != 0)
            {
                Console.Error.WriteLine("Aborting kiosk configuration: valid exit credential is required.");
                return 1;
            }
        }

        Console.WriteLine($"Configuring SecureKiosk as the dedicated kiosk shell for user '{user}'...");
        var result = await kioskService.ConfigureKioskAsync(user).ConfigureAwait(false);
        if (result.Success)
        {
            Console.WriteLine();
            Console.WriteLine("SUCCESS: " + result.Message);
            Console.WriteLine("When the user logs in, SecureKiosk will start automatically with no desktop access.");
            Console.WriteLine("To activate, sign out of this session or restart the workstation.");
            return 0;
        }
        else
        {
            Console.Error.WriteLine("FAILED: " + result.Message);
            return 1;
        }
    }

    private static async Task<int> RemoveKioskAsync(IWindowsKioskService kioskService, string? user)
    {
        if (!IsAdministrator())
        {
            Console.Error.WriteLine("ERROR: Administrator privileges are required to remove Windows Kiosk mode.");
            Console.Error.WriteLine("Please re-run this command from an elevated session.");
            return 1;
        }

        Console.WriteLine("Removing SecureKiosk configuration and restoring standard Windows desktop...");
        var result = await kioskService.RemoveKioskAsync(user).ConfigureAwait(false);
        if (result.Success)
        {
            Console.WriteLine("SUCCESS: " + result.Message);
            Console.WriteLine("Explorer has been restored as the default desktop shell.");
            return 0;
        }
        else
        {
            Console.Error.WriteLine("FAILED: " + result.Message);
            return 1;
        }
    }

    private static async Task<int> ProvisionExitCodeAsync(ISecureCredentialStore credentialStore)
    {
        if (!IsAdministrator())
        {
            Console.Error.WriteLine("ERROR: Administrator privileges are required to provision DPAPI credentials.");
            return 1;
        }

        try
        {
            string first = ReadMaskedCode("Enter a new 4-digit administrator exit code: ");
            string confirmation = ReadMaskedCode("Confirm the new 4-digit administrator exit code: ");

            try
            {
                if (!CodesMatch(first, confirmation))
                {
                    Console.Error.WriteLine("ERROR: Credential confirmation did not match.");
                    return 1;
                }

                await credentialStore.WriteAsync(CredentialFactory.Create(first)).ConfigureAwait(false);
                Console.WriteLine("SUCCESS: Administrator exit code provisioned successfully.");
                return 0;
            }
            finally
            {
                first = string.Empty;
                confirmation = string.Empty;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: Credential operation failed: {ex.Message}");
            return 1;
        }
    }

    private static string ReadMaskedCode(string prompt)
    {
        Console.Write(prompt);
        var characters = new List<char>(ExitCodePolicy.Length);
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                var code = new string([.. characters]);
                if (!ExitCodePolicy.IsValid(code))
                {
                    throw new InvalidOperationException("Exit code must be exactly four digits (0-9).");
                }
                return code;
            }
            if (key.Key == ConsoleKey.Backspace && characters.Count > 0)
            {
                characters.RemoveAt(characters.Count - 1);
                Console.Write("\b \b");
                continue;
            }
            if (key.KeyChar is >= '0' and <= '9' && characters.Count < ExitCodePolicy.Length)
            {
                characters.Add(key.KeyChar);
                Console.Write('*');
            }
        }
    }

    private static bool CodesMatch(string first, string second)
    {
        var firstBytes = Encoding.UTF8.GetBytes(first);
        var secondBytes = Encoding.UTF8.GetBytes(second);
        try
        {
            return CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(firstBytes);
            CryptographicOperations.ZeroMemory(secondBytes);
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string? GetArgument(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("SecureKiosk — Production Windows Kiosk Application");
        Console.WriteLine("Usage:");
        Console.WriteLine("  SecureKiosk.exe                             Launch in kiosk mode");
        Console.WriteLine("  SecureKiosk.exe --status                    Display current kiosk configuration & credential status");
        Console.WriteLine("  SecureKiosk.exe --configure-kiosk [--user <name>] Configure kiosk shell for designated user");
        Console.WriteLine("  SecureKiosk.exe --remove-kiosk [--user <name>]    Remove kiosk shell and restore Explorer");
        Console.WriteLine("  SecureKiosk.exe --provision-exit-code       Provision or rotate 4-digit administrator exit code");
        Console.WriteLine("  SecureKiosk.exe --dev-test                  Run in non-enforcing development test mode");
        Console.WriteLine("  SecureKiosk.exe --help                      Show this help message");
    }
}
