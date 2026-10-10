# Changelog

This file records notable changes to UMPK.

## 0.9.0-beta.6 - 2026-10-10

- Support encrypted offline login without session authentication on Minecraft 1.20.5+ and report authentication separately from encryption.
- Renew Microsoft access tokens during certificate renewal, using the Minecraft token expiry and retaining refresh tokens when no replacement is returned.
- Refresh certificates at their renewal time and serialize concurrent session refreshes.

## 0.9.0-beta.5 - 2026-10-09

- Use the platform AES-CFB8 provider for ARM32 processes while preserving the existing cipher selection on other architectures and the explicit software fallback.

## 0.9.0-beta.4 - 2026-09-30

- Replace the built-in HTTP CONNECT and SOCKS negotiation code with QuickProxyNet while preserving UMPK's proxy factory API and transport lifetime.
- Preserve graceful proxy transport shutdown across operating systems instead of inheriting QuickProxyNet's abortive socket-close default.

## 0.9.0-beta.3 - 2026-09-30

- Add weather snapshots and change events, dropped-item stack snapshots, and caller-supplied verified-navigation options for MCC integrations.
- Correct the NuGet package readme to list Minecraft Java 26.3 (protocol 777) as the newest supported version.

## 0.9.0-beta.2 - 2026-09-22

- Add Minecraft Java 26.3 (protocol 777) support and update affected packet codecs.

## 0.9.0-beta.1 - 2026-09-12

- First public beta release.
