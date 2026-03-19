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
Przeglądaj i synchronizuj faktury z KSeF na telefonie — powiadomienia i QR.
```

---

## Full description (max 4000 characters)

```
OpenKSeF to darmowa, open-source'owa aplikacja mobilna do przeglądania i synchronizacji faktur z Krajowego Systemu e-Faktur (KSeF).

Aplikacja łączy się z Twoją instancją OpenKSeF i pozwala szybko sprawdzać faktury, kopiować dane do przelewu i generować kody QR — bez potrzeby logowania do portalu KSeF na komputerze.

GŁÓWNE FUNKCJE:

• Przegląd faktur – lista faktur z KSeF z wyszukiwaniem i filtrami. Szczegóły faktury: kwoty, daty, dane kontrahenta.

• Synchronizacja w tle – nowe faktury pojawiają się automatycznie dzięki synchronizacji z KSeF przez backend OpenKSeF.

• Powiadomienia push – natychmiastowe powiadomienia o nowych fakturach (SignalR + opcjonalnie Firebase Cloud Messaging).

• Kody QR i dane przelewu – skopiuj dane przelewu jednym kliknięciem lub wygeneruj kod QR do szybkiej płatności.

• Tryb offline – faktury są zapisywane lokalnie na urządzeniu, więc możesz je przeglądać bez połączenia z internetem.

• Wiele firm – obsługuj kilka firm (NIP-ów) w jednej aplikacji. Szybkie przełączanie między firmami.

• Onboarding – prosty kreator konfiguracji: dodaj firmę, podaj token KSeF i gotowe.

• Bezpieczeństwo – logowanie przez OpenID Connect (Keycloak), szyfrowanie tokenów KSeF w bazie, komunikacja HTTPS.

• Self-hosted – OpenKSeF to oprogramowanie self-hosted. Twoje dane faktur pozostają na Twoim serwerze. Nie korzystamy z żadnych zewnętrznych usług do przechowywania Twoich danych.

• Open source – kod źródłowy jest publicznie dostępny na GitHub (github.com/open-ksef). Licencja Elastic License 2.0.

JAK ZACZĄĆ:

1. Zainstaluj OpenKSeF na swoim serwerze (Docker / docker-compose) — instrukcja na open-ksef.pl
2. Uruchom kreator konfiguracji w portalu webowym
3. Pobierz aplikację mobilną i zeskanuj kod QR z portalu lub wpisz adres serwera ręcznie
4. Gotowe — Twoje faktury z KSeF pojawią się automatycznie

WYMAGANIA:

• Własna instancja OpenKSeF (self-hosted) lub dostęp do instancji demo
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

| Data type                      | Collected | Shared                | Purpose                            | Optional |
|--------------------------------|-----------|-----------------------|------------------------------------|----------|
| Email address                  | Yes       | No                    | Account creation, notifications    | No       |
| Name (business/vendor name)    | Yes       | No                    | Invoice display                    | No       |
| Financial info (invoices, NIP) | Yes       | No                    | Core app functionality             | No       |
| Device identifiers (FCM token) | Yes       | Yes (to push relay)   | Push notifications                 | Yes      |
| App interactions               | No        | No                    | —                                  | —        |
| Crash logs                     | No        | No                    | —                                  | —        |

Additional declarations:
- Data is encrypted in transit (HTTPS)
- Users can request account and data deletion
- Data is NOT shared with third parties for advertising
- Data is NOT used for personalization or profiling

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
