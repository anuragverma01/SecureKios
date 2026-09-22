using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using SecureKiosk.Core.Security;
using SecureKiosk.Infrastructure.Security;

namespace SecureKiosk.CredentialTool;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return Fail("This utility can only run on Windows.");
        if (!IsAdministrator()) return Fail("Run this administrator-only utility from an elevated console.");
        if (args.Length != 1 || args[0] is not ("provision" or "rotate" or "status"))
            return Fail("Usage: SecureKiosk.CredentialTool.exe [provision|rotate|status]");

        try
        {
            var store = new DpapiCredentialStore(CredentialPaths.GetDefaultPath());
            if (args[0] == "status")
            {
                var record = await store.ReadAsync().ConfigureAwait(false);
                Console.WriteLine(record is null ? "No administrator credential is provisioned." : "Administrator credential is provisioned and readable.");
                return record is null ? 2 : 0;
            }

            var first = ReadCode("Enter a new 4-digit administrator exit code: ");
            var confirmation = ReadCode("Confirm the new 4-digit administrator exit code: ");
            try
            {
                if (!CodesMatch(first, confirmation)) return Fail("Credential confirmation did not match.");
                await store.WriteAsync(CredentialFactory.Create(first)).ConfigureAwait(false);
                Console.WriteLine(args[0] == "rotate" ? "Administrator credential rotated." : "Administrator credential provisioned.");
                return 0;
            }
            finally
            {
                first = string.Empty;
                confirmation = string.Empty;
            }
        }
        catch (Exception)
        {
            return Fail("Credential operation failed. Verify administrator access and the SecureKiosk credential store.");
        }
    }

    private static string ReadCode(string prompt)
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
                if (!ExitCodePolicy.IsValid(code)) throw new InvalidOperationException("Exit code must be exactly four digits.");
                return code;
            }
            if (key.Key == ConsoleKey.Backspace && characters.Count > 0)
            {
                characters.RemoveAt(characters.Count - 1);
                continue;
            }
            if (key.KeyChar is >= '0' and <= '9' && characters.Count < ExitCodePolicy.Length) characters.Add(key.KeyChar);
        }
    }

    private static bool CodesMatch(string first, string second)
    {
        var firstBytes = Encoding.UTF8.GetBytes(first);
        var secondBytes = Encoding.UTF8.GetBytes(second);
        try { return CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes); }
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

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
