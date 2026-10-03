# HealthPrep Backend

Modularny monolit w ASP.NET Core 8, zaprojektowany tak, aby reguły biznesowe oraz integracje z AI, bazą i generatorem PDF można było wymieniać niezależnie.

## Architektura

- `HealthPrep.Domain` — agregat wizyty, zgody, statusy, deadline edycji i wersjonowanie.
- `HealthPrep.Application` — przypadki użycia, kontrakty repozytorium, generatora pytań i PDF.
- `HealthPrep.Infrastructure` — EF Core + PostgreSQL, Redis, adaptacyjny silnik pytań i PDF.
- `HealthPrep.Api` — wersjonowane Minimal API, OpenAPI, izolacja tenantów i obsługa błędów.
- `HealthPrep.UnitTests` — testy niezmienników domenowych.

PostgreSQL jest źródłem prawdy. Redis jest zarejestrowany jako rozproszony cache i może służyć do statusów, sesji oraz rate limiting bez zmiany domeny. `IInterviewQuestionProvider` jest celowo abstrakcją: obecny deterministyczny provider można zastąpić adapterem LLM bez ingerencji w przypadki użycia.

## Uruchomienie

W katalogu `Backend`:

```bash
docker compose up --build
```

API: `http://localhost:8080`, Swagger: `http://localhost:8080/swagger`, health check: `http://localhost:8080/health`.

Demo integracji używa nagłówka `X-Api-Key: demo-clinic-key`. Endpointy pacjenta używają tymczasowego nagłówka `X-Patient-Id`; przed produkcją należy podmienić go na identyfikator `sub` z firmowego OIDC/OAuth2. Klucze produkcyjne należy przekazywać przez secrets manager, nie przez `appsettings.json`.

## Najważniejsze przepływy

1. Placówka tworzy wizytę: `POST /api/v1/integration/appointments`.
2. Pacjent odpowiada: `POST /api/v1/patient/appointments/{id}/answers` (tekst lub transkrypcja głosu).
3. Pacjent edytuje dane: `PUT /api/v1/patient/appointments/{id}/summary`.
4. Pacjent zatwierdza wersję i nadaje zgodę: `POST .../approve`, następnie `PUT .../consent`.
5. Placówka pobiera zatwierdzony JSON/PDF dopiero po zgodzie.
6. Lekarz może przesłać pytanie uzupełniające; wcześniejsze wersje pozostają zachowane.

Przykład utworzenia wizyty:

```bash
curl -X POST http://localhost:8080/api/v1/integration/appointments \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: demo-clinic-key" \
  -d '{"externalAppointmentId":"visit-001","externalPatientId":"patient-001","scheduledAt":"2026-12-10T10:00:00Z","consultationReason":"Recurring headache"}'
```

## Decyzje pod dalszy rozwój

- Każda placówka ma `TenantId`; zapytania integracyjne nigdy nie zwracają danych obcego tenantu.
- Raport placówki wymaga jednocześnie zatwierdzonego statusu i aktualnej zgody pacjenta.
- Snapshoty JSON są niezmienne i wersjonowane; dane robocze pozostają edytowalne do deadline'u.
- Obserwacje trendów wskazują konkretne wizyty i nie interpretują braku wzmianki jako ustąpienia objawu.
- Audio powinno trafić do osobnego object storage, a backend powinien przechowywać tylko metadane i zweryfikowaną transkrypcję.
- `Database:Initialize=true` wykorzystuje `EnsureCreated` dla szybkiego demo. Przed produkcją należy ustawić `false` i wdrażać jawne migracje EF w pipeline.

## Weryfikacja lokalna

```bash
dotnet restore HealthPrep.sln
dotnet build HealthPrep.sln --no-restore
dotnet test HealthPrep.sln --no-build
docker compose config
```
