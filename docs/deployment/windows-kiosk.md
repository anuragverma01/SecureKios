# Windows kiosk deployment

The supported product target is Windows 10/11 Enterprise, Enterprise LTSC, Education, IoT Enterprise, or IoT Enterprise LTSC. Shell Launcher is not supported on Windows Pro. Assigned Access single-app kiosk supports additional editions, but a WinUI desktop app uses Shell Launcher in this product and Shell Launcher and `KioskModeApp` must not be configured together.

Authoritative references: [Shell Launcher overview](https://learn.microsoft.com/windows/configuration/assigned-access/shell-launcher), [Shell Launcher XML configuration](https://learn.microsoft.com/windows/configuration/assigned-access/shell-launcher/configuration-file), [Assigned Access CSP](https://learn.microsoft.com/windows/client-management/mdm/assignedaccess-csp), and [Keyboard Filter](https://learn.microsoft.com/windows/configuration/keyboard-filter/).

1. Publish and sign the WinUI app on Windows.
2. Create a standard local kiosk account and sign in once if required by the chosen image policy.
3. From an elevated PowerShell console run `scripts/install.ps1 -PublishRoot <signed-publish-directory> -KioskUser <user>`. This copies the signed app and applies Shell Launcher configuration.
5. Configure Keyboard Filter and application allowlisting through the organization’s supported policy/MDM/GPO process. Do not implement keyboard interception in SecureKiosk.
6. Run `scripts/validate-kiosk.ps1`, then reboot and execute the manual validation matrix.

The scripts use the documented Assigned Access MDM bridge and Shell Launcher XML. They do not silently fall back to Startup-folder persistence. Configuration changes take effect after sign-in/restart.
