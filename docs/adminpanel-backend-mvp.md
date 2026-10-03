# Adminpanel i kontynuacja rozmowy — implementacja MVP

Stan: 4 października 2026. Zmiany przygotowane w osobnym worktree na branchu `codex/admin-backend-mvp`, przeznaczone do scalenia z `main`.

## Wynik audytu

Backend miał już logowanie personelu, JWT/refresh tokeny, zarządzanie kontami personelu, tworzenie/zmianę/anulowanie wizyt, statusy wywiadu, wersje raportów, zgody, pytania lekarza i integrację ElevenLabs. `AdminFrontend` jest obecnie osobną aplikacją demonstracyjną: `src/services/reception.ts` i lista lekarzy korzystają z mocków. Nie wykonywał prawdziwych rezerwacji ani wysyłek.

Dla recepcji brakowało katalogu lekarzy, danych pacjenta do listy wizyt, czasu trwania wizyty, kalendarza z kontrolą kolizji i możliwości ponownego pobrania istniejącego linku. Frontend pacjenta pobierał nowe poświadczenie na każdą sesję i przenosił historię przy zmianie głos/czat, ale wyłącznie z pamięci otwartej strony.

## Zaimplementowane

- Katalog lekarzy placówki: dodanie, odczyt, zmiana nazwy/specjalizacji/domyślnego gabinetu oraz aktywacja/dezaktywacja. Dezaktywacja blokuje nowe przypisania i zachowuje istniejące wizyty.
- Utworzenie wizyty recepcji z nazwą pacjenta, telefonem/e-mailem, lekarzem, terminem, czasem trwania, gabinetem i rodzajem wizyty. PESEL jest opcjonalny; bez niego tworzona jest odrębna tożsamość dla wizyty, bez automatycznego łączenia pacjentów po nazwisku czy telefonie.
- Dane pacjenta są szyfrowane w `reception_details`. Odpowiedzi recepcji nie zawierają draftów, transkrypcji ani treści raportu.
- Lista z paginacją, filtrami dat/statusu/lekarza/gabinetu i wyszukiwaniem po nazwisku lub identyfikatorze wizyty. Wyszukiwanie nazw odbywa się po odszyfrowaniu, przed paginacją.
- Kalendarz zwracający wizyty przecinające podany przedział, również te zaczynające się przed `from`. Zakres obejmuje maksymalnie 93 dni; `from` jest włączone, `to` wyłączone. Anulowane wizyty są wyłączone z kalendarza.
- Zmiana terminu/lekarza/gabinetu/czasu trwania, synchronizacja ważności zaproszeń oraz anulowanie z unieważnieniem dostępu pacjenta. `expectedVersion` pozwala wykryć edycję nieaktualnej wizyty.
- Blokowanie kolizji lekarza oraz gabinetu dla wizyt stacjonarnych. Przedziały są półotwarte: kolejna wizyta może zacząć się dokładnie w chwili zakończenia poprzedniej. Kontrola obejmuje także istniejące endpointy integracyjne. Blokada advisory PostgreSQL na placówkę serializuje równoczesne rezerwacje.
- Gotowy link `/i/{token}` przy utworzeniu wizyty i osobny odczyt tego samego linku. Samo pobranie lub wysłanie nie obraca tokenu; regeneracja jest osobną operacją.
- Wysłanie zaproszenia przez zapisany telefon/e-mail, status próby i błąd dostarczenia. `deliveryMode: demo` jednoznacznie oznacza adapter demonstracyjny, który nie wysyła SMS/e-maila.
- Token zaproszenia ma hash do weryfikacji i szyfrowaną kopię do odzyskania linku przez uprawnioną recepcję. Tokeny rozmowy ElevenLabs i podpisane URL nadal nie są przechowywane. Linki i dane recepcji mają `Cache-Control: no-store`.
- Izolacja placówek, brak dostępu anonimowego i brak dostępu roli `Clinician` do danych recepcji.
- Migracja `ReceptionMvp`: `clinicians`, `reception_details`, `visits.DurationMinutes`, `interview_invitations.EncryptedToken`, `delivery_attempts.IsSimulated` i indeks kalendarza. Istniejące wizyty dostają domyślnie 30 minut; migracja nie usuwa danych.

## Kontrakty recepcji

Wszystkie endpointy `/api/v1/admin` wymagają JWT roli `Administrative` albo serwerowego API key roli `Administrative`/`System`. Frontend korzysta z `/api/v1/auth/login|refresh|logout|me`; API key nie powinien trafić do przeglądarki.

| Metoda | Ścieżka `/api/v1/admin` | Wynik |
|---|---|---|
| GET | `/facility` | Placówka bieżącego użytkownika |
| GET | `/doctors?includeInactive=true` | Lista lekarzy; domyślnie aktywni |
| POST | `/doctors` | Utworzenie lekarza, 201 |
| PUT | `/doctors/{id}` | Edycja/aktywacja/dezaktywacja |
| GET | `/appointments` | `{items,page,pageSize,total}` |
| POST | `/appointments` | `{appointment,invitation}`, 201 |
| GET | `/appointments/{id}` | Szczegóły recepcji |
| PUT | `/appointments/{id}` | Zmiana rezerwacji |
| POST | `/appointments/{id}/cancel` | Anulowanie, 204 |
| GET | `/calendar?from=...&to=...` | Wizyty przecinające zakres |
| GET | `/appointments/{id}/invitation` | Obecny link |
| POST | `/appointments/{id}/invitation/send` | Wysłanie istniejącego linku |
| POST | `/appointments/{id}/invitation/regenerate` | Nowy link; poprzedni zostaje unieważniony |

Filtry listy: `from`, `to`, `doctorId`, `room`, `status`, `q`, `page`, `pageSize` (maksymalnie 100). Kalendarz dodatkowo obsługuje `doctorId` i `room`. Daty są ISO 8601 z offsetem; backend zapisuje je w PostgreSQL jako UTC. `timeZone` domyślnie wynosi `Europe/Warsaw`, także przy zmianie czasu letniego/zimowego.

Przykładowy lekarz:

```json
{"id":"doctor-demo","name":"Anna Nowak","specialty":"Internista","defaultRoom":"01"}
```

Identyfikator lekarza odpowiada `clinicianId` konta personelu. Utworzenie profilu nie zakłada konta z hasłem. Aby lekarz mógł odczytywać swoje raporty, administracja tworzy konto przez istniejące `POST /api/v1/staff` z `role: "Clinician"` i tym samym `clinicianId`. Utworzenie konta lekarza uzupełnia brakujący wpis katalogu; w Development istniejące konta demo również uzupełniają katalog.

Przykładowa wizyta:

```json
{
  "patientName":"Jan Testowy",
  "phone":"+48500100200",
  "email":"jan@example.invalid",
  "doctorId":"doctor-demo",
  "scheduledAt":"2026-12-10T10:00:00+01:00",
  "durationMinutes":30,
  "room":"01",
  "visitType":"InPerson",
  "timeZone":"Europe/Warsaw",
  "sendInvitation":false
}
```

Opcjonalne pola: `externalVisitId` (bez wartości powstaje unikalny `WIZ-...`), `pesel`, `serviceExpiresAt` (domyślnie koniec dnia wizyty w podanej strefie), `locationInstructions`. Co najmniej jeden poprawny kontakt jest wymagany. Czas trwania: 5–240 minut. Dla wizyty stacjonarnej wymagany jest gabinet, ewentualnie domyślny gabinet lekarza. `sendInvitation` domyślnie jest `false`, więc dodanie wizyty nie wysyła wiadomości.

Odpowiedź `appointment` zawiera m.in. `patient`, snapshot `doctor`, `facility`, `scheduledAt`, `endsAt`, `durationMinutes`, `status`, `deliveryStatus`, `deliveryMode`, `deliveryError`, `contactChannel`, `lastSentAt`, `hasOpenSupplementationRound`, `interviewId`, `interviewStatus`, `version`. `invitation` zawiera `url`, `expiresAt`, `canStart`, `remainingSessions` i identyfikatory wizyty/wywiadu. Link do zakończonego wywiadu służy przeglądowi wyniku; `canStart` jest wtedy `false`.

Zmiana wizyty używa `scheduledAt`, `durationMinutes`, `doctorId`, `room`, `visitType`, `timeZone` oraz opcjonalnie `serviceExpiresAt`, `locationInstructions`, `expectedVersion`. Wysłanie/regeneracja przyjmują `{"channel":"Sms"}` lub `{"channel":"Email"}`. Kanał musi mieć zapisany kontakt.

Konflikty zwracają 409 i kod w `ProblemDetails.code`, np. `calendar.clinician_conflict`, `calendar.room_conflict`, `visit.version_conflict`, `clinician.unavailable`. Obcy identyfikator wizyty daje 404. Nieaktywny link/wizyta daje 409.

## Kontynuacja ElevenLabs

`POST /api/interviews/{interviewId}/sessions` oraz publiczny odpowiednik nadal pobierają od ElevenLabs nowe poświadczenie na każde połączenie: WebRTC `conversationToken` dla głosu lub WebSocket `signedUrl` dla czatu. Dodatkowo zwracają `userId` i `dynamicVariables`:

```json
{
  "language":"pl",
  "visit_type":"wywiad przed wizytą",
  "interview_type":"pre-visit",
  "is_continuation":true,
  "previous_conversation_summary":"Podsumowanie sesji: ..."
}
```

Kontekst pochodzi z zapisanych sesji tego wywiadu: z `analysis.transcript_summary`, a gdy go brakuje — z ograniczonego fragmentu transkrypcji. Ma maksymalnie 6000 znaków. Przy rundzie uzupełniającej zawiera wcześniejszy raport wywiadu tej samej wizyty i nieodpowiedziane pytania lekarza. Nie pobiera danych innych wizyt/pacjentów i nie generuje dodatkowych, domyślonych faktów.

Istniejący `AgentApi.startSession` przekazuje zmienne bez zmian do SDK. Hook frontendu dodatkowo wysyła zapisany skrót przez `sendContextualUpdate` po połączeniu; nadal przenosi bieżącą historię z pamięci przy zmianie głos/czat. Dzięki temu kontekst trafia do nowej rozmowy także po ponownym otwarciu oryginalnego linku.

Odczyt konfiguracji aktualnego agenta wykazał brak placeholderów kontekstu. Nie zmieniano jego zdalnej konfiguracji. Kod frontendu przekazuje kontekst bezpośrednio, a [instrukcja agenta](instrukcja-agenta-prowadzacego-wywiad.md) zawiera także opcjonalny blok promptu z `{{previous_conversation_summary}}`, `{{is_continuation}}` i `{{interview_type}}`, przydatny przed pierwszą wypowiedzią agenta. Mechanizmy odpowiadają [dynamic variables](https://elevenlabs.io/docs/eleven-agents/customization/personalization/dynamic-variables) i [JavaScript SDK](https://elevenlabs.io/docs/eleven-agents/libraries/java-script) ElevenLabs.

Podsumowanie jest dostępne po zapisaniu webhooka. Przy natychmiastowej zmianie trybu, przed webhookiem, frontend przenosi własną historię. Po przerwaniu sesji i powrocie przed webhookiem serwer może jeszcze nie mieć kontekstu. Zakończony wywiad pozostaje w trybie przeglądu; pytania lekarza tworzą odrębną generację uzupełniającą. Limit publicznego zaproszenia pozostaje równy trzem sesjom, a regeneracja resetuje limit zaproszenia.

## Konfiguracja lokalna

Compose obsługuje `FRONTEND_BASE_URL` (domyślnie `http://127.0.0.1:5173`), `NOTIFICATIONS_PROVIDER_URL` i `NOTIFICATIONS_API_KEY`. Bez gatewaya powiadomień aktywny jest adapter demonstracyjny. Po podłączeniu gatewaya rzeczywiste wysyłki używają pola `interviewUrl`. CORS obejmuje również port adminpanelu 5174.

Stare zaproszenia mają wyłącznie hash i nie da się odzyskać ich jawnego tokenu. Odczyt linku zwraca `invitation.regeneration_required`; jednorazowa regeneracja przez istniejący `/api/v1/integration/visits/{id}/invitations` z polem `contact` tworzy odzyskiwalny link. Starsze wizyty bez danych recepcji zwracają `patient: null`.

## Co pozostaje do podłączenia na frontendzie

1. Zastąpić mockowy `AdminFrontend/src/services/reception.ts` adapterem JWT do powyższych endpointów. Backend jest gotowy, ale demonstracyjny UI nie przełącza się automatycznie na API.
2. Pobierać lekarzy i placówkę z API zamiast `src/data/mock.ts`; mapować odpowiedzi do modelu prezentacyjnego. `initials` i `color` są polami UI, nie backendu.
3. Kopiować `invitation.url` z API zamiast adresu `.example`. Rozdzielić przyciski wysyłania i regeneracji; pokazywać `deliveryMode: demo`.
4. Raporty otwierać w roli lekarza przez istniejące endpointy `report-versions` (tylko przypisane, zatwierdzone raporty ze zgodą). Recepcja ma status przygotowania, nie treść medyczną.
5. Podłączyć prawdziwy gateway SMS/e-mail, jeśli MVP ma wysyłać wiadomości poza demo.

Grafiki pracy lekarzy, urlopy, cykliczne sloty i synchronizacja z zewnętrznym kalendarzem nie są częścią obecnego MVP. Kalendarz obejmuje konkretne rezerwacje i kontrolę ich kolizji.

## Weryfikacja

- Build backendu Release bez błędów i ostrzeżeń.
- 26 testów backendu, w tym dwa rzeczywiste scenariusze HTTP na osobnej bazie PostgreSQL: proces ElevenLabs oraz recepcja (JWT, izolacja placówek/roli, szyfrowanie, odczyt/wysyłka/regeneracja linku, lista/kalendarz, zmiana wersji, anulowanie, równoczesna kolizja).
- Build frontendu i 12 testów jednostkowych w izolowanym worktree.
- 4 testy przeglądarkowe na desktopie i telefonie: istniejący flow głos/czat oraz powrót do rozmowy. Test kontynuacji używa atrapy transportu ElevenLabs i prawdziwego hooka/API adaptera; sprawdza dwukrotne otwarcie linku, świeże poświadczenia i przekazanie zapisanego kontekstu. Weryfikacja nie generuje płatnych rozmów ElevenLabs.
- Lokalny kontener API należy odtworzyć po scaleniu; migracje są wykonywane przy starcie. Istniejący PostgreSQL i woluminy pozostają na miejscu.
