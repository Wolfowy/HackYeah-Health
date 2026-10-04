# Przed wizytą — frontend

React + TypeScript + Vite. Konto pacjenta, wizyty oraz raport korzystają z backendu ASP.NET; rozmowa używa oficjalnego SDK ElevenLabs. Makieta jest dostępna osobno pod `/demo`. Animowana sfera ma przezroczysty środek, pięć fal zmieniających kolor i krycie oraz reakcję na poziom audio.

Pełny opis ekranów, przepływów i modeli: [docs/przed-wizyta-frontend.md](../docs/przed-wizyta-frontend.md).

## Uruchomienie

Node.js 22.12+:

```sh
cd Frontend
npm install
npm run dev
```

Adres: `http://127.0.0.1:5173`. Vite przekazuje `/api` do `http://127.0.0.1:8080`.

```sh
npm run build
npm test
npm run test:e2e
```

E2E używa zainstalowanego Chrome i sprawdza komputer oraz telefon. Transport SDK i odpowiedzi serwera w testach są zastępowane atrapami; testy nie wykonują płatnych połączeń.

## Ekrany i linki

| Adres                         | Zachowanie                                                                                        |
| ----------------------------- | ------------------------------------------------------------------------------------------------- |
| `/`                           | Rzeczywiste logowanie pacjenta, własne wizyty, profil i raporty.                                  |
| `/demo`                       | Osobny panel z fikcyjnymi danymi, wywiadem i kontem demonstracyjnym.                              |
| `/i/demo-appointment-1`       | Rozmowa demo dla pierwszej wizyty z kartą terminu, bez sidebaru.                                  |
| `/i/demo-appointment-2`       | Sama rozmowa demo dla drugiej wizyty.                                                             |
| `/i/{token}`                  | Rzeczywiste zaproszenie: walidacja backendu, wymiana na sesję ograniczoną do jednego wywiadu.     |
| `/visits/{visitId}/interview` | Rozmowa przez API pacjenta. Wymaga sesji pacjenta DocPrep dostarczonej przez aplikację nadrzędną. |
| `/rozmowa`                    | Adres po wymianie tokenu zaproszenia. Odświeżenie wymaga ponownego otwarcia otrzymanego linka.    |

Token zaproszenia po autoryzacji znika z URL. Sesja aplikacyjna i tymczasowy credential ElevenLabs są tylko w pamięci. `Referrer-Policy` ustawiono przez meta na `no-referrer`. Na hostingu należy ustawić fallback tras do `index.html` i nie zapisywać tokenów z adresów zaproszeń w logach dostępu ani analityce.

## ElevenLabs i backend

Instrukcja serwerowa: [Backend/README.md](../Backend/README.md). Placówka tworzy wizytę przez `POST /api/v1/integration/visits`. Zwrócony `interviewInvitationToken` służy do zbudowania adresu `/i/{token}` na domenie frontendu. Wymiana zaproszenia zwraca anonimowy JWT i `interviewId`; kolejne żądania trafiają do `/api/interviews/{interviewId}/sessions` i `/result`.

Konfiguracja klucza API, agenta i webhooka oraz zweryfikowany zakres integracji: [ElevenLabs — uruchomienie](../docs/elevenlabs-uruchomienie.md).

Opcjonalnie skopiuj `.env.example` do `.env.local`:

```dotenv
VITE_API_BASE_URL=
```

Pusty adres API używa proxy / tej samej domeny. Nie ma mostka `X-Patient-Id`. Aplikacja nadrzędna może przekazać sesję pacjenta otrzymaną z `/api/v1/patient-access/link/exchange` lub `/code/exchange` przez `agentApi.setPatientSession(token)`; adapter przechowuje ją w pamięci i wysyła jako bearer token. Przy bezpośrednim otwarciu ścieżki wizyty bez takiej sesji serwer odmówi dostępu. Strona główna korzysta z API konta pacjenta. JWT konta i sesja ograniczona do jednej wizyty są rozdzielone; żadne poświadczenia nie trafiają do localStorage ani sessionStorage. Odświeżenie strony wymaga ponownego zalogowania. Rejestracja jest dostępna przez przycisk „Konto pacjenta” w zweryfikowanym linku do wizyty. Hasło wymaga 12 znaków, wielkiej i małej litery oraz cyfry. Uwierzytelnienie JWT personelu backendu jest osobnym mechanizmem. Nigdy nie dodawaj klucza ElevenLabs do zmiennych `VITE_*`.

- Głos: kliknięcie mikrofonu → zgoda na mikrofon → backend wydaje `conversationToken` → SDK zestawia WebRTC. Podczas łączenia rozlega się łagodny sygnał, który respektuje przełącznik dźwięku. Połączenie, pierwsza wypowiedź, błąd, anulowanie i opuszczenie ekranu zatrzymują sygnał. Można anulować oczekiwanie na mikrofon lub połączenie. Stany połączenia, mówienia, słuchania i wyciszenia sterują sferą. Zakończenie i opuszczenie ekranu zamykają transport SDK.
- Tekst: backend wydaje `signedUrl` → SDK używa WebSocket z `textOnly: true`; mikrofon nie jest potrzebny. Enter wysyła, Shift+Enter dodaje linię.
- Zmiana trybu: poprzedni transport jest zamykany i oznaczany jako kontynuacja wywiadu; historia pozostaje widoczna i trafia do nowej sesji jako kontekst. Każde rozpoczęcie zużywa jedną z maksymalnie trzech sesji zaproszenia.
- Wynik: backend zapisuje podpisany webhook. Adapter odczytuje `structuredData`, `schemaVersion`, `extractionStatus`, `importStatus` i `issues`, zachowując zgodność z `structuredDataJson`. Niepoprawny legacy JSON nie przerywa ekranu; edytowalne dane są pobierane z typowanego `PatientInterviewView`. Interfejs odpytuje wynik przez minutę i pozwala sprawdzić go ponownie. Raport powstaje na backendzie i jest przekazywany lekarzowi po zatwierdzeniu całej treści przez pacjenta.
- Błędne, wygasłe lub unieważnione zaproszenie blokuje uruchomienie SDK. Ukończone zaproszenie otwiera podsumowanie i korektę, bez rozpoczynania kolejnej sesji agenta. Brak mikrofonu pozwala przejść do tekstu.
- Transkrypcja: `displayAgentText` usuwa rozpoznane angielskie i polskie oznaczenia emocji/wykonania z tekstu asystenta, bieżącego pytania i podsumowania. Nie zmienia wypowiedzi pacjenta ani surowej historii; zachowuje dawki i nieznane adnotacje, np. `[500 mg]`. Niepełny końcowy tag jest ukrywany, gdy ma co najmniej trzy znaki pasujące do znanego oznaczenia. Listę używanych tagów można rozszerzyć w `src/lib/agent-text.ts`.

Implementację zweryfikowano z [dokumentacją JavaScript SDK](https://elevenlabs.io/docs/eleven-agents/libraries/java-script). `Conversation.startSession()` zwraca instancję; identyfikator odczytujemy przez `getId()`.

## Ścieżka MVP od linku pacjenta

1. Recepcja tworzy wizytę przez `POST /api/v1/admin/appointments` z `sendInvitation: false` i kopiuje `invitation.url`. Otwórz ten link `/i/{token}`. Po autoryzacji token znika z URL; karta pokazuje dane tej wizyty.
2. Głos: **Rozpocznij rozmowę** i zgoda na mikrofon. Tekst: **Czat** → **Rozpocznij czat** → odpowiedzi przez **Wyślij odpowiedź**. Czat nie wywołuje `setVolume()` ani `setMicMuted()`, których `TextConversation` w SDK nie obsługuje.
3. **Zakończ rozmowę**. ID dostawcy jest zapisywane już w callbacku `onConnect`; fallback używa `getId()`. SDK i backend kończą daną sesję raz. Zmiana trybu i anulowanie startu oznaczają transport przez `continuesInterview: true`, aby nie zakończyć całego wywiadu.
4. Poczekaj na wynik serwera. Lokalna transkrypcja i sam `finalReport` nie odblokowują zatwierdzenia. Przy `extractionStatus` lub `importStatus` równym `pending` nadal trwa oczekiwanie. Błąd ekstrakcji/importu jest pokazany jawnie w przeglądzie, bez udawania kompletności danych. Po minucie użyj **Sprawdź podsumowanie** lub **Odzyskaj wynik rozmowy** dla znanej sesji.
5. Sprawdź sekcje; opcjonalnie **Edytuj podsumowanie** → **Zapisz poprawki**. Przy sekcjach i wpisach dostępne są też krótsze formularze, np. **Edytuj lek Paracetamol** i **Edytuj alergen Penicylina**. Zapisz odpowiedzi na pytania lekarza, jeżeli są wymagane. **Zatwierdź i udostępnij raport** jednym kliknięciem akceptuje całą treść, obserwacje i widoczne braki, zapisuje wersję i przekazuje ją lekarzowi przypisanemu do wizyty.
6. Po zatwierdzeniu sprawdź status udostępnienia i użyj **Pobierz PDF**. UI nie pokazuje pobrania JSON ani drugiego przycisku udostępnienia. **Cofnij zgodę na udostępnienie** blokuje kolejne odczyty raportu w placówce. Gdy generowanie PDF zawiedzie, **Ponów przygotowanie i udostępnienie raportu** odtwarza PDF i automatycznie udostępnia tę samą wersję; nie zatwierdza ponownie i nie tworzy kolejnego snapshotu.
7. Ponowne otwarcie tego samego ukończonego linku otwiera raport bez startu SDK, do końca ważności dostępu. Odświeżenie `/rozmowa` wymaga ponownego otwarcia oryginalnego linku, bo tokeny pozostają wyłącznie w pamięci.

Wspólny odbiór recepcja → pacjent → lekarz opisuje [plan MVP](../docs/mvp-wspolny-przeplyw.md). Testy frontendowe używają kontrolowanych atrap API/transportu SDK; pełny test bazy, podpisanego webhooka/recovery i konfiguracji ElevenLabs wykonuje koordynator.

## Raport i konto — integracja z aktualnym backendem

Po zakończeniu lub ponownym otwarciu ukończonego linku frontend pobiera `GET /api/v1/interview`. Pokazuje powód wizyty, objawy i chronologię, leki z powodem przyjmowania, alergie, choroby przewlekłe, pytania i uwagi. Brak danych i odpowiedź „nie wiem” pozostają odrębne od potwierdzonego braku leków/alergii/chorób.

| Akcja                                                | Endpoint                                                                                                |
| ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| Poprawki całego raportu, tekstowe dopowiedzenie      | `PUT /api/v1/interview/draft` z `expectedRevision`                                                      |
| Głosowe dopowiedzenie                                | `POST /api/v1/interview/voice/transcribe`, multipart z polem `audio`                                    |
| Decyzja o obserwacji (tylko kompatybilność API)      | `PUT /api/v1/interview/observations/{id}/decision` — zwykła ścieżka UI nie wywołuje                     |
| Odpowiedzi na pytania lekarza                        | `POST /api/v1/interview/supplementation-round/answers`                                                  |
| Zatwierdzenie całego raportu i udostępnienie         | `POST /api/v1/interview/approve`                                                                        |
| Cofnięcie udostępnienia                              | `PUT /api/v1/interview/consent` z `granted: false`                                                      |
| Pobranie zapisanej wersji PDF                        | `GET /api/v1/interview/report.pdf`                                                                      |
| Naprawa PDF i dokończenie udostępnienia              | `POST /api/v1/interview/report/regenerate-pdf`, potem `PUT /api/v1/interview/consent` z `granted: true` |
| Ponowny import wyniku                                | `POST /api/interviews/{id}/result/retry-import`                                                         |
| Odzyskanie wyniku aktywnej sesji przy braku webhooka | `POST /api/interview-sessions/{id}/recover`                                                             |

**Kontrakt zatwierdzania:** frontend wysyła do `POST /api/v1/interview/approve`:

```json
{
  "confirmIncompleteReport": true,
  "acceptAllObservations": true,
  "shareWithFacility": true
}
```

`confirmIncompleteReport` jest `true`, gdy `draft.clarifications` zawiera braki, w pozostałych przypadkach `false`. Jeden przycisk obejmuje akceptację widocznych braków, bez dodatkowego checkboxa. Backend musi akceptować obserwacje `Pending`, zapisać niezmienną wersję i udostępnić ją po gotowym PDF w tej operacji. Nie stosujemy po stronie frontendu sekwencji pojedynczych `/decision` ani osobnego `/consent` do zwykłego zatwierdzania. `/complete` pozostaje dostępne w adapterze dla kompatybilności, ale ten przycisk go nie wywołuje.

Przyciski **Zatwierdź i udostępnij raport**, **Dopowiedz głosowo** i **Dopowiedz na czacie** są nieaktywne podczas przetwarzania. Zatwierdzenie jest blokowane przy braku wymaganego powodu wizyty, niezapisanych odpowiedziach lekarzowi lub otwartym formularzu poprawek. Brakujące odpowiedzi należy zapisać przed zatwierdzeniem. Obserwacje są widoczne w raporcie, bez osobnych decyzji. UI wskazuje numer zapisanej/udostępnionej wersji; edycja nie zmienia starszego snapshotu. API nie podaje rewizji zatwierdzonego draftu, więc po ponownym otwarciu UI pokazuje istniejącą wersję i pozwala jawnie zatwierdzić nową.

**Edycja pól:** przy każdej sekcji dostępne jest **Edytuj: …**, a przy objawie, leku, alergenie i chorobie — edycja konkretnego wpisu. `DraftEditor` pokazuje wyłącznie wybraną sekcję/wpis, ale przechowuje cały draft. Zapis używa `expectedRevision` i zachowuje pozostałe wpisy, przebieg objawów oraz stany nieznanych danych. Sekcyjne formularze zachowują dodawanie i usuwanie. Konflikt rewizji pozostawia wpisane poprawki.

Makieta `/demo` używa takiego samego jednego przycisku, bez checkboxa, JSON i osobnego potwierdzenia wysyłki; udostępnienie pozostaje symulowane.

Dopowiedzenie tekstowe jest dopisywane do `additionalNotes` z zachowaniem pozostałych danych i stanów. Głosowe nagrywa maksymalnie 2 minuty/15 MB i wysyła nagranie do endpointu transkrypcji. Pacjent sprawdza i poprawia rozpoznany tekst przed zapisem. MIME jest wysyłany bez parametru `codecs`, zgodnie z walidacją backendu. Mikrofon zostaje zwolniony po zatrzymaniu lub opuszczeniu formularza. Nie uruchamiamy blokowanej sesji ElevenLabs na ukończonym wywiadzie.

**Konfiguracja dopowiedzenia głosowego:** backendowy `HttpTranscriptionService` wymaga `Transcription:Endpoint` (zmienna `Transcription__Endpoint`) oraz opcjonalnie `Transcription:ApiKey` (`Transcription__ApiKey`). Usługa otrzymuje surowy plik audio przez POST i zwraca `{ "text": "..." }`. Obecny `compose.local.yaml` nie przekazuje tych zmiennych, więc właściciel backendu musi dodać je do `environment` API i odtworzyć kontener. Sam klucz agenta ElevenLabs nie konfiguruje tej osobnej usługi. Bez konfiguracji UI pokazuje komunikat i pozwala pisać.

Konflikt rewizji nie kasuje wprowadzonych poprawek. Po konflikcie można odczytać aktualny raport, a następnie anulować edycję i otworzyć formularz z nową rewizją, przenosząc potrzebne poprawki. Ponowny import respektuje serwerową ochronę nowszych zmian ręcznych. Błąd generowania PDF pozostawia zatwierdzoną treść i udostępnia jeden przycisk ponowienia PDF wraz z udostępnieniem. Jeżeli sam zapis zgody podczas ponowienia zawiedzie, przycisk pozostaje dostępny i nie tworzy następnej wersji raportu.

Konto korzysta z `/api/v1/patient-account/auth/register|login|refresh`, `/me`, `/visits`, `/visits/{id}/session` oraz `/logout`. Refresh jest rotowany i współdzielony przez równoległe żądania. Przed otwarciem wizyty frontend otrzymuje osobną sesję ograniczoną do tej wizyty. Konto pacjenta nie przyjmuje kont personelu `admin@docprep.local` / `doctor@docprep.local` z README backendu.

Po aktualizacji kodu backendu przebuduj lokalne API, jeżeli OpenAPI nadal nie zawiera nowych endpointów:

```sh
cd Backend
docker compose -f compose.local.yaml --env-file .env up --build -d api
```

Weryfikacja 4.10.2026: adaptery frontendu przeszły rzeczywisty lokalny przepływ metadane → draft → zatwierdzenie → JSON/PDF → zgoda/cofnięcie oraz konto → profil → własna wizyta → ograniczona sesja. Test wykorzystał nową fikcyjną wizytę i konto; nie uruchamiał ElevenLabs. E2E na komputerze i telefonie używa atrap SDK/API i sprawdza również konflikty, niepełne dane, obserwacje, pytania lekarza, ponowienie importu/PDF i rozpoznawanie dopowiedzenia.

## Metadane wizyty — kontrakt dla backendu (FE-03 / BE-01)

Wspólna karta `AppointmentCard` działa nad rozmową z linka oraz w panelu demo. Frontend przyjmuje opcjonalne `interview.visit` w odpowiedzi `GET /api/public/interviews/{token}` i `GET /api/visits/{visitId}/interview`. Aktualny backend zwraca te metadane w istniejących endpointach:

```json
{
  "interview": {
    "id": "interview-id",
    "displayName": "Wywiad przed wizytą",
    "visitDate": "2026-12-10T10:00:00Z",
    "status": "pending",
    "visit": {
      "id": "visit-id",
      "externalVisitId": "external-visit-id",
      "scheduledAt": "2026-12-10T10:00:00Z",
      "timeZone": "Europe/Warsaw",
      "serviceExpiresAt": "2026-12-10T09:00:00Z",
      "doctor": { "id": "doctor-id", "name": "lek. Anna Testowa", "specialty": "Neurologia" },
      "facility": {
        "id": "facility-id",
        "name": "Przychodnia Testowa",
        "address": "ul. Testowa 12, Warszawa"
      },
      "room": "204",
      "visitType": "Konsultacja w placówce",
      "locationInstructions": "Drugie piętro, wejście od ulicy."
    }
  }
}
```

Przykład zawiera fikcyjne dane. Endpoint publiczny opakowuje dane w `interview`, natomiast `/api/visits/{id}/interview` zwraca bezpośrednio `AgentInterviewView`. `scheduledAt` powinno mieć offset lub `Z`, a `timeZone` być nazwą IANA. Karta używa tej strefy, domyślnie `Europe/Warsaw`, także przy zmianie czasu; nieprawidłowa strefa wraca do domyślnej. Widać datę z rokiem, godzinę, lekarza, specjalizację, placówkę i adres. Gabinet, forma wizyty (np. `InPerson` jest wyświetlane jako „Wizyta w placówce”) oraz wskazówki są rozwijane w szczegółach. `serviceExpiresAt` jest przewidziane w modelu do późniejszej obsługi edycji.

Pozostałe pola mogą być pominięte lub mieć `null`. Przy obecnym API bez `visit` karta bierze wyłącznie termin z `visitDate`, a lekarza i placówkę oznacza jako niepodane. Nie pobiera ich z danych demo. Nieprawidłowy lub brakujący termin jest oznaczony jako niepodany. Podczas autoryzacji pozostaje istniejący stan ładowania, a karta pojawia się po pomyślnym odczycie. Metadane sprawdzono również z nową wizytą utworzoną na aktualnym lokalnym API.

## Zakres demo

Panel i dwa jawne linki `demo-appointment-*` używają fikcyjnych danych w pamięci. Symulacja głosu pozwala wpisać lub wybrać transkrypcję; nie nagrywa mikrofonu. Konto demo przyjmuje fikcyjny e-mail i dowolne niepuste hasło, którego nie zapisuje ani nie wysyła. Odświeżenie/wylogowanie czyści stan.

Raport demo można edytować, zatwierdzić i osobno udostępnić lub cofnąć zgodę. Zatwierdzone wersje są niezmienne; eksport JSON i druk/PDF korzystają z tego samego snapshotu. Rzeczywiste wizyty i raporty używają `AccountWorkspace` / `ReportReview`; demonstracja pod `/demo` nie zapisuje danych do backendu.

## Kod

- `src/models.ts`, `src/data/mock.ts`, `src/lib/interview.ts` — modele i reguły wersji/zgód demo.
- `src/lib/routes.ts`, `src/components/StandaloneConversation.tsx` — linki i ekran bez sidebaru.
- `src/lib/agent-api.ts`, `backend-http.ts`, `patient-report.ts` — kontrakty API, autoryzacja, raport, błędy i credentiale.
- `src/lib/patient-account-api.ts`, `components/AccountEntry.tsx`, `AccountWorkspace.tsx` — rzeczywiste konto, profil i własne wizyty.
- `components/ReportReview.tsx`, `DraftEditor.tsx`, `ReportSupplement.tsx` — raport, korekta, dopowiedzenie, wersje i zgody.
- `src/hooks/useAgentConversation.ts` — cykl życia SDK, historia, mikrofon, poziom audio i wynik.
- `src/lib/connection-tone.ts`, `src/lib/agent-text.ts` — sygnał łączenia i czyszczenie tekstu modelu do wyświetlenia.
- `src/components/AppointmentCard.tsx`, `src/lib/visit.ts` — współdzielona karta i formatowanie terminu w strefie wizyty.
- `src/components/Orb.tsx` — SVG fal bez wypełnienia środka.
- `src/lib/*.test.ts`, `tests/*.spec.ts` — reguły danych, kontrakt API i przepływy przeglądarkowe.
