# Development MSIX signing

Development signing is separate from production signing. The development scripts create or reuse a code-signing certificate in the current user's Windows certificate store whose Subject exactly matches `Package.appxmanifest`'s Publisher. They export only the public `.cer`; no private key or certificate password is written to the repository.

On the Windows build/test machine, after downloading the unsigned MSIX, run:

```powershell
.\deployment\windows\scripts\sign-msix-dev.ps1 -MsixPath .\SecureKiosk.msix
.\deployment\windows\scripts\trust-dev-certificate.ps1 -MsixPath .\SecureKiosk.msix -CertificatePath .\SecureKiosk-development.cer
.\deployment\windows\scripts\verify-msix-signature.ps1 -MsixPath .\SecureKiosk.msix -CertificatePath .\SecureKiosk-development.cer
```

The trust script requires elevation and imports only a current, code-signing certificate whose Subject exactly matches the MSIX manifest Publisher into `LocalMachine\TrustedPeople`. Test installation must be performed on Windows; an unsigned GitHub artifact cannot be installed until it is signed and the intended development certificate is trusted.

# Exit credential provisioning

Publish the administrator-only `SecureKiosk.CredentialTool` separately from the single-project MSIX package. Run it from an elevated Windows console:

```powershell
SecureKiosk.CredentialTool.exe provision
SecureKiosk.CredentialTool.exe status
SecureKiosk.CredentialTool.exe rotate
```

The utility accepts and confirms exactly four numeric digits, stores only a DPAPI-protected salted verifier, and prints no credential material. `setup-kiosk.ps1` refuses to configure kiosk mode unless the utility reports a readable provisioned credential.

For DEBUG builds only, a developer may set `SECUREKIOSK_DEV_EXIT_CODE` to a four-digit test value, for example `5013`. Release builds do not register this fallback and always require a provisioned credential.
