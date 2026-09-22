# Architecture

`SecureKiosk.Core` contains no WinUI or Windows APIs. `IExitAuthorizationService` owns verification and throttling. `ISecureCredentialStore` and `IAuditService` are boundaries so storage and logging can be replaced without changing UI behavior.

`SecureKiosk.Infrastructure` implements DPAPI LocalMachine credential storage, JSON-line audit logging, configuration validation, and the Windows lifecycle adapter. `SecureKiosk.App` is a thin WinUI/MVVM shell. The window uses the Windows App SDK `AppWindow` full-screen presenter and cancels the normal window closing event; the administrator exit dialog is the only intentional application exit path.

Shell Launcher starts the desktop app after kiosk-user sign-in and restarts it after an ordinary process exit. The app sets exit code `1` only after successful administrator authorization; the supplied Shell Launcher XML maps that code to `DoNothing`, returning control to the administrator/recovery flow. It is not a complete allowlist by itself; use supported Assigned Access policy/application restriction controls and Keyboard Filter on the target image.
