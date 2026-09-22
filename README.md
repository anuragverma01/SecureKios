# SecureKiosk

SecureKiosk is a native WinUI 3 / .NET 10 Windows kiosk application with a separate, testable security core and administrator-only Windows provisioning scripts.

The production kiosk profile targets Windows Enterprise, Enterprise LTSC, Education, IoT Enterprise, or IoT Enterprise LTSC. The desktop shell is configured with Shell Launcher; Keyboard Filter and application restriction policy are separate supported Windows controls. Windows-specific behavior must be validated on a supported Windows installation.

## Repository layout

- `src/SecureKiosk.App` - WinUI 3 presentation layer.
- `src/SecureKiosk.Core` - platform-neutral contracts, models, validation, and exit authorization.
- `src/SecureKiosk.Infrastructure` - protected credential storage, JSON configuration, audit logging, and Windows integration adapters.
- `tests` - cross-platform unit tests for the core and infrastructure abstractions.
- `deployment/windows` - administrator-only provisioning, validation, repair, and removal scripts.
- `docs` - architecture, security, deployment, recovery, and validation guidance.

## Development

The ordinary business/security projects and tests are intended to be editable on Ubuntu. A Windows machine with the .NET 10 SDK, Windows App SDK workload/packages, and a supported Windows edition is required to build and validate the WinUI application and kiosk provisioning.

```bash
dotnet restore SecureKiosk.sln
dotnet build SecureKiosk.sln --configuration Release
dotnet test SecureKiosk.sln --configuration Release
```

See `docs/deployment/windows-kiosk.md` before applying kiosk configuration. Do not run provisioning scripts on a production machine until the recovery procedure has been tested.
