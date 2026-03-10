# CLAUDE.md

Repository guidance for coding agents.

## Project

OpenKSeF Mobile is a .NET 10 MAUI app for KSeF invoice sync and browsing on Android and iOS.

Main projects:
- `src/OpenKSeF.Mobile` - MAUI app (Android + iOS)
- `src/OpenKSeF.Mobile.Tests` - xUnit unit tests (net8.0, no device required)
- `src/OpenKSeF.Mobile.E2E.Shared` - shared Appium E2E infrastructure
- `src/OpenKSeF.Mobile.E2E.Android` - Android E2E tests (NUnit + Appium WebDriver)

Solution file: `src/OpenKSeF.Mobile.slnx` (.slnx format, VS 17.10+ / .NET 9+)

## Architecture

The app is fully self-contained -- it has **no ProjectReference** to any backend project. It communicates with the [open-ksef backend](https://github.com/OpenKSeF/open-ksef) exclusively over:
- REST API (`ApiService.cs`)
- Keycloak OIDC (`AuthService.cs`, `WebAuthenticatorBrowser.cs`)
- SignalR hub for real-time invoice notifications

Key patterns:
- MVVM with CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`)
- Dependency injection via `MauiProgram.cs`
- SQLite local cache via `LocalDbService.cs` (sqlite-net-pcl)
- Server settings (base URL) stored in `ServerSettingsService.cs`

## Code change rules
- All code in this repo is active. There is nothing to avoid or tiptoe around.
- When implementing changes, modify existing files in-place. Do not create parallel implementations.
- If a ViewModel, Service, or View already handles similar logic, refactor it instead of adding a new one.
- Delete unused code after refactoring.

## Backend dependency

E2E tests and running the app locally require the backend stack. Start it from the [open-ksef](https://github.com/OpenKSeF/open-ksef) repo:

```powershell
./scripts/dev-env-up.ps1        # starts Docker, ngrok, Keycloak, provisions test user
./scripts/dev-env-up.ps1 -SkipNgrok  # if no Android emulator needed
```

### URLs (after backend dev-env-up)

| Service | URL |
|---------|-----|
| Gateway (portal+API+auth) | http://localhost:8080 |
| Keycloak admin console | http://localhost:8082/auth/admin |
| API Swagger | http://localhost:8081/swagger |
| HTTPS via ngrok (required for Android OIDC) | printed by dev-env-up.ps1 |

### Test credentials

| Account | Username | Password |
|---------|----------|----------|
| Keycloak admin | `admin` | `admin` |
| E2E test user | `testuser` | `Test1234!` |
| Test NIP | `1111111111` | — |

## Common commands

```bash
# Build all projects
dotnet build src/OpenKSeF.Mobile.slnx

# Unit tests (no device needed)
dotnet test src/OpenKSeF.Mobile.Tests/OpenKSeF.Mobile.Tests.csproj

# Run on Android emulator
dotnet build src/OpenKSeF.Mobile/OpenKSeF.Mobile.csproj -f net10.0-android -t:Run

# Android E2E (requires backend running + ngrok)
./scripts/setup-android-e2e.ps1                        # prepare emulator + Appium
./scripts/run-mobile-e2e.ps1                           # smoke + login tests (~30s)
./scripts/run-mobile-e2e.ps1 -Filter "Category=Regression"  # full suite
```

## MCP servers for agent testing

Configured in `.cursor/mcp.json`. Connects to the local Docker stack from the backend repo.

| Server | Purpose |
|--------|---------|
| **playwright** | Browse portal UI for integration verification |
| **postgres** | Read-only SQL on `openksef` database |
| **keycloak** | Manage Keycloak users/clients |
| **appium-mcp** | Drive MAUI Android app on emulator |
| **context7** | Look up library documentation on demand |

### Agent testing workflow

1. **Start backend**: `./scripts/dev-env-up.ps1` from the open-ksef backend repo
2. **Run unit tests**: `dotnet test src/OpenKSeF.Mobile.Tests/`
3. **Run E2E (preferred)**: `./scripts/run-mobile-e2e.ps1 -Filter "Category=Login"` (~30s)
4. **Interactive Appium**: use Appium MCP only for new UI features without existing tests
5. **Check docs**: use Context7 MCP for MAUI API references

## NuGet feed

`src/nuget.config` references the CIRFMF GitHub Packages feed. Credentials are not stored in the repo. Configure once:

```bash
dotnet nuget update source CIRFMF \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_GITHUB_PAT \
  --store-password-in-clear-text \
  --configfile src/nuget.config
```

## Skills

`.cursor/skills/` contains 35 skills:
- **34 .NET MAUI skills** -- auto-detected by Cursor for MAUI topics (authentication, navigation, SQLite, theming, accessibility, etc.)
- **android-e2e-appium** -- E2E testing for the MAUI Android app. Preferred: `./scripts/run-mobile-e2e.ps1`. Fall back to Appium MCP only for new features.

## Push notifications

Firebase is optional. If `src/OpenKSeF.Mobile/Platforms/Android/google-services.json` exists, the build automatically includes `Xamarin.Firebase.Messaging` and defines `FIREBASE_ENABLED`. Without it, the app builds normally without push support.

## Debugging playbook

| Problem | How to debug |
|---------|-------------|
| Build fails | Check .NET 10 SDK + MAUI workload: `dotnet workload list` |
| CIRFMF restore fails | Re-add credentials to nuget.config (see above) |
| App can't login | Verify ngrok is running; URL must be HTTPS |
| OIDC redirect error | Run `dev-env-up.ps1` again from backend repo to refresh Keycloak redirect URIs |
| E2E tests fail | Run `./scripts/run-mobile-e2e.ps1` -- it prints preflight failures with fix commands |
| Appium can't find element | Check AutomationId in `Views/*.xaml`; run `AutomationIdCoverageTests` |
| App crashes on emulator | Check `adb logcat` for exceptions |
