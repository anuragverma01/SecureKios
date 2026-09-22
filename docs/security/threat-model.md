# Security notes

- Production credentials are not compiled into the app and are never written to audit logs.
- The verifier uses a random 32-byte salt, PBKDF2-HMAC-SHA256, and fixed-time comparison. DPAPI protects the serialized verifier at rest.
- Invalid attempts are throttled in memory. A production deployment should persist lockout telemetry and coordinate it with the device-management policy without storing the code.
- The app does not claim to defeat local administrators, WinRE, safe mode, physical access, or secure-attention sequences. Those are administrator/device security boundaries.
