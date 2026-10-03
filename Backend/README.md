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

## Agent ElevenLabs

Dodano osobne encje `AgentInterview` → `AgentSession` → `ProviderConversationId` oraz `InterviewInvitation`. Powiązanie z istniejącą wizytą (`Appointment`) zachowuje dotychczasową obsługę zgód i zatwierdzonych wersji.

Konfiguracja wyłącznie na backendzie, przez środowisko / secret manager:

```dotenv
ElevenLabs__ApiKey=...
ElevenLabs__AgentId=...
ElevenLabs__WebhookSecret=...
ElevenLabs__Environment=production
```

Dla `docker compose` odpowiadają im `ELEVENLABS_API_KEY`, `ELEVENLABS_AGENT_ID`, `ELEVENLABS_WEBHOOK_SECRET`. Brak konfiguracji powoduje 503 przy rozpoczęciu sesji. Lokalnie API uruchom na porcie proxy frontendu:

```sh
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:8080 dotnet run --project src/HealthPrep.Api --no-launch-profile
```

Nowa baza otrzymuje tabele przez `EnsureCreated`. Dla istniejącej bazy wykonaj **jednorazowo** `database/002_elevenlabs.sql` przed uruchomieniem nowego API. `EnsureCreated` nie aktualizuje istniejącego schematu. Skrypt dodaje tylko trzy tabele integracji i ich indeksy/klucze; nie usuwa danych wizyt.

### Zaproszenia

Po utworzeniu wizyty przez dotychczasowy endpoint:

```sh
curl -X POST http://localhost:8080/api/v1/integration/appointments/ID_WIZYTY/invitation \
  -H 'X-Api-Key: demo-clinic-key'
```

Odpowiedź zawiera `invitationUrl: /i/{token}`, `invitationId`, `expiresAt`. Dodaj domenę frontendu do ścieżki. Ponowne wystawienie unieważnia wcześniejsze zaproszenia, a `DELETE` pod tym samym adresem je odwołuje. Nie wysyłamy SMS ani e-maili.

Token ma 256 bitów losowości, w bazie jest tylko SHA-256. Wygasa najpóźniej w chwili wizyty lub po siedmiu dniach. Limit to trzy sesje, rezerwowane atomowo w transakcji; nieudane wydanie credentiala nie zużywa limitu. Walidacja publiczna zwraca tylko nazwę wywiadu, termin i status.

`POST /api/public/interviews/{token}/authorize` wymienia zaproszenie na godzinny, chroniony **opaque bearer token ASP.NET Data Protection**, ograniczony do konkretnego wywiadu/zaproszenia. To wariant sesji aplikacyjnej z dokumentu integracji; token nie jest JWT. Serwer przy każdym użyciu ponownie sprawdza ważność i unieważnienie zaproszenia. W produkcji przechowuj i zabezpieczaj klucze Data Protection poza cyklem życia procesu, wspólne dla replik (`InterviewAccess__KeyDirectory`; Compose używa wolumenu `access_keys`). Tokeny aplikacyjne, zaproszenia i credentiale dostawcy nie są logowane przez kod integracji; analogiczne wyłączenie stosuj w reverse proxy i analityce.

### API

| Operacja | Endpoint |
| --- | --- |
| Metadane / wymiana zaproszenia | `GET /api/public/interviews/{token}`, `POST .../{token}/authorize` |
| Odczyt / sesja / wynik gościa | `GET /api/interview`, `POST /api/interview/sessions`, `GET /api/interview/result` |
| Wywiad dla wizyty pacjenta | `GET /api/visits/{visitId}/interview` |
| Sesja / wynik pacjenta | `POST /api/interviews/{id}/sessions`, `GET /api/interviews/{id}/result` |
| Potwierdzenie rozmowy dostawcy | `PUT /api/interview-sessions/{sessionId}/provider-conversation` |
| Zakończenie transportu | `POST /api/interview-sessions/{sessionId}/end` |
| Podpisany wynik | `POST /api/webhooks/elevenlabs` |

Sesja przyjmuje `{ "mode": "voice" }` lub `text`. Głos otrzymuje WebRTC token, tekst signed URL. API key dostawcy jest wysyłany tylko przez serwer. Jeśli credential zawiera `conversation_id`, binding musi być identyczny; w przeciwnym razie backend potwierdza agenta i techniczny `user_id` przez API ElevenLabs. Identyfikator techniczny ma postać `session_{losowyGuid}`, bez e-maila czy PESEL-u. `dynamicVariables` zawierają `language` i `visit_type`; zadeklaruj te zmienne w konfiguracji agenta.

Nowe endpointy pacjenta w Development dopuszczają `X-Patient-Id`. W produkcji wymagają uwierzytelnionego principal z `sub` / `NameIdentifier`; konfiguracja OIDC i rzeczywistego logowania nie jest jeszcze zaimplementowana. Panel konta frontendu pozostaje mockiem. Wariant publicznego zaproszenia działa niezależnie od logowania.

Zakończenie połączenia oznacza `processing`; `{ "continuesInterview": true }` przy zmianie głosu na tekst zapisuje wcześniejszą sesję bez kończenia całego wywiadu. Dane poprzedniej sesji pozostają w bazie. Wynik wywiadu tworzy ostatnia zakończona sesja; frontend przekazuje jej historię jako kontekst. Pełne scalanie Data Collection pomiędzy sesjami wymaga osobnej logiki domenowej.

### Konfiguracja agenta i webhooka

W ElevenLabs ustaw prywatnego agenta obsługującego polski wywiad, WebRTC i tekst oraz pola Data Collection. Utwórz webhook `post_call_transcription` wskazujący na publiczny HTTPS `/api/webhooks/elevenlabs`; jego sekret wpisz do konfiguracji backendu. Włącz ponowienia webhooków. Dla lokalnego API potrzebny jest tunel HTTPS lub środowisko testowe dostępne dla ElevenLabs.

Handler sprawdza HMAC-SHA256 dokładnych bajtów body, nagłówek `ElevenLabs-Signature` i timestamp (30 minut, także kontrola przyszłych dat). Zapisuje transcript, analysis, metadata, Data Collection i `transcript_summary`. Powtórzony webhook nie nadpisuje wyniku; opóźniona wcześniejsza sesja nie zastępuje nowszej. Nie zatwierdza raportu ani zgody i nie udostępnia automatycznie placówce. Endpointy pacjenta/gościa zwracają wynik, a dotychczasowy raport placówki nadal wymaga istniejącego procesu zatwierdzenia i zgody; mapowanie wyniku AI do tego procesu pozostaje do podłączenia.

Źródła: [SDK JavaScript](https://elevenlabs.io/docs/eleven-agents/libraries/java-script), [post-call webhooks](https://elevenlabs.io/docs/eleven-agents/workflows/post-call-webhooks), [implementacja podpisu w oficjalnym SDK](https://github.com/elevenlabs/elevenlabs-python/blob/main/src/elevenlabs/webhooks_custom.py).

### Testy integracji

`dotnet test` uruchamia testy domeny, modelu i podpisów. Test endpointów jest domyślnie pomijany bez izolowanej bazy:

```sh
HEALTHPREP_TEST_POSTGRES='Host=localhost;Port=5432;Database=healthprep_test;Username=test;Password=test' dotnet test HealthPrep.sln
```

Użyj osobnej pustej bazy testowej. Test uruchamia lokalne API i atrapę dostawcy, sprawdza credentiale, izolację uprawnień, konkurencyjny limit, odwołanie linka, podpis i idempotencję webhooka. Nie łączy się z ElevenLabs.
