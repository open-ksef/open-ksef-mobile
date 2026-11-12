# CLAUDE.md

Repository guidance for coding agents.

## Project

OpenKSeF Mobile is a .NET 10 MAUI app for KSeF invoice sync and browsing. Android is released; iOS only compiles in CI.

Main projects:
- `src/OpenKSeF.Mobile` - MAUI app (targets `net10.0-android`, `net10.0-ios`)
- `src/OpenKSeF.Mobile.Tests` - xUnit unit tests (net10.0, no device required)
- `src/OpenKSeF.Mobile.E2E.Shared` - shared Appium E2E infrastructure
- `src/OpenKSeF.Mobile.E2E.Android` - Android E2E tests (NUnit + Appium WebDriver)

Solution file: `src/OpenKSeF.Mobile.slnx` (.slnx format, VS 17.10+ / .NET 9+)

## Architecture

The app is fully self-contained -- it has **no ProjectReference** to any backend project. It communicates with the [open-ksef backend](https://github.com/open-ksef/open-ksef) exclusively over:
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

## Architecture principles

### MVVM discipline

- **ViewModels** own all presentation logic, state, and commands. Use `[ObservableProperty]` for bindable state and `[RelayCommand]` for actions.
- **Views** are pure XAML. Code-behind should contain only navigation glue or platform-specific wiring -- never business logic or data transformation.
- **Services** handle data access, networking, and platform APIs. A ViewModel calls services; it never makes HTTP requests or database queries directly.

### Service boundaries

Each service has a single, clear responsibility:

| Service | Responsibility |
|---------|----------------|
| `ApiService` | HTTP calls to the backend REST API |
| `AuthService` | OIDC tokens (login, refresh) |
| `LocalDbService` | SQLite cache (read/write cached invoices) |
| `ServerSettingsService` | Base URL and server configuration |
| `NotificationHubService` | SignalR connection for live events |
| `DeviceTokenService` | FCM/APNs token registration |

Do not merge responsibilities across services. New features should fit into an existing service or justify a new one with a clear single purpose.

### Model sync with backend

- `Models/` contains DTOs that mirror the backend REST API contracts (`open-ksef` repo, `Api/Models/`).
- When the backend API changes a response shape, update the corresponding mobile model to match.
- Document any intentional deviation from the backend contract (e.g. `CachedInvoice` is a local-only SQLite entity, `QrSetupPayload` is client-only).
- `TenantDto` mirrors backend `TenantResponse`, `UpdateTenantRequest` sends `NotificationEmail` (backend PUT overwrites it, so the edit form round-trips the loaded value). `InvoiceDto` carries `IsPaid`/`PaidAt`.
- `TransferDetailsDto` mirrors `TransferDetailsResponse`. One deviation: `[JsonIgnore] ServerHasQrRules`, set by the `QrMessage` setter. System.Text.Json calls that setter for an explicit null and not for an absent key. v0.0.7 has no `qrMessage` key and draws a QR with no rules, also for EUR, so `QrCodeViewModel` ignores `qrCodeBase64` when the flag is false. The API must keep writing the key. Remove the guard once no supported server is older than the T-019 tag. Mobile tags follow the backend tag on prod.
- Pay and copy data come only from `GET .../invoices/{id}/transfer`; no payment payload is built on the client. `QRCoder` stays for KOD I (`IssuedInvoiceDetailsViewModel`).
- Issuing DTOs mirror `Api/Models/{IssuedInvoiceModels,SellerProfileModels,NipLookupModels,ValidationEnvelope}.cs` (contract `open-ksef/docs/issuing-api.md`); wire names match, type names keep the mobile `Dto` suffix (`PartyDto` = `IssuedInvoiceBuyerModel`, `SellerPartyDto`, `KsefSubmissionDto`). `SellerProfileDto` extends `SellerProfileRequest`, `IssuedInvoiceLineDto` extends `IssuedInvoiceLineRequest`. `GetSellerProfileAsync`/`LookupNipAsync` return `null` on 404 (seller-profile 404 = server without issuing). 400/422 bodies land in `ApiException.Validation`. `ApiException.Message` is shown to users: a `message`/`error` string from a 400/409/422 body, otherwise fixed text; other bodies are only logged, redacted. `IssuedInvoiceTotals` is the client totals preview and must match the server rounding rule.
- `IssuedInvoiceDto.Version` is the server's concurrency token, sent back as `?version=` on PUT, DELETE and issue. Null (server older than `61f0df7`, no `version` key) sends no query. The form keeps the version from its load or its own save, never a refetch; a screen sends the version it showed when the confirmation opened. A 409 on save shows the server message and offers to reload the form. A 409 on delete reloads the details and shows the server message. A 409 on issue from the form shows the server message with no reload. A 409 on issue from the details reloads them, then says `Faktura zmieniona` if the version moved and the draft can still be issued here, otherwise `Błąd` with the server message (number already used). Issue on an invoice that is not Draft or Rejected returns its current view, not a 409.
- Foreign invoices (REQ-0046) are view-only here. Response fields are mirrored with `WhenWritingNull`, so a domestic request stays byte-identical. Deviations: `IssuedInvoiceDraftRequest` has no `currency`, `exchangeRate`, `exchangeRateDate`; `SellerProfileRequest` has no foreign bank fields, because null keeps the stored value; no `ExchangeRateProposalResponse`. `IssuingFormat.IsPortalOnly` (kind not Company/Person, or non-PLN) blocks the draft form, save and issue. The list DTO has no rate, so `IssuedInvoiceRow.ForeignText` derives `np II` from `buyerIdType = VatUe`, otherwise `np I`, only for Accepted and Sending, where issue checks have enforced that pairing.
- Issued-invoice PDF: `Resources/Raw/pdf/` holds the unmodified CIRFMF ksef-pdf-generator 1.1.46 bundle (same SHA-256 as the portal vendor copy, pinned by a unit test) plus `index.html`. `IssuedInvoiceDetailsPage` creates a hidden `HybridWebView` on first use and passes it to the view model as `RenderPdf`; any failure shows the fallback (KOD I link + PDF w portalu), never a PDF without KOD I.

### SOLID checklist

- **Single Responsibility.** One ViewModel per page. One service per concern. One model per API contract.
- **Open/Closed.** Add new pages/features as new ViewModel+View pairs. Do not overload existing ViewModels with unrelated responsibilities.
- **Liskov Substitution.** Every service interface implementation (e.g. `IApiService`, `IAuthService`) must be safely interchangeable -- important for unit testing with mocks.
- **Interface Segregation.** Keep service interfaces focused. If `IApiService` grows too large, split by domain area (e.g. tenant methods, invoice methods).
- **Dependency Inversion.** ViewModels depend on service interfaces, never on concrete classes. All wiring goes through `MauiProgram.cs` DI registration.

## Backend dependency

E2E tests and running the app locally require the backend stack. Start it from the [open-ksef](https://github.com/open-ksef/open-ksef) repo:

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

# Unit tests (no device needed, ~0.3s)
dotnet test src/OpenKSeF.Mobile.Tests/OpenKSeF.Mobile.Tests.csproj

# Integration tests against backend (no emulator, ~1.5s)
dotnet test src/OpenKSeF.Mobile.E2E.Android/OpenKSeF.Mobile.E2E.Android.csproj --filter "Category=Integration"

# Run on Android emulator (Debug, installs + launches)
dotnet build src/OpenKSeF.Mobile/OpenKSeF.Mobile.csproj -f net10.0-android -t:Run

# Build standalone APK for sideloading (Release)
dotnet publish src/OpenKSeF.Mobile/OpenKSeF.Mobile.csproj -f net10.0-android -c Release -p:AndroidPackageFormat=apk -p:AndroidKeyStore=false

# Android E2E on emulator (requires Appium + backend)
dotnet test src/OpenKSeF.Mobile.E2E.Android/OpenKSeF.Mobile.E2E.Android.csproj --filter "Category=Smoke|Category=Login"
```

### Network addresses for testing

| Context | Server URL | Notes |
|---------|-----------|-------|
| Host (unit/integration tests, portal) | `http://localhost:8080` | Direct to Docker gateway |
| Android emulator (ROPC login) | `http://10.0.2.2:8080` | Emulator special IP for host localhost |
| Android emulator (Google/OIDC login) | ngrok HTTPS URL | OIDC redirect requires HTTPS |
| Physical device (any login) | ngrok HTTPS URL | Device can't reach localhost |

**Why ngrok?** Android OIDC (Google login via Keycloak) requires HTTPS redirect URIs. ROPC login (email/password) works over plain HTTP. For emulator E2E tests that only use ROPC, set the server URL to `http://10.0.2.2:8080` -- no ngrok needed.

### Test tiers

| Tier | Command | Duration | Requires |
|------|---------|----------|----------|
| Unit tests | `dotnet test src/OpenKSeF.Mobile.Tests/` | ~0.3s | Nothing |
| Integration tests | `dotnet test ... --filter "Category=Integration"` | ~1.5s | Docker backend |
| E2E Smoke+Login | `dotnet test ... --filter "Category=Smoke\|Category=Login"` | ~30s | Docker + emulator + Appium |
| E2E Regression | `dotnet test ... --filter "Category=Regression"` | ~5min | Docker + emulator + Appium |

Unit tests compile app sources as linked files (`OpenKSeF.Mobile.Tests.csproj` `Compile Include`) against `ViewModels/MauiStubs.cs`, never copies of app logic. ViewModels call `Shell.Current` statically, and real MAUI on plain `net10.0` hangs on `DisplayAlertAsync` without a handler, so a ProjectReference cannot run them. `TEST_CULTURE=en-US` (or `pl-PL`) runs the suite under that culture.

### Build gotchas

- **Debug APK**: .NET 10 MAUI Debug uses fast deployment (no standalone APK). Use `-t:Run` to install directly on emulator.
- **Release APK**: Do NOT pass `-p:PublishTrimmed=false` -- it breaks JNI bindings and the app crashes on launch (`MauiApplication.n_onCreate not found`). Let Release use default trimming.
- **Emulator server URL**: After installing, set `server_url` in shared_prefs to `http://10.0.2.2:8080`.

## MCP servers for agent testing

Configured in `.cursor/mcp.json` (Cursor only, gitignored, not in the repo). Claude Code reads `.mcp.json`; none is committed yet. Connects to the local Docker stack from the backend repo.

| Server | Purpose |
|--------|---------|
| **playwright** | Browse portal UI for integration verification |
| **postgres** | Read-only SQL on `openksef` database |
| **keycloak** | Manage Keycloak users/clients |
| **appium-mcp** | Drive MAUI Android app on emulator |
| **context7** | Look up library documentation on demand |

### Agent testing workflow

1. **Run unit tests first** (always, no deps): `dotnet test src/OpenKSeF.Mobile.Tests/`
2. **Run integration tests** (needs Docker): `dotnet test src/OpenKSeF.Mobile.E2E.Android/ --filter "Category=Integration"`
3. **If touching auth/login/API code**, run both above before building APK
4. **Emulator E2E** (needs emulator + Appium): `dotnet test src/OpenKSeF.Mobile.E2E.Android/ --filter "Category=Smoke|Category=Login"`
   - Set emulator server URL to `http://10.0.2.2:8080` (not ngrok, not localhost)
   - Use `ANDROID_APP_ACTIVITY=crc6464956e39085f5526.MainActivity`
5. **Interactive Appium MCP**: only for new UI features without existing tests
6. **Check docs**: use Context7 MCP for MAUI API references

## Skills

No skills are committed (`.cursor/skills/` is whitelisted in `.gitignore` but absent).
- Android E2E: `./scripts/run-mobile-e2e.ps1` (setup: `./scripts/setup-android-e2e.ps1`). Fall back to Appium MCP only for new features.

## Push notifications

Firebase is optional. If `src/OpenKSeF.Mobile/Platforms/Android/google-services.json` exists, the build automatically includes `Xamarin.Firebase.Messaging` and defines `FIREBASE_ENABLED`. Without it, the app builds normally without push support.

- Hub `ReceiveNotification` carries `nativePush` (server has Relay or FCM configured). The local notification is skipped only when a native token is stored and `nativePush == true`; an absent field (API before T-618) shows it.
- All device registration POSTs run in one chain in `DeviceTokenService` with a session token that sign-out cancels. Each sign-out has an id. If removal is not confirmed (delete failed, budget hit, or a POST of that session ended without a server answer: cancel, timeout, network error, 5xx), its device tokens stay in `pending_signout_sessions` (max 5, oldest evicted and logged out) and `AuthService.DeferLogoutAsync(id)` keeps its refresh token. `CompleteSignOutCleanupAsync` (after each sign-out, on start, on resume) waits for the chain and `LateRegistrationWait` (90 s) after an abandoned POST, then per entry refreshes, deletes the rows with that bearer, logs out of Keycloak and drops the entry only after a logout 2xx or `invalid_grant` (offline keeps it for the next run); each step, HTTP included, is cancelled at `ServerRemovalLimit`. It skips while `LogoutUnderway`.

## Debugging playbook

| Problem | How to debug |
|---------|-------------|
| Build fails | Check .NET 10 SDK + MAUI workload: `dotnet workload list` |
| App can't login (ROPC) | Run integration tests: `dotnet test ... --filter "Category=Integration"`. If they pass, the backend is fine -- check emulator server URL (`10.0.2.2:8080` not `localhost`) |
| App can't login (Google/OIDC) | Verify ngrok is running and URL is HTTPS. Run `dev-env-up.ps1` to refresh Keycloak redirect URIs |
| Release APK crashes on start | You probably built with `-p:PublishTrimmed=false`. Remove that flag -- Release needs trimming for JNI bindings |
| E2E login timeout on emulator | Check server URL in prefs: `adb shell "run-as com.openksef.mobile cat shared_prefs/com.openksef.mobile_preferences.xml"`. Must be `http://10.0.2.2:8080` for emulator |
| Appium can't find element | MAUI AutomationId maps to `resource-id` with `MobileBy.Id("com.openksef.mobile:id/...")`, NOT `MobileBy.AccessibilityId` |
| App crashes on emulator | Check `adb logcat -t 200 \| Select-String "AndroidRuntime"` for exceptions |
| JSON deserialization returns null | Do NOT use `JsonSerializerContext` source generation with private nested types -- source gen won't generate code. Use standard `ReadFromJsonAsync<T>()` |
