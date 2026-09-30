# Changelog

This file records notable changes to UMPK.

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
