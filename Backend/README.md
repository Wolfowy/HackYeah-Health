# DocPrep Backend

Modularny backend ASP.NET Core 8 realizujący procesy P-01–P-08 z dokumentacji projektu.

## Stack i moduły

- ASP.NET Core Minimal API i OpenAPI,
- EF Core z PostgreSQL oraz jawnymi migracjami,
- Redis dla krótkotrwałych sesji pacjenta,
- JWT access token oraz rotowany refresh token dla użytkowników panelu,
- QuestPDF dla raportów z polskimi znakami,
- Docker Compose dla API, PostgreSQL i Redis.

Solution zawiera `DocPrep.Domain`, `DocPrep.Application`, `DocPrep.Infrastructure`, `DocPrep.Api` i testy. Model rozdziela proces wizyty, hashowane linki/kody, wersję roboczą, obserwacje, niezmienne wersje raportu, zgody i rundy uzupełniające.

## Uruchomienie

### Istniejący PostgreSQL na porcie 2142

Lokalny wariant uruchamia API i Redis, korzystając z istniejącego PostgreSQL na komputerze. Z katalogu `Backend`:

```sh
# Tylko jeśli nie masz jeszcze .env:
cp .env.local.example .env
# Ustaw hasło istniejącego PostgreSQL oraz wartości ElevenLabs w .env.
docker compose -f compose.local.yaml --env-file .env up --build -d
```

Połączenie używa `host.docker.internal:2142`, użytkownika `postgres` i bazy `docprep`. Hasło jest wyłącznie w ignorowanym pliku `Backend/.env`. Migracje EF tworzą bazę i schemat przy pierwszym starcie. Redis nie publikuje portu na komputerze, a API jest dostępne na `http://127.0.0.1:8080`.

W `Backend/.env` wklej pełny klucz do `ELEVENLABS_API_KEY`, a identyfikator `agent_…` do `ELEVENLABS_AGENT_ID`. Sekret podpisanego webhooka wpisz do `ELEVENLABS_WEBHOOK_SECRET`. Po zmianie tych wartości odtwórz API:

```sh
docker compose -f compose.local.yaml --env-file .env up -d --force-recreate api
```

Uruchom frontend w osobnym terminalu przez `cd Frontend && npm run dev` z katalogu repozytorium. Adres `http://127.0.0.1:5173` przekazuje `/api` do tego backendu. Polecenie zatrzymania lokalnego wariantu to `docker compose -f compose.local.yaml --env-file .env down`; nie zatrzymuje istniejącego PostgreSQL.

### Pełny zestaw usług z własnym PostgreSQL

```bash
docker compose up --build
```

- API: `http://localhost:8080`
- Swagger w Development: `http://localhost:8080/swagger`
- liveness: `http://localhost:8080/health/live`
- readiness: `http://localhost:8080/health/ready`

## Uwierzytelnianie i autoryzacja

Backend rozdziela cztery rodzaje poświadczeń:

- pacjent: opaque bearer token ograniczony do jednej wizyty i przechowywany w Redisie,
- użytkownik panelu: krótkotrwały JWT oraz jednorazowo rotowany refresh token,
- system placówki: `X-Api-Key`, wyłącznie do komunikacji serwer–serwer.
- opcjonalne konto pacjenta: osobny JWT i rotowany refresh token; konto można założyć dopiero z dostępu do zweryfikowanej wizyty.

Konta demonstracyjne panelu, tworzone tylko w środowisku `Development`:

| Rola | Login | Hasło |
|---|---|---|
| Administracja | `admin@docprep.local` | `DocPrepDemo!2026` |
| Lekarz | `doctor@docprep.local` | `DocPrepDemo!2026` |

```http
POST /api/v1/auth/login
Content-Type: application/json

{ "email": "admin@docprep.local", "password": "DocPrepDemo!2026" }
```

Frontend używa `accessToken` jako `Authorization: Bearer ...`. Odnowienie i wylogowanie realizują odpowiednio `/api/v1/auth/refresh` i `/api/v1/auth/logout`; `/api/v1/auth/me` zwraca bieżącego użytkownika wraz z rolą i placówką. Access token żyje domyślnie 15 minut, refresh token 7 dni i jest rotowany przy każdym użyciu.

Demo integracji system–system ma trzy role:

| Rola | `X-Api-Key` |
|---|---|
| Administracja | `demo-admin-key` |
| Lekarz | `demo-clinician-key` |
| System placówki | `demo-system-key` |

Wartości są przeznaczone wyłącznie do lokalnego demo. Klucze integracyjne, connection stringi, `Security__PatientHmacKey`, `Security__EncryptionKey` i `Authentication__Jwt__SigningKey` muszą być dostarczone jako sekrety środowiska poza `Development`. Klucz JWT musi być losowym kluczem co najmniej 256-bitowym zakodowanym Base64.

## Dostęp pacjenta

Podstawowy proces nie wymaga konta i nie używa PESEL-u jako poświadczenia. Placówka tworzy wizytę i otrzymuje jednorazowo link token oraz kod. Pacjent wymienia jeden z nich na krótko żyjący opaque bearer token ograniczony do jednej wizyty:

```text
POST /api/v1/patient-access/link/exchange
POST /api/v1/patient-access/code/exchange
```

Pozostałe endpointy pacjenta używają `Authorization: Bearer <session-token>`. Anonimowy JWT z zaproszenia ElevenLabs zawiera również zakres `report:review`, dlatego po zakończeniu rozmowy może korzystać z istniejących operacji `/api/v1/interview` do korekty, zatwierdzenia i zgody. Nie pozwala już jednak rozpocząć kolejnej rozmowy.

Opcjonalne konto pacjenta udostępnia `/api/v1/patient-account/auth/register|login|refresh`, profil `/me`, listę `/visits` oraz wydanie ograniczonej sesji dla własnej wizyty przez `/visits/{visitId}/session`. Rejestracja wymaga aktywnej, wcześniej zweryfikowanej sesji wizyty.

Frontend powinien po wymianie linku natychmiast usunąć token linku z paska adresu (`history.replaceState`) i przechowywać sesję pacjenta w pamięci, nie w `localStorage`.

## Izolacja danych

- Administracja pobiera wyłącznie status, termin, dostarczenie zaproszenia i stan rundy.
- Lekarz pobiera immutable `ReportVersion`, nigdy draft.
- Lekarz odczytuje raport wyłącznie dla wizyty przypisanej do jego `ClinicianId`.
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

Swagger opisuje oba warianty dostępu do endpointów placówki jako alternatywę: JWT użytkownika panelu albo API key systemu placówki. Dla generowanego klienta TypeScript należy używać JWT; klucza `X-Api-Key` nie wolno osadzać w aplikacji przeglądarkowej.

## ElevenLabs Agent

Instrukcja krok po kroku: [ElevenLabs — konfiguracja i stan integracji](../docs/elevenlabs-uruchomienie.md), w tym pobranie klucza API, konfiguracja webhooka i utworzenie linku do rozmowy.

Nowa wizyta automatycznie otrzymuje biznesowy wywiad oraz osobne zaproszenie do agenta. Odpowiedź `POST /api/v1/integration/visits` zawiera `interviewId` i jednorazowo jawny `interviewInvitationToken`. W bazie przechowywany jest wyłącznie SHA-256 tokenu.

Konfiguracja serwerowa:

```text
ElevenLabs__ApiKey=<secret>
ElevenLabs__AgentId=agent_...
ElevenLabs__Environment=production
ElevenLabs__WebhookSecret=<secret>
```

Żadna z tych wartości nie jest zwracana frontendowi. Voice otrzymuje krótkotrwały `conversationToken` WebRTC, a text chat `signedUrl` WebSocket.

Publiczny flow rekomendowany dla frontendu:

```text
GET  /api/public/interviews/{invitationToken}
POST /api/public/interviews/{invitationToken}/authorize
     -> anonymous JWT ograniczony do jednego interviewId
POST /api/interviews/{interviewId}/sessions
PUT  /api/interview-sessions/{sessionId}/provider-conversation
POST /api/interview-sessions/{sessionId}/end
GET  /api/interviews/{interviewId}
GET  /api/interviews/{interviewId}/result
```

`result` zwraca kompatybilne `structuredDataJson` oraz typowane `structuredData`, `schemaVersion`, `extractionStatus`, `importStatus` i `issues`. Agent powinien posiadać Data Collection typu String o nazwie `interview_json`, zawierające JSON schematu DocPrep. Niepoprawny JSON otrzymuje status `failed`; częściowe dane nie są przedstawiane jako kompletny raport.

Po finalnym webhooku backend scala wyniki wszystkich sesji, mapuje objawy, leki, alergie, choroby, pytania i uwagi do `InterviewDraft`, ale nie zatwierdza raportu i nie udziela zgody automatycznie. Pacjent korzysta następnie z istniejącego procesu `/api/v1/interview/draft`, `/complete`, `/approve` i `/consent`. Raport JSON/PDF zawiera powód przyjmowania leku, dodatkowe uwagi i chronologię objawów.

Po `/authorize` frontend powinien usunąć surowy token zaproszenia z URL. Dla kompatybilności dostępne jest również bezpośrednie `POST /api/public/interviews/{invitationToken}/sessions`, ale wariant z anonimowym JWT jest bezpieczniejszy.

Pacjent posiadający istniejącą sesję DocPrep może użyć:

```text
GET  /api/visits/{visitId}/interview
POST /api/interviews/{interviewId}/sessions
```

Webhook należy skonfigurować w ElevenLabs jako:

```text
POST https://<api-host>/api/webhooks/elevenlabs
```

Backend weryfikuje `ElevenLabs-Signature` na surowym body, odrzuca podpisy starsze niż 30 minut, sprawdza `agent_id`, koreluje `conversation_id` z sesją i idempotentnie zapisuje transcript, analysis, metadata, data collection oraz finalne podsumowanie. Credential ElevenLabs, signed URL i surowy token zaproszenia nigdy nie są zapisywane.

### Frontend i zakończenie sesji

Frontend `Przed wizytą` znajduje się w `Frontend`; instrukcja uruchomienia jest w [README frontendu](../Frontend/README.md). Link publiczny ma postać `/i/{interviewInvitationToken}`. `/authorize` zwraca `accessToken`, `expiresIn` i `interviewId`. Frontend wywołuje endpointy wywiadu z bearer JWT i mapuje wynik `finalReport` oraz `structuredDataJson` na podsumowanie UI.

`POST /api/interview-sessions/{sessionId}/end` przyjmuje `{ "continuesInterview": false }`. Oznacza transport i wywiad jako `processing`; wynik nadal pochodzi wyłącznie z podpisanego webhooka. Przy zmianie głosu na tekst frontend najpierw wysyła `continuesInterview: true`, a następnie zamyka SDK. Taka sesja ma status `Abandoned`, a jej webhook zapisuje transkrypcję bez kończenia wywiadu i zaproszenia. Opóźniony webhook starszej sesji nie zastępuje wyniku nowszej. Nie wymaga to nowej migracji: statusy mają już reprezentację tekstową. Aktualne migracje EF DocPrep zastępują wcześniejszy skrypt SQL HealthPrep; nie należy używać starego skryptu dla schematu `docprep`.

Sesja pacjenta DocPrep może zostać przekazana adapterowi frontendu przez `agentApi.setPatientSession(token)`; nagłówek `X-Patient-Id` nie jest obsługiwany.

### Wizyty, niezawodność i integracje

`POST /api/v1/integration/visits` przyjmuje snapshot lekarza i miejsca (`doctorName`, `doctorSpecialty`, `facilityName`, `facilityAddress`, `room`, `visitType`, `locationInstructions`, `timeZone`). `GET /api/v1/integration/visits` obsługuje `from`, `to`, `status`, `page`, `pageSize`, a `PUT /api/v1/integration/visits/{id}` zmienia termin i przypisanie. Powtórzenie `externalVisitId` kończy się kontrolowanym konfliktem.

Brak webhooka można obsłużyć przez `POST /api/interview-sessions/{id}/recover`, a import ponowić bez nowej rozmowy przez `POST /api/interviews/{id}/result/retry-import`. Ręczna korekta o nowszej rewizji nie jest nadpisywana przez spóźniony webhook. Pytania lekarza tworzą nową generację wywiadu uzupełniającego i zachowują wcześniejsze wersje raportu.

Produkcję powiadomień konfiguruje `Notifications__ProviderUrl`, `Notifications__ApiKey` i `Notifications__FrontendBaseUrl`. Bez URL używany jest adapter demonstracyjny. Adapter HTTP przekazuje gotowy link powrotu; docelowy gateway SMS/e-mail musi obsłużyć ten kontrakt.

### Test endpointów agenta

Nową rozmowę testową przygotujesz przez [elevenlabs-new-conversation.http](requests/elevenlabs-new-conversation.http): najpierw wyślij żądanie `0`, aby zalogować administrację danymi demo z tego README. JWT `accessToken` automatycznie autoryzuje tworzenie wizyty. Następnie utwórz wizytę, skopiuj `interviewInvitationToken` do linku `http://127.0.0.1:5173/i/{token}` i przeprowadź rozmowę w przeglądarce. Ten sam plik zawiera odczyt statusu oraz wyniku po zakończeniu rozmowy; daty i unikalny identyfikator wizyty generują się automatycznie. [Instrukcja krok po kroku](../docs/elevenlabs-uruchomienie.md#nowa-rozmowa-z-pliku-http).

Do odczytania zapisanego wyniku rzeczywistej rozmowy użyj [elevenlabs-test.http](requests/elevenlabs-test.http). W VS Code z rozszerzeniem REST Client wklej `linkToken` z odpowiedzi tworzącej wizytę i wysyłaj żądania po kolei. Żądania `0a` i `0b` pobierają JWT administracji oraz lekarza. Token sesji pacjenta, `visitId` i `interviewId` także są pobierane automatycznie z odpowiedzi. JWT personelu autoryzuje endpointy placówki; odczyt wyniku samej rozmowy nadal wymaga sesji pacjenta. Kopię zawierającą prawdziwy token przechowuj w ignorowanym `Backend/.local/`. Szczegóły oceny wyniku: [sprawdzenie zapisu przez HTTP](../docs/elevenlabs-uruchomienie.md#sprawdzenie-zapisu-przez-plik-http).

Testy domeny i dostawcy uruchamia `dotnet test DocPrep.sln`. Test pełnego API jest domyślnie pomijany bez izolowanej bazy PostgreSQL:

```sh
DOCPREP_TEST_POSTGRES='Host=localhost;Database=docprep_test;Username=test;Password=test' dotnet test DocPrep.sln
```

Użyj osobnej bazy testowej. Test uruchamia aplikację z autoryzacją JWT i migracjami, zastępuje dostawcę oraz sesje Redis atrapami, sprawdza izolację wywiadów, limit sesji, zmianę trybu, status przetwarzania, podpis i idempotencję webhooka. Nie kontaktuje się z ElevenLabs.
