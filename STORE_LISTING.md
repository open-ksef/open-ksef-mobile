# Google Play Store Listing

Source of the pl-PL Google Play listing. `.github/workflows/play-publish.yml` reads the title, short and full blocks from this file at the dispatched commit; `scripts/play_publish_test.py` checks the limits. Keep the headings and fences as they are.

---

## App identity

| Field            | Value                |
|------------------|----------------------|
| App name         | See Title            |
| Default language | Polish (pl-PL)       |
| App or game      | App                  |
| Free or paid     | Free                 |
| Category         | Business             |
| Application ID   | com.openksef.mobile  |
| Min Android      | API 24 (Android 7.0) |

---

## Title (max 30 characters)

```
OpenKSeF: faktury z KSeF
```

---

## Short description (max 80 characters)

```
Faktury z KSeF na telefonie. Łączy się z Twoją instancją OpenKSeF.
```

---

## Full description (max 4000 characters)

```
OpenKSeF to aplikacja dla właścicieli małych firm i księgowych, którzy mają własną instancję OpenKSeF albo dostęp do instancji udostępnionej przez jej właściciela. Aplikacja nie działa bez serwera: łączy się z instancją, której adres podajesz. Nie prowadzimy wersji hostowanej dla firm.

OpenKSeF to niezależny projekt, niepowiązany z Ministerstwem Finansów. Z KSeF łączy się przez jego publiczne API.

CO MOŻESZ ZROBIĆ

• Faktury otrzymane: lista i szczegóły z kwotami, datami i danymi kontrahenta.
• Wystawianie: faktura dla firmy lub osoby prywatnej, dane nabywcy pobrane po NIP, wysyłka do KSeF, status, PDF, XML i UPO.
• Faktury zagraniczne: podgląd w aplikacji, wystawianie w portalu.
• Płatność: kod QR i dane przelewu, gdy faktura je zawiera.
• Powiadomienia o nowych fakturach, które można wyłączyć na ekranie Konto. Serwer sprawdza KSeF okresowo, więc powiadomienie nie przychodzi od razu.
• Wiele firm (NIP-ów) na jednym koncie.
• Offline: ostatnie faktury otrzymane są dostępne bez internetu. Wystawianie wymaga połączenia.

JAK ZACZĄĆ

1. Uruchom OpenKSeF na swoim serwerze (Docker). Instrukcja: open-ksef.pl
2. Dodaj firmę i token KSeF w portalu albo w aplikacji.
3. W aplikacji podaj adres serwera lub zeskanuj kod QR z portalu i zaloguj się.

DANE

Dane firm i faktur są na instancji, z którą łączy się aplikacja. Powiadomienia domyślnie przechodzą przez usługę relay OpenKSeF i Firebase Cloud Messaging. Wystawione faktury trafiają do KSeF. Sprawdzenie NIP-u korzysta z białej listy Ministerstwa Finansów. Połączenie szyfruje HTTPS.

Kod źródłowy jest publicznie dostępny: github.com/open-ksef. Licencja: Elastic License 2.0.

Wymagany Android 7.0 lub nowszy. Błędy: github.com/open-ksef/open-ksef-mobile/issues
```

---

## Contact details

| Field         | Value                               |
|---------------|-------------------------------------|
| Contact email | krystian@mikrut.dev                 |
| Website       | https://open-ksef.pl                |

---

## Privacy policy URL

```
https://open-ksef.pl/docs/polityka-prywatnosci
```

---

## Content rating

IARC questionnaire answers for a business invoice app:

- Violence: No
- Sexual content: No
- Gambling: No
- Controlled substances: No
- User-generated content: No
- Shared personal information: No
- Location data: No
- Purchases: No

Expected rating: **Everyone / PEGI 3**

---

## Target audience

- Target age group: **18+** (business users, tax invoices)
- NOT a children's app (no COPPA/Families compliance needed)

---

## Data safety

Answers cover the hosted instance and self-hosted instances together, because one label covers one binary. Evidence: T-303. Internal-only builds are exempt; submit before closed, open or production.

| Data type (Play)                                                     | Collected | Shared | Purpose                                 | Optional |
|----------------------------------------------------------------------|-----------|--------|-----------------------------------------|----------|
| Personal: Name (account, seller, private buyer)                      | Yes       | No     | App functionality, account management   | Yes      |
| Personal: Email address                                              | Yes       | No     | App functionality, account management   | No       |
| Personal: User IDs (account ID)                                      | Yes       | No     | App functionality, account management   | No       |
| Personal: Address (seller, buyer, may be a home address)             | Yes       | No     | App functionality                       | Yes      |
| Personal: Other info (NIP, KSeF token)                               | Yes       | No     | App functionality                       | No       |
| Financial: User payment info (seller bank account)                   | Yes       | No     | App functionality                       | Yes      |
| Financial: Other financial info (issued invoices, amounts, lines)    | Yes       | No     | App functionality                       | Yes      |
| Financial: Purchase history (received invoices)                      | Yes       | No     | App functionality                       | No       |
| Device or other IDs (FCM/APNs token, device ID, Firebase install ID) | Yes       | No     | App functionality (push notifications)  | No       |
| App interactions                                                     | No        | No     | -                                       | -        |
| Crash logs, diagnostics                                              | No        | No     | -                                       | -        |
| Photos, files, location, contacts, messages                          | No        | No     | -                                       | -        |

Additional declarations:
- All data is encrypted in transit (HTTPS enforced in Release builds)
- Account creation: in-app (username and password, Google through Keycloak)
- Account deletion URL: https://open-ksef.pl/docs/polityka-prywatnosci/#usuwanie-konta
- Users can request account and data deletion; invoices accepted in KSeF on production may be retained
- Not "shared" under Google's exclusions: push relay (run by the publisher), FCM and APNs (service providers), KSeF and the MF VAT white list (legal purpose, user action)
- Data is NOT used for advertising, personalization or profiling
- Crash logs change to Yes when Sentry ships (T-108); update this table and the form first (T-107)

### App Store: App Privacy

Not submitted yet; iOS is not released. Purpose for all: App Functionality. Linked to user: Yes. Tracking: No.

| Category       | Types collected                                  |
|----------------|--------------------------------------------------|
| Contact Info   | Name, Email Address, Physical Address            |
| Financial Info | Payment Info (bank account), Other Financial Info |
| User Content   | Other User Content (invoice line descriptions)   |
| Identifiers    | User ID, Device ID                               |

Not collected: Diagnostics, Location, Contacts, Browsing History, Search History, Purchases, Usage Data, Sensitive Info, Health and Fitness.

---

## Graphics

`play-publish.yml` with `listing: true` replaces all three image types from these committed files, and fails before any Play call if one is missing. Fake data only: NIP `1111111111` or `2222222222`. Never production data.

| Asset             | Spec                                    | File                              | Source                                                                 |
|-------------------|-----------------------------------------|-----------------------------------|------------------------------------------------------------------------|
| App icon          | 512x512, 32-bit PNG, opaque, max 1024 KB | `store/pl-PL/icon.png`            | `production/design/brand/app-icon/appicon-1024.png` exported RGBA      |
| Feature graphic   | 1024x500, 24-bit PNG                    | `store/pl-PL/featureGraphic.png`  | `production/design/brand/source/og-image.html`, white mark on `#4f46e5`, no price or Beta claim |
| Phone screenshots | 1080x1920, 24-bit PNG, no alpha         | `store/pl-PL/phoneScreenshots/`   | Release build on a 1080x1920 AVD; file name order is listing order      |

### Screenshots

1. `InvoiceListPage`: received invoices
2. `InvoiceDetailsPage`: amounts, `Zapłać`
3. `QrCodePage`: payment QR, `Kopiuj dane przelewu`
4. `InvoiceDraftPage`: company buyer, one line, VAT
5. `IssuedInvoiceListPage`: statuses
6. `LoginPage`: server field with `https://twoj-serwer.example`

No taglines, device frames or calls to action.

---

## Release tracks

1. **Internal**: a `v*` tag uploads the bundle (`release.yml`)
2. **Closed**: `play-publish.yml`, `track: closed`, an uploaded versionCode
3. **Production**: `play-publish.yml`, `track: production`, the same versionCode
