# OpenKSeF Mobile

[![License: Elastic-2.0](https://img.shields.io/badge/License-Elastic--2.0-blue.svg)](LICENSE)

Android and iOS mobile app for [OpenKSeF](https://open-ksef.pl) — browse and manage KSeF invoices from your phone.

Built with [.NET MAUI](https://dotnet.microsoft.com/apps/maui) targeting **net10.0-android** and **net10.0-ios**.

## Features

- OIDC login via Keycloak (WebAuthenticator)
- Browse and search KSeF invoices
- Tenant (company/NIP) onboarding with QR code setup
- Push notifications for new invoices (Firebase / APNS)
- Offline invoice cache (SQLite)
- KSeF real-time updates via SignalR

## Documentation

Full documentation is at [open-ksef.pl](https://open-ksef.pl).

## Requirements

| Tool | Version |
|------|---------|
| .NET SDK | 10.0.x |
| MAUI Android workload | `dotnet workload install maui-android` |
| MAUI iOS workload (macOS) | `dotnet workload install maui-ios` |
| Android Studio | For Android emulator |
| Xcode (macOS) | For iOS simulator |

### NuGet: CIRFMF private feed

The MAUI app uses packages from the CIRFMF GitHub Packages feed. Add your credentials once:

```bash
dotnet nuget update source CIRFMF \
  --source https://nuget.pkg.github.com/CIRFMF/index.json \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_GITHUB_PAT \
  --store-password-in-clear-text \
  --configfile src/nuget.config
```

A GitHub PAT with `read:packages` scope is required. See [GitHub docs](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry#authenticating-to-github-packages).

## Build

```bash
# All projects (solution)
dotnet build src/OpenKSeF.Mobile.slnx

# Android only
dotnet build src/OpenKSeF.Mobile/OpenKSeF.Mobile.csproj -f net10.0-android

# iOS only (macOS)
dotnet build src/OpenKSeF.Mobile/OpenKSeF.Mobile.csproj -f net10.0-ios
```

## Tests

### Unit tests (no device required)

```bash
dotnet test src/OpenKSeF.Mobile.Tests/OpenKSeF.Mobile.Tests.csproj
```

### Android E2E tests (requires backend + emulator)

The E2E tests connect to a running backend. Start the backend first from the [open-ksef](https://github.com/OpenKSeF/open-ksef) repo, then:

```powershell
# Prepare Android emulator + Appium + install app
./scripts/setup-android-e2e.ps1

# Run tests (smoke + login by default)
./scripts/run-mobile-e2e.ps1

# Full regression suite
./scripts/run-mobile-e2e.ps1 -Filter "Category=Regression"
```

## Backend

This app connects to the [open-ksef](https://github.com/OpenKSeF/open-ksef) backend (API + Keycloak). Run the backend locally with:

```powershell
# From the open-ksef repo:
./scripts/dev-env-up.ps1
```

The app communicates with the backend via:
- REST API at `/api/`
- Keycloak OIDC at `/auth/`
- SignalR hub for real-time invoice notifications

## Push notifications (optional)

Firebase Cloud Messaging is enabled automatically when `src/OpenKSeF.Mobile/Platforms/Android/google-services.json` is present. Without it, the app builds without push notification support. See [docs/push-notifications-setup.md](https://open-ksef.pl/docs/push-notifications-setup) for setup instructions.

## Release

Releases are built automatically by the [release workflow](.github/workflows/release.yml) when a version tag is pushed:

```bash
git tag v1.0.0 && git push origin v1.0.0
```

Required repository secrets:

| Secret | Description |
|--------|-------------|
| `NUGET_CIRFMF_PAT` | GitHub PAT for CIRFMF NuGet feed |
| `ANDROID_KEYSTORE_BASE64` | Base64-encoded Android keystore |
| `ANDROID_KEY_ALIAS` | Keystore key alias |
| `ANDROID_KEY_PASSWORD` | Key password |
| `ANDROID_STORE_PASSWORD` | Store password |
| `APPLE_CERTIFICATE_BASE64` | Apple distribution certificate (optional) |
| `APPLE_CERTIFICATE_PASSWORD` | Certificate password (optional) |
| `APPLE_PROVISIONING_PROFILE_BASE64` | Provisioning profile (optional) |
| `APPLE_SIGNING_IDENTITY` | Signing identity (optional) |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[Elastic License 2.0](LICENSE) — free to use and modify; commercial hosting requires a license. See [COMMERCIAL.md](COMMERCIAL.md).
