<p>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://open-ksef.pl/img/mark-dark.svg">
    <img src="https://open-ksef.pl/img/mark.svg" alt="OpenKSeF mark" width="48">
  </picture>
</p>

# OpenKSeF Mobile

[![Build](https://github.com/open-ksef/open-ksef-mobile/actions/workflows/build.yml/badge.svg)](https://github.com/open-ksef/open-ksef-mobile/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/open-ksef/open-ksef-mobile)](https://github.com/open-ksef/open-ksef-mobile/releases/latest)
[![License: Elastic-2.0](https://img.shields.io/badge/License-Elastic--2.0-blue.svg)](LICENSE)

Android app for [OpenKSeF](https://github.com/open-ksef/open-ksef): invoices from Poland's National e-Invoice System (KSeF) on your phone. It connects to an OpenKSeF instance; it does not work without one.

`Android: plik APK na GitHubie, Google Play i iPhone jeszcze nie` (Android: an APK file on GitHub, not on Google Play or iPhone yet). Download: [latest release](https://github.com/open-ksef/open-ksef-mobile/releases/latest).

`Kod źródłowy jest publicznie dostępny` (the source code is publicly available) under the [Elastic License 2.0](LICENSE). `Niezależny projekt, niepowiązany z Ministerstwem Finansów` (an independent project, not affiliated with the Ministry of Finance). `Nie prowadzimy wersji hostowanej dla firm` (we run no hosted version for companies).

Guide in Polish: [open-ksef.pl/docs/aplikacja-mobilna](https://open-ksef.pl/docs/aplikacja-mobilna).

## Features

- Login through Keycloak on your instance, with a QR code from the portal to set the server address
- Received invoices: list, details, line items, offline cache (SQLite)
- Transfer details and a QR code for the banking app, both from the server
- Issuing invoices. Invoices for foreign companies are view-only here: issue them in the portal
- New invoice notifications: Firebase Cloud Messaging through the relay or your own Firebase project, plus SignalR while the app is connected

## Build

.NET 10 SDK, `dotnet workload install maui-android`, Android SDK.

```bash
dotnet build src/OpenKSeF.Mobile/OpenKSeF.Mobile.csproj -f net10.0-android
dotnet test src/OpenKSeF.Mobile.Tests/OpenKSeF.Mobile.Tests.csproj
```

The project also targets `net10.0-ios` so that CI can compile it. No iOS build is released.

Push is optional: Firebase is compiled in when `src/OpenKSeF.Mobile/Platforms/Android/google-services.json` is present. Setup: [push notifications](https://github.com/open-ksef/open-ksef/blob/main/docs/push-notifications-setup.md).

## End-to-end tests

They need a running backend from [open-ksef](https://github.com/open-ksef/open-ksef), `./scripts/dev-env-up.ps1` there, plus an emulator and Appium:

```powershell
./scripts/setup-android-e2e.ps1
./scripts/run-mobile-e2e.ps1                                # smoke + login
./scripts/run-mobile-e2e.ps1 -Filter "Category=Regression"  # full suite
```

## Release

A version tag runs the [release workflow](.github/workflows/release.yml): signed APK and AAB on the GitHub release, and the AAB on the Google Play internal track. Secrets: `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`, `ANDROID_STORE_PASSWORD`, `GOOGLE_SERVICES_JSON`, `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON`.

## Contributing and security

[CONTRIBUTING.md](CONTRIBUTING.md). Security reports: [SECURITY.md](SECURITY.md).

## License

[Elastic License 2.0](LICENSE). See [COMMERCIAL.md](COMMERCIAL.md).
