namespace SecureKiosk.Infrastructure.Security;

public static class CredentialPaths
{
    public static string GetDefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SecureKiosk",
        "credential.bin");
}
