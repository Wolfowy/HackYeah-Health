# DocPrep Backend

Modularny backend ASP.NET Core 8 realizujący procesy P-01–P-08 z dokumentacji projektu.

## Stack i moduły

- ASP.NET Core Minimal API i OpenAPI,
- EF Core z PostgreSQL oraz jawnymi migracjami,
- Redis dla krótkotrwałych sesji pacjenta,
- QuestPDF dla raportów z polskimi znakami,
- Docker Compose dla API, PostgreSQL i Redis.

Solution zawiera `DocPrep.Domain`, `DocPrep.Application`, `DocPrep.Infrastructure`, `DocPrep.Api` i testy. Model rozdziela proces wizyty, hashowane linki/kody, wersję roboczą, obserwacje, niezmienne wersje raportu, zgody i rundy uzupełniające.

## Uruchomienie

```bash
docker compose up --build
```

- API: `http://localhost:8080`
- Swagger w Development: `http://localhost:8080/swagger`
- liveness: `http://localhost:8080/health/live`
- readiness: `http://localhost:8080/health/ready`

Demo ma trzy role:

| Rola | `X-Api-Key` |
|---|---|
| Administracja | `demo-admin-key` |
| Lekarz | `demo-clinician-key` |
| System placówki | `demo-system-key` |

Wartości są przeznaczone wyłącznie do lokalnego demo. Klucze integracyjne oraz `Security__PatientHmacKey` i `Security__EncryptionKey` muszą być nadpisane sekretami środowiska produkcyjnego.

## Dostęp pacjenta

Pacjent nie posiada konta i nie przekazuje PESEL-u jako poświadczenia. Placówka tworzy wizytę i otrzymuje jednorazowo link token oraz kod. Pacjent wymienia jeden z nich na krótko żyjący opaque bearer token ograniczony do jednego wywiadu:

```text
POST /api/v1/patient-access/link/exchange
POST /api/v1/patient-access/code/exchange
```

Pozostałe endpointy pacjenta używają `Authorization: Bearer <session-token>`. Nie istnieje endpoint przeglądania historii pacjenta.

## Izolacja danych

- Administracja pobiera wyłącznie status, termin, dostarczenie zaproszenia i stan rundy.
- Lekarz pobiera immutable `ReportVersion`, nigdy draft.
- Podczas edycji poprzednia udostępniona wersja pozostaje dostępna.
- Cofnięcie zgody natychmiast blokuje kolejne pobrania.
- JSON i PDF są zapisane z tego samego snapshotu i mają wspólny `VersionId`.
- PESEL służy jedynie do tworzenia HMAC correlation key; wartość źródłowa jest szyfrowana.

## Komendy developerskie

```bash
dotnet restore DocPrep.sln
dotnet build DocPrep.sln --no-restore -c Release
dotnet test DocPrep.sln --no-build -c Release
dotnet ef database update --project src/DocPrep.Infrastructure --startup-project src/DocPrep.Api
docker compose config
```
