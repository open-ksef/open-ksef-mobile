# Google Play Store Listing

Reference document for the Google Play Console submission. Copy-paste these values when creating the app listing.

---

## App identity

| Field            | Value                |
|------------------|----------------------|
| App name         | OpenKSeF             |
| Default language | Polish (pl-PL)       |
| App or game      | App                  |
| Free or paid     | Free                 |
| Category         | Business             |
| Application ID   | com.openksef.mobile  |
| Min Android      | API 24 (Android 7.0) |

---

## Short description (max 80 characters)

```
Wystawiaj i przeglądaj faktury w KSeF na telefonie — powiadomienia i kody QR.
```

---

## Full description (max 4000 characters)

```
OpenKSeF to darmowa, open-source'owa aplikacja mobilna do wystawiania, przeglądania i synchronizacji faktur w Krajowym Systemie e-Faktur (KSeF).

Aplikacja łączy się z Twoją instancją OpenKSeF i pozwala szybko wystawiać i sprawdzać faktury, kopiować dane do przelewu i generować kody QR — bez potrzeby logowania do portalu KSeF na komputerze.

GŁÓWNE FUNKCJE:

• Przegląd faktur – lista faktur z KSeF z wyszukiwaniem i filtrami. Szczegóły faktury: kwoty, daty, dane kontrahenta.

• Wystawianie faktur – utwórz fakturę dla firmy lub osoby prywatnej, pobierz dane nabywcy po NIP i wyślij fakturę do KSeF. Sprawdzaj jej status, udostępniaj PDF, XML i UPO.

• Synchronizacja w tle – nowe faktury pojawiają się automatycznie dzięki synchronizacji z KSeF przez backend OpenKSeF.

• Powiadomienia push – powiadomienia o nowych fakturach.

• Kody QR i dane przelewu – skopiuj dane przelewu jednym kliknięciem lub wygeneruj kod QR do szybkiej płatności.

• Tryb offline – faktury są zapisywane lokalnie na urządzeniu, więc możesz je przeglądać bez połączenia z internetem.

• Wiele firm – obsługuj kilka firm (NIP-ów) w jednej aplikacji. Szybkie przełączanie między firmami.

• Onboarding – prosty kreator konfiguracji: dodaj firmę, podaj token KSeF i gotowe.

• Bezpieczeństwo – logowanie przez OpenID Connect (Keycloak), szyfrowanie tokenów KSeF w bazie, komunikacja HTTPS.

• Self-hosted – OpenKSeF możesz uruchomić na własnym serwerze. Dane firm i faktur są przechowywane na instancji, z którą łączy się aplikacja. Powiadomienia push przechodzą przez usługę relay OpenKSeF oraz Firebase Cloud Messaging lub Apple Push Notification service, wystawione faktury trafiają do KSeF, a sprawdzenie NIP-u korzysta z białej listy Ministerstwa Finansów.

• Open source – kod źródłowy jest publicznie dostępny na GitHub (github.com/open-ksef). Licencja Elastic License 2.0.

JAK ZACZĄĆ:

1. Zainstaluj OpenKSeF na swoim serwerze (Docker / docker-compose) — instrukcja na open-ksef.pl
2. Uruchom kreator konfiguracji w portalu webowym
3. Pobierz aplikację mobilną i zeskanuj kod QR z portalu lub wpisz adres serwera ręcznie
4. Gotowe — Twoje faktury z KSeF pojawią się automatycznie

WYMAGANIA:

• Dostęp do instancji OpenKSeF: własnej (self-hosted) lub udostępnionej przez jej właściciela
• Android 7.0 (API 24) lub nowszy

WSPARCIE I KONTAKT:

• Dokumentacja: open-ksef.pl
• GitHub: github.com/open-ksef
• Zgłaszanie błędów: github.com/open-ksef/open-ksef-mobile/issues
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

## Graphics checklist

| Asset                    | Spec              | Source                                         |
|--------------------------|-------------------|------------------------------------------------|
| App icon (Play Store)    | 512x512 PNG       | Export from `Resources/AppIcon/appicon.svg` + `appiconfg.svg` |
| Feature graphic          | 1024x500 PNG      | Create with OpenKSeF branding (#1E88E5 blue)   |
| Phone screenshots (min 2)| 16:9 or 9:16      | Capture from emulator or device                |

### Recommended screenshots

1. Invoice list (Faktury tab)
2. Invoice details with amounts
3. QR code / transfer data screen
4. Tenant list (Firmy tab)
5. Login / onboarding screen

---

## Release track recommendation

1. **Internal testing** — upload first AAB, add team emails as testers
2. **Closed testing** — invite beta users
3. **Production** — after testing passes, promote to production
