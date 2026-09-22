# Administrator recovery

Maintain a separate administrator account and test it before deployment. If SecureKiosk is unavailable, use the administrator recovery path at the physical console (not Remote Desktop), sign in with the administrator account, and run `deployment/windows/scripts/remove-kiosk.ps1` elevated. Restart, repair or replace the signed application, then re-run setup.

To change the exit credential, use the administrator credential-management procedure to generate a new salted verifier and replace the DPAPI-protected credential file with controlled ACLs. Never put a credential in a script, source file, command-line argument, or log. The first implementation must ship a signed administrator-only credential utility before production rollout.

Ctrl+Alt+Delete and other secure-attention sequences are Windows-reserved. Do not attempt to intercept them from the app. Keyboard Filter and Windows policy determine the supported behavior. Keep offline recovery media and an image rollback procedure.
