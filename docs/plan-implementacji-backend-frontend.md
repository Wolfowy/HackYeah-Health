# Przed wizytą — plan implementacji backendu i frontendu

Audyt bazowy wykonano 3 października 2026 r. dla commita `27c0db6`. Aktualizacja 4.10.2026 uwzględnia nowe README, endpointy i modele backendu oraz podłączenie funkcji do frontendu. Kod backendu nie był zmieniany; lokalny kontener API został przebudowany do aktualnej wersji.

## Aktualizacja 4.10.2026 — podłączone funkcje

| Funkcja dostępna w nowym backendzie                                             | Integracja frontendu                                                                                    |
| ------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| Metadane wizyty: lekarz, specjalizacja, termin/strefa, placówka, adres, gabinet | Rzeczywista `AppointmentCard`, także w linku bez konta.                                                 |
| Normalizacja ekstrakcji i import do `InterviewDraft`                            | Odczyt statusów ekstrakcji/importu; sekcje i stany z typowanego `PatientInterviewView`.                 |
| Zakres `report:review` anonimowego JWT i ponowne otwarcie ukończonego linku     | Automatyczne otwarcie raportu zamiast startu SDK.                                                       |
| Edycja draftu z `expectedRevision`                                              | Pełny formularz, zachowanie chronologii/stanów, obsługa konfliktu bez kasowania poprawek.               |
| Obserwacje, braki i pytania lekarza                                             | Jawne decyzje pacjenta, potwierdzenie niepełnego raportu i zapis odpowiedzi.                            |
| Zatwierdzenie, wersje, zgody, JSON/PDF                                          | Rzeczywisty zapis; osobna zgoda/cofnięcie, pobrania, ponowienie PDF.                                    |
| Dodatkowe uwagi po zakończeniu rozmowy                                          | Zapis tekstu do draftu, głos przez transkrypcję i sprawdzenie tekstu przed zapisem.                     |
| Recover / retry-import                                                          | Odzyskanie wyniku bieżącej sesji i ponowny import bez uruchamiania nowej rozmowy.                       |
| Konto pacjenta, JWT/refresh, profil/avatar, własne wizyty i sesje               | `/` korzysta z rzeczywistego API; stara makieta jest pod `/demo`. Rejestracja ze zweryfikowanego linku. |

Weryfikacja: testy jednostkowe, E2E z atrapami na komputerze i telefonie oraz rzeczywisty lokalny test adapterów: utworzenie fikcyjnej wizyty → metadane → draft → zatwierdzenie → PDF/JSON → zgoda/cofnięcie; osobno rejestracja → profil → własne wizyty → login → ograniczona sesja → logout. Nie wykonywano płatnej rozmowy ani nowego testu webhooka ElevenLabs.

Pozostałe zależności: konfiguracja `interview_json` i rzeczywisty test ekstrakcji dla głosu/czatu; osobna usługa `Transcription__Endpoint` dla głosowych dopowiedzeń (obecny compose jej nie przekazuje); frontend personelu FE-06; produkcyjne powiadomienia i pozostałe procesy operacyjne z BE-06/BE-08. API nie zwraca rewizji ostatnio zatwierdzonego draftu ani stanu PDF, więc po ponownym otwarciu frontend wskazuje istniejącą wersję, a błąd PDF rozpoznaje z odpowiedzi operacji. Warto dodać te pola do `PatientInterviewView`.

Cel: placówka dodaje wizytę, pacjent widzi jej szczegóły i rozmawia głosowo lub tekstowo, backend odbiera uporządkowane dane, pacjent sprawdza i poprawia raport, a lekarz otrzymuje zatwierdzoną wersję po osobnej zgodzie.

Założenia biznesowe: [dokumentacja procesów](DocPrep%20-%20dokumentacja%20procesów.md). Konfiguracja i testy aktualnej integracji: [ElevenLabs — uruchomienie](elevenlabs-uruchomienie.md). Pierwotna koncepcja aplikacji obejmuje także konto pacjenta, listę wizyt i dodatkowy opis; dokumentacja procesów MVP przewiduje dostęp bez konta. Dlatego poniżej uwzględniono oba zakresy: najpierw kompletny przepływ jednej wizyty z linku, następnie rzeczywisty wariant z kontem.

## 1. Punkt wyjścia z audytu bazowego (przed aktualizacją)

| Obszar                      | Stan obecny                                                                                                                                               | Pozostała praca                                                                                                                      |
| --------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| Dodanie wizyty              | `POST /api/v1/integration/visits` tworzy wizytę, draft, wywiad ElevenLabs i zaproszenia. Działa przez JWT administracji albo klucz integracyjny.          | Rozszerzyć dane lekarza i miejsca, podłączyć formularz administracji, obsłużyć zmianę terminu i przypisania lekarza.                 |
| Dostęp z linku              | Walidacja zaproszenia, anonimowy JWT, ekran bez sidebaru, usuwanie tokenu z URL.                                                                          | Dodać dostęp do korekty, zatwierdzania i późniejszego odczytu wyniku bez ponownego uruchamiania rozmowy.                             |
| Głos i czat                 | SDK ElevenLabs, WebRTC/WebSocket, mikrofon, wyciszenie, sfera, zmiana trybu, sygnał łączenia i czyszczenie tagów modelu.                                  | Obsługa wznowienia i wyniku wielu sesji.                                                                                             |
| Zapis po rozmowie           | Podpisany `post_call_transcription` zapisuje transcript, analysis, metadata, podsumowanie i JSON dostawcy.                                                | Zweryfikować kompletność ekstrakcji, normalizować dane i przenieść je do raportu DocPrep. Ten sam odbiornik służy głosowi i czatowi. |
| Odczyt podsumowania         | `GET /api/interviews/{id}/result` zwraca status, `finalReport` i `structuredDataJson`. Front pokazuje tekst podsumowania.                                 | Zwracać i wyświetlać typowane sekcje, braki i stan przetwarzania danych strukturalnych.                                              |
| Karta wizyty                | Wspólna karta działa w panelu i nad rzeczywistą rozmową bez sidebaru. Obsługuje opcjonalne `interview.visit`; obecne API podaje termin przez `visitDate`. | Dostarczyć rzeczywiste metadane z backendu (BE-01) i zweryfikować kartę z wizytą utworzoną przez API.                                |
| Raport dla lekarza          | Istnieją edycja draftu, obserwacje, zatwierdzanie, zgody, wersje JSON/PDF i odczyt przypisanego lekarza.                                                  | Połączyć ten proces z wynikiem ElevenLabs i rzeczywistym UI; sam webhook nie tworzy `ReportVersion`.                                 |
| Konto, avatar i lista wizyt | Frontend korzysta z danych demo. JWT backendu dotyczy personelu.                                                                                          | Zaprojektować osobną tożsamość pacjenta, własność wizyt i API konta.                                                                 |
| Powiadomienia               | `DemoNotificationSender` zwraca sukces bez wysłania SMS/e-maila.                                                                                          | Podłączyć dostawcę, rzeczywiste linki, ponowienia i statusy dostarczenia.                                                            |
| Historia i uzupełnienia     | Backend ma proste reguły porównania objawów, decyzje pacjenta i rundy pytań lekarza.                                                                      | Podłączyć wynik rozmowy, poprawić porównania i obsłużyć nową rozmowę uzupełniającą po zakończeniu pierwszej.                         |

Dodawanie wizyty i odczyt podsumowania wymagają rozszerzenia istniejących endpointów, a nie tworzenia ich od początku. W dotychczasowym teście rzeczywista rozmowa, webhook i tekst raportu zapisały się poprawnie, ale `structuredDataJson` zawierał `{}`. Poprawny zapis techniczny nie potwierdza zebrania pełnego wywiadu.

## 2. Kolejność prac

| Etap                      | Backend                                                | Frontend                                                                               | Warunek zakończenia                                                                                     |
| ------------------------- | ------------------------------------------------------ | -------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| 1 — dane wizyty i rozmowa | BE-01, kontrakt karty wizyty.                          | FE-01, FE-02 i FE-03. Dźwięk i czyszczenie tekstu można zrobić równolegle z backendem. | Pacjent widzi prawdziwą wizytę, słyszy sygnał łączenia, a tekst modelu nie zawiera oznaczeń emocji.     |
| 2 — uporządkowany wynik   | BE-02, BE-03 i podstawowa obsługa błędów z BE-04.      | FE-04 i dostęp do sprawdzenia wyniku.                                                  | Głos i czat kończą się zapisanym, typowanym wynikiem oraz wypełnionym draftem.                          |
| 3 — raport i placówka     | BE-05 oraz uprawnienia i podstawy powiadomień z BE-06. | FE-05, FE-06.                                                                          | Pacjent poprawia i zatwierdza raport, udziela zgody, a przypisany lekarz widzi tę samą wersję JSON/PDF. |
| 4 — pełne procesy MVP     | Pozostałe BE-04, BE-06, BE-07, BE-08.                  | Uzupełnienia, błędy, powrót do raportu i stany panelu.                                 | Działają ponowienia, pytania lekarza, cofnięcie zgody, anulowanie i usunięcie danych.                   |
| 5 — wariant z kontem      | BE-09.                                                 | FE-07.                                                                                 | Logowanie pacjenta, profil, avatar, własne wizyty i dodatkowy opis korzystają z API.                    |

Priorytet pierwszego wdrożenia: etapy 1–3. Pełny zakres procesów MVP wymaga etapu 4, a pełna pierwotna koncepcja z dwoma trybami dostępu również etapu 5.

## 3. Backend — lista prac i wdrożone elementy

Zaznaczone zadania oznaczają dostępność implementacji w obecnym kodzie/API; nie zastępują testów produkcyjnych ani nowej rozmowy z dostawcą.

### BE-01. Rozszerzenie wizyty i jej metadanych

- [x] Rozszerzyć `CreateVisitRequest`, `CreateVisitCommand`, `VisitProcess` i migrację o dane potrzebne do pokazania wizyty. Minimalny model poniżej.
- [x] Przechowywać snapshot danych prezentacyjnych lekarza i miejsca dla konkretnej wizyty. `AssignedClinicianId` pozostaje identyfikatorem uprawnienia, a nazwisko wyświetlane na karcie nie nadaje dostępu do raportu.
- [x] Rozszerzyć `AgentInterviewView` o `visit` i zwracać tę samą strukturę w publicznym widoku zaproszenia oraz widoku autoryzowanego wywiadu. Nie zwracać PESEL-u ani danych innych wizyt.
- [x] Uzupełnić listę i szczegóły wizyt placówki o nowe metadane, filtrowanie po dacie/statusie i paginację. Pozostawić administracji wyłącznie dane operacyjne.
- [x] Dodać zmianę terminu, miejsca i przypisanego lekarza. Dopilnować uprawnień po zmianie przypisania oraz zasad ważności istniejących linków.
- [x] Zachować unikalność `(FacilityId, ExternalVisitId)`, która już istnieje w bazie. Doprecyzować kontrakt ponowienia tworzenia: konflikt albo idempotentne zwrócenie istniejącej wizyty, bez podwójnego zaproszenia i wysyłki.
- [x] Uzupełnić `.http` i Swagger o nowe pola oraz przykłady błędów walidacji.

Proponowany kontrakt `VisitDetails`:

| Pole                                               | Znaczenie                                                                                         |
| -------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `id`, `externalVisitId`                            | Identyfikatory wizyty, bez danych identyfikujących pacjenta.                                      |
| `scheduledAt`, `timeZone`                          | Termin ISO 8601 z offsetem oraz strefa prezentacji, np. `Europe/Warsaw`.                          |
| `serviceExpiresAt`                                 | Termin końca możliwości edycji i uzupełnienia.                                                    |
| `doctor.id`, `doctor.name`, `doctor.specialty`     | Przypisany lekarz i dane karty wizyty; dopuszczalne wartości nieznane.                            |
| `facility.id`, `facility.name`, `facility.address` | Placówka i adres świadczenia.                                                                     |
| `room`, `visitType`, `locationInstructions`        | Gabinet, forma wizyty i opcjonalne wskazówki; dla wizyty zdalnej nie wymagać fizycznego gabinetu. |
| `status`, `interviewId`, `interviewStatus`         | Oddzielny stan procesu wizyty i rozmowy.                                                          |

**Odbiór:** po utworzeniu wizyty te same dane lekarza, miejsca i terminu wracają z API i są widoczne w rzeczywistej karcie; inna placówka nie może ich zmieniać.

### BE-02. Odbiór i normalizacja danych strukturalnych po głosie lub czacie

Aktualny backend normalizuje `interview_json`, rozróżnia stan ekstrakcji i importu oraz mapuje wynik do draftu. Pozostaje sprawdzenie konfiguracji działającego agenta i ekstrakcji z rzeczywistych rozmów.

- [x] Ustalić jeden wersjonowany schemat `NormalizedInterviewData`, wspólny dla głosu i tekstu.
- [ ] Skonfigurować i zapisać reguły **Analysis → Data collection** w rzeczywistej, używanej wersji agenta. Sprawdzić także polski język i pierwszą wiadomość tej wersji.
- [ ] Zaprojektować transport tablic objawów i leków. Dokumentacja ElevenLabs wymienia typy String, Boolean, Integer i Number; nie zakładać, że pole Data collection zwróci natywną tablicę lub obiekt. Proponowany pierwszy wariant: pole String `interview_json` z instrukcją zwracania JSON zgodnego z naszym schematem. To projekt integracji, który trzeba potwierdzić testem; backend musi odrzucać niepoprawny JSON. [Data collection](https://elevenlabs.io/docs/eleven-agents/customization/agent-analysis/data-collection).
- [x] Odczytywać wartości z formatu dostawcy i mapować je do DTO; zachować surowe dane w sesji do diagnostyki. Nie przekazywać obiektu dostawcy bez walidacji bezpośrednio do formularza pacjenta.
- [x] Rozróżniać brak ekstrakcji, błędny format, częściowe dane i poprawny wynik. Dodać osobny `extractionStatus`, np. `pending`, `ready`, `partial`, `failed`, oraz listę braków; `completed` rozmowy nie oznacza kompletnego raportu.
- [x] Walidować typy, długości, nasilenie 0–10, daty i schemat. Relatywne określenia czasu zachować w chronologii; nie dopisywać dokładnej daty, której pacjent nie podał.
- [x] Rozróżniać „nie wiem”, „nie zapytano”, sprzeczność i wyraźne zaprzeczenie, np. brak alergii. Pusta tablica sama nie może oznaczać potwierdzonego braku chorób lub leków.
- [x] Udostępnić w `/result` typowany obiekt `structuredData` i `schemaVersion`. Przewidzieć kompatybilne rozszerzenie lub nową wersję kontraktu, zanim zostanie usunięty istniejący `structuredDataJson`.
- [ ] Potwierdzić ekstrakcję po osobnym teście głosowym i tekstowym. Źródłem wyniku pozostaje podpisany webhook po rozmowie, a nie wiadomość JSON wysłana przez model w czacie. [Post-call webhooks](https://elevenlabs.io/docs/eleven-agents/workflows/post-call-webhooks).

Minimalne pola modelu docelowego:

| Obszar               | Dane                                                                                                                             |
| -------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| Powód konsultacji    | `consultationReason`.                                                                                                            |
| Objawy               | Nazwa, początek i jego stan, częstotliwość, nasilenie, przebieg, wpływ na codzienność, opis i chronologia.                       |
| Leki                 | Nazwa, dawka i jej stan, schemat oraz `reason` — powód przyjmowania. Pole jest już w modelu i raporcie.                          |
| Pozostały wywiad     | Alergie i reakcje, choroby przewlekłe, pytania pacjenta, `additionalNotes`. Dodatkowy opis jest w drafcie i snapshotach raportu. |
| Jakość i pochodzenie | Stany pól, jawne zaprzeczenia, braki/sprzeczności, wersja schematu i powiązanie z sesją źródłową.                                |

**Odbiór:** przykładowa rozmowa o dwóch objawach i dwóch lekach daje poprawne tablice w API, także po czacie; brak dawki pozostaje jawnym brakiem, a błędna ekstrakcja nie znika pod statusem sukcesu.

### BE-03. Przeniesienie wyniku do edytowalnego raportu

- [x] Dodać serwis mapujący znormalizowany wynik do istniejącego `InterviewDraft`: objawy, chronologia, leki, alergie, choroby, pytania i dodatkowy opis.
- [x] Po mapowaniu uruchamiać istniejące reguły braków i propozycji obserwacji, następnie oznaczać wizytę jako oczekującą na sprawdzenie. Nie tworzyć zgody ani zatwierdzonej wersji automatycznie.
- [x] Zapisać powiązanie zastosowanej ekstrakcji z sesją i rewizją draftu. Ponowienie webhooka nie może powielać danych ani nadpisywać późniejszych poprawek pacjenta.
- [x] Przy zmianie głos ↔ tekst scalać znormalizowane wyniki sesji po stronie serwera. Historia przekazana SDK jako kontekst nie zastępuje tego importu; jakość scalania wymaga dodatkowo rzeczywistego testu dostawcy.
- [ ] Scalenie opierać na kolejności wypowiedzi i jawnych korektach pacjenta. Sprzeczne informacje pozostawić do wyjaśnienia; nie wybierać samodzielnie dawki leku.
- [x] Obsłużyć spóźnione webhooki starszych sesji bez cofania aktualnego raportu. Wynik importu i draft muszą mieć czytelny stan dostępny dla frontendu.

**Odbiór:** wynik z ElevenLabs jest widoczny przez API draftu i da się go poprawić bez ponownej rozmowy; webhook po ręcznej poprawce nie kasuje tej poprawki.

### BE-04. Niezawodność i cykl życia sesji

- [x] Przed pobraniem credentiala od ElevenLabs sprawdzać możliwość rozpoczęcia wywiadu. Sprawdzenie jest wykonywane przed wydaniem credentiala.
- [ ] Rozdzielić błędy rozmowy, analizy i importu danych. Zdefiniować timeout oraz możliwość ponowienia przetwarzania wyniku bez rozpoczynania kolejnej sesji i zużywania limitu zaproszenia.
- [ ] Utrwalić przyjęcie webhooka i zadanie przetwarzania, obsłużyć równoległe duplikaty, ponowienia oraz chwilowy brak powiązania `conversation_id`. Istnieją już HMAC, kontrola agenta i deduplikacja; rozszerzyć je o trwałą obsługę błędów.
- [x] Dodać kontrolowane odzyskanie wyniku z ElevenLabs dla znanej, autoryzowanej sesji, gdy webhook nie dotrze. Utrzymywać zgodność ID agenta i rozmowy.
- [x] Rozdzielić prawo rozpoczęcia rozmowy od prawa odczytu i korekty raportu. Ukończone zaproszenie pozwala wrócić do raportu; anonimowy JWT jest akceptowany przez endpointy draftu i zgód.
- [x] Zaprojektować ograniczony dostęp do sprawdzania raportu dla użytkownika z linku: np. osobna sesja/zakres `review` dla tej samej wizyty, z kontrolą cofnięcia i wygaśnięcia. Zakończenie rozmowy nie może odbierać możliwości zatwierdzenia raportu; nie może też odblokować kolejnego startu SDK.
- [ ] Ujednolicić anulowanie wizyty, unieważnianie obu rodzajów zaproszeń i sesji oraz zachowanie opóźnionych webhooków po anulowaniu/usunięciu.

**Odbiór:** duplikat, timeout, restart API i spóźniony webhook nie powodują utraty wyniku, podwójnego importu ani dostępu do innej wizyty.

### BE-05. Zatwierdzenie, zgoda i raport lekarza

- [x] Wykorzystać istniejące `PUT /api/v1/interview/draft`, `POST /complete`, `POST /approve`, `PUT /consent` i odczyty JSON/PDF zamiast budować drugi niezależny proces raportu.
- [x] Po rozwiązaniu dostępu z BE-04 udostępnić te operacje pacjentowi z rzeczywistego linku rozmowy; walidować powiązanie wywiadu z wizytą po stronie serwera.
- [x] Dodać `reason` leku i `additionalNotes` do modelu, mapowania, snapshotu JSON i PDF. Uwzględnić wersję schematu oraz odczyt starych raportów.
- [x] Zachować rozdzielenie: sprawdzenie treści → zatwierdzenie niezmiennej wersji → osobna zgoda. Decyzje dotyczące obserwacji i braków muszą pozostać jawne.
- [ ] Uzupełnić PDF o chronologię objawów. Obecny renderer jej nie wyświetla, mimo obecności w snapshotach JSON. Przetłumaczyć generowane opisy braków i obserwacji, które częściowo powstają po angielsku.
- [ ] Zweryfikować wymóg jednostronicowego raportu dla długiego wywiadu. Aktualny szablon A4 nie gwarantuje jednej strony; ustalić regułę skrótu i obsługę przepełnienia bez cichego obcinania danych.
- [ ] Testować zgodność JSON/PDF, ponowienie generowania PDF oraz to, że lekarz widzi wyłącznie zatwierdzoną wersję z aktywną zgodą i właściwym przypisaniem.

**Odbiór:** treść po korekcie pacjenta trafia do tej samej wersji JSON i PDF; bez zgody lekarz nie otrzymuje raportu, a cofnięcie zgody natychmiast blokuje następne odczyty.

### BE-06. Placówka, powiadomienia i integracja

- [ ] Podłączyć rzeczywistego dostawcę SMS/e-mail zamiast `DemoNotificationSender`, z właściwym URL frontendu i kodem dostępu.
- [ ] Utrwalać wysyłkę wraz z utworzeniem wizyty, np. przez outbox, aby powiadomienie nie wyprzedzało skutecznego zapisu. Obsługiwać ponowienia, błędy i potwierdzenia dostarczenia.
- [ ] Wysyłać link umożliwiający także późniejsze sprawdzenie raportu i odpowiedź na pytania lekarza. Aktualny adapter uzupełnień nie przekazuje pacjentowi takiego linku.
- [ ] Dodać zarządzanie lekarzami/przypisaniami i użytkownikami placówki poza kontami seedowanymi w Development. Ustalić sposób onboardingu i zarządzania kluczami systemów placówek.
- [ ] Udostępnić panelowi listę statusów i terminy; osobno wskazać zakończenie rozmowy, przygotowanie danych, zatwierdzenie raportu i udostępnienie. Nie udostępniać administracji transkrypcji ani podsumowania medycznego.

### BE-07. Uzupełnienia, historia i tryb formularzowy

- [ ] Połączyć istniejące rundy pytań lekarza z rzeczywistym UI i nowym, ograniczonym dostępem pacjenta. Nowe pytania dopisywać do otwartej rundy.
- [ ] Zaprojektować rozmowę uzupełniającą po zakończeniu pierwszej: nowa runda/generacja rozmowy, bez nadpisania zatwierdzonego raportu. Samo wygenerowanie nowego zaproszenia do zakończonego `AgentInterview` obecnie nie wystarcza.
- [ ] Rozszerzyć porównanie historii o chronologię, częstość i wpływ na codzienność. Obecne reguły rozpoznają głównie identyczną nazwę objawu i zmianę nasilenia; to nie pełna analiza trendów opisana w procesach.
- [ ] Zachować decyzje pacjenta i wewnętrzne źródła obserwacji oraz ograniczenia odczytu źródeł przez lekarza. Brak wzmianki o objawie nie oznacza ustąpienia.
- [ ] Zapewnić działający formularz awaryjny. `SubmitAnswer` zapisuje odpowiedzi i wybiera kolejne pytanie z listy, ale nie wypełnia z nich struktury draftu. Formularz powinien zapisywać typowane pola albo korzystać z tego samego sprawdzonego importu.
- [ ] Jeśli zachowujemy osobne nagrywanie formularzowych odpowiedzi głosowych, skonfigurować `Transcription:Endpoint` i obsłużyć jego błędy. Ścieżka SDK ElevenLabs nie wymaga tego dodatkowego adaptera.

### BE-08. Usuwanie, ślad operacji i eksploatacja

- [ ] Dokończyć trwałe zadania usuwania i ich ponowienia. Obecna operacja wykonuje usunięcie synchronicznie; brak osobnego procesu ponawiania błędów.
- [ ] Od momentu zweryfikowanego żądania blokować ponowne otwarcie przez link/kod, unieważnić sesje i zaproszenia. Sama revokacja istniejących sesji Redis nie wystarczy przy nieudanym usunięciu bazy.
- [ ] Określić czyszczenie metadanych webhooków, logów/audytu oraz danych przechowywanych u dostawcy. Rekordy webhooków nie mają dziś powiązania FK z wizytą; lokalne usunięcie pacjenta nie potwierdza usunięcia rozmowy w ElevenLabs.
- [ ] Ustalić retencję transkrypcji i nagrań dostawcy zgodnie z założeniami projektu. Zachować zatwierdzone wersje do zweryfikowanego usunięcia, jak przewiduje specyfikacja.
- [ ] Uzupełnić audyt odczytów/zmian i metryki opóźnienia webhooka, błędów ekstrakcji, importu, wysyłek i PDF, bez logowania tokenów i treści wywiadu.
- [ ] Uporządkować konfigurację środowiska docelowego: trwały HTTPS callback, migracje, backup i odtwarzanie, sekret konfiguracji, limity oraz równoległe unieważnianie sesji Redis.

### BE-09. Rzeczywiste konto pacjenta

- [x] Model konta pacjenta i sesji, odrębny od `StaffUser`; logowanie, rotowany refresh i wylogowanie.
- [ ] Doprecyzować proces potwierdzenia konta; obecna rejestracja potwierdza własność wizyty przez zweryfikowaną sesję, nie własność adresu e-mail.
- [x] Wiązać wizytę z kontem po zweryfikowanym dostępie. Sam wpisany PESEL, e-mail lub UUID wizyty nie potwierdza jej własności.
- [x] Dodać API własnego profilu, avatara i listy wizyt oraz zakresy dostępu do edycji raportu. Ustalić zakres historii widocznej dla pacjenta, ponieważ procesy MVP nie przewidują przeglądu pełnej historii źródłowej.
- [x] Umożliwić dopisanie dodatkowego opisu po rozmowie i ponowne zatwierdzenie raportu przed końcem obsługi wizyty. Poprzednia udostępniona wersja pozostaje dostępna lekarzowi do publikacji nowej.

## 4. Frontend — zadania

Postęp: FE-01–FE-05 oraz pacjentowy zakres FE-07 są podłączone do aktualnego API. Szczegóły i ograniczenia: [README frontendu](../Frontend/README.md#raport-i-konto--integracja-z-aktualnym-backendem). FE-06 pozostaje osobnym panelem personelu.

### FE-01. Dźwięk łączenia/dzwonienia

- [x] Dodać krótki, łagodny sygnał w trybie głosowym, gdy `phase === 'connecting'`. Uruchamiać audio po świadomym kliknięciu rozpoczęcia rozmowy, z uwzględnieniem blokady autoplay.
- [x] Zatrzymywać sygnał przy połączeniu, pierwszej wypowiedzi modelu, błędzie, anulowaniu, zmianie trybu i opuszczeniu ekranu. Sprzątać timery i zasoby audio.
- [x] Powiązać go z istniejącym przełącznikiem dźwięku. Nie odtwarzać sygnału w trybie tekstowym ani podczas przygotowywania raportu; nie zapętlać kilku kopii przy ponownym kliknięciu.

Miejsca zmian: [useAgentConversation.ts](../Frontend/src/hooks/useAgentConversation.ts), [LiveConversation.tsx](../Frontend/src/components/LiveConversation.tsx). **Odbiór:** sygnał słychać wyłącznie podczas łączenia i zawsze ustaje po zmianie stanu, także na telefonie.

### FE-02. Usunięcie oznaczeń emocji z tekstu modelu

- [x] Dodać wspólną funkcję przygotowania tekstu do wyświetlenia, usuwającą rozpoznane tagi wykonania/emocji, np. `[laughs]`, `[sighs]`, `[whispers]` i ich używane odpowiedniki.
- [x] Stosować ją dla wiadomości asystenta, tekstu bieżącego pytania i ewentualnych takich tagów w podsumowaniu. `onMessage` nadal zachowuje surowe `event.message`; czyszczenie odbywa się przy renderowaniu.
- [x] Pozostawić oryginalną transkrypcję w backendzie; zmiana dotyczy prezentacji. Nie czyścić wypowiedzi pacjenta i nie usuwać wszystkich nawiasów wyrażeniem regularnym — zapis `[500 mg]` lub istotny dopisek musi pozostać.
- [x] Obsłużyć wiele tagów, zbędne spacje i aktualizacje tej samej wiadomości. Niepełny końcowy tag jest ukrywany po rozpoznaniu co najmniej trzech znaków; nieznane nawiasy pozostają widoczne.

**Odbiór:** `[sighs] Rozumiem. [whispers] Od kiedy boli?` pokazuje się jako `Rozumiem. Od kiedy boli?`; zwykłe nawiasy i odpowiedzi pacjenta zachowują treść.

### FE-03. Rzeczywista karta wizyty

- [x] Rozszerzyć `AgentInterviewInfo` i adapter API o opcjonalne `VisitDetails` z BE-01, zachowując zgodność z dotychczasowym `visitDate`.
- [x] Wyodrębnić wielokrotnie używalną kartę z istniejących komponentów wizyty i użyć jej nad rozmową także w `StandaloneConversation`, bez dodawania sidebaru.
- [x] Pokazywać datę, godzinę, lekarza, specjalizację, placówkę, adres, gabinet i formę wizyty. Formatować termin w uzgodnionej strefie, z poprawną obsługą zmiany czasu.
- [x] Zachować stan ładowania i dodać obsługę brakujących pól. Nie podstawiać fikcyjnego lekarza lub placówki, gdy backend ich nie zwrócił.
- [x] Zachować czytelność na telefonie i możliwość zwinięcia szczegółów podczas rozmowy.
- [x] Zweryfikować pełne metadane z rzeczywistą fikcyjną wizytą utworzoną na lokalnym API.

Miejsca zmian: [StandaloneConversation.tsx](../Frontend/src/components/StandaloneConversation.tsx), [VisitContext.tsx](../Frontend/src/components/VisitContext.tsx), [agent-api.ts](../Frontend/src/lib/agent-api.ts), [models.ts](../Frontend/src/models.ts). **Odbiór:** karta pokazuje dane wizyty utworzonej przez API, a nie `data/mock.ts`.

### FE-04. Wynik i dane strukturalne

- [x] Użyć typowanego DTO draftu `PatientInterviewView`, zachowując enumy C# w modelu transportowym; surowy legacy JSON wyniku pozostaje opcjonalnym polem diagnostycznym. Obsługiwać wersję schematu oraz brak lub niepoprawne dane.
- [x] Wyświetlać osobne sekcje powodu wizyty, objawów i chronologii, leków wraz z powodem przyjmowania, alergii, chorób, pytań i dodatkowych uwag.
- [x] Pokazywać braki i sprzeczności do sprawdzenia; nie przedstawiać pustego `{}` jako kompletnego raportu.
- [x] Oddzielić zakończenie rozmowy od analizy i przygotowania draftu. Dodać stan dłuższego oczekiwania, ponowienie odczytu i komunikat błędu przetwarzania.
- [x] Umożliwić bezpieczny powrót do wyniku po odświeżeniu lub ponownym otwarciu linku, w oparciu o dostęp z BE-04.

### FE-05. Poprawki, zatwierdzenie i zgoda

Frontend, 4.10.2026: `ReportReview` korzysta z serwerowego draftu, wersji i zgód. Dopowiedzenia uzupełniają draft; ukończony wywiad nie jest ponownie uruchamiany jako sesja ElevenLabs.

- [x] Akcje na końcu rozmowy z blokadą podczas przetwarzania oraz powrotem z formularza dopowiedzenia.
- [x] Rzeczywisty draft, zapis poprawek i dodatkowego opisu do API.
- [x] Decyzje o obserwacjach, potwierdzenie braków, zatwierdzenie wersji i osobna zgoda.
- [x] Pobranie JSON/PDF i cofnięcie zgody ze stanem z backendu.
- [x] Konflikt rewizji bez utraty poprawek oraz ponowienie generowania PDF.
- [ ] Skonfigurować backendową usługę transkrypcji i wykonać rzeczywiste nagranie dopowiedzenia. Test UI/kontraktu jest gotowy; brak `Transcription__Endpoint` daje jawny komunikat i alternatywę tekstową.

### FE-06. Panel administracji i lekarza

- [ ] Podłączyć logowanie personelu, odnowienie JWT i wylogowanie. Konta personelu nie są kontami pacjentów.
- [ ] Dodać formularz tworzenia wizyty, listę statusów, ponowienie zaproszenia, zmianę danych i anulowanie.
- [ ] Dla lekarza pokazywać przypisane raporty, braki, PDF oraz formularz pytań uzupełniających. Zachować rozdzielenie widoków zgodnie z rolą.
- [ ] Używać JWT w przeglądarce. Klucze integracyjne i klucz ElevenLabs pozostają po stronie serwera.

### FE-07. Konto pacjenta i uzupełnienia

- [x] Udostępnić rzeczywiste konto, avatar i własne wizyty przez API; `mockPatientService` pozostaje wyłącznie w osobnej makiecie `/demo`.
- [x] Po zalogowaniu udostępniać wyłącznie wizyty powiązane z kontem i akcje dozwolone dla ich aktualnego stanu.
- [x] Dodać widok pytań lekarza, odpowiedzi i sprawdzenia nowej wersji raportu. Dostęp gościa nadal dotyczy jednej wizyty.
- [x] Podłączyć edycję profilu i dodatkowych uwag; czyścić lokalny stan po wylogowaniu.

## 5. Kontrakty API do wykorzystania i rozszerzenia

| Operacja                     | Istniejący endpoint                                                                               | Plan                                                                   |
| ---------------------------- | ------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| Logowanie personelu          | `POST /api/v1/auth/login`, `/refresh`, `/logout`, `GET /me`                                       | Podłączyć do panelu; zachować osobną tożsamość pacjenta.               |
| Utworzenie wizyty            | `POST /api/v1/integration/visits`                                                                 | Dodać dane lekarza i miejsca; termin już jest obsługiwany.             |
| Lista/status wizyt           | `GET /api/v1/integration/visits`, `/{id}/status`                                                  | Rozszerzyć metadane, filtrowanie i stany przygotowania.                |
| Zaproszenia i anulowanie     | `POST /api/v1/integration/visits/{id}/invitations`, `/{id}/cancel`                                | Połączyć z faktyczną wysyłką i pełnym unieważnianiem dostępu.          |
| Metadane rozmowy             | `GET /api/public/interviews/{token}`, `GET /api/interviews/{id}`                                  | Rozszerzyć o kartę wizyty; oddzielić start od późniejszego przeglądu.  |
| Wynik rozmowy                | `GET /api/interviews/{id}/result`                                                                 | Typowany wynik, stan ekstrakcji i importu, zachowanie kompatybilności. |
| Callback                     | `POST /api/webhooks/elevenlabs`                                                                   | Normalizacja i import po głosie/czacie, trwałe ponowienia.             |
| Draft, zatwierdzenie i zgoda | `GET /api/v1/interview`, `PUT /draft`, `POST /approve`, `PUT /consent` w tej grupie               | Podłączyć wynik ElevenLabs i dostęp pacjenta z linku.                  |
| Raport lekarza               | `GET /api/v1/integration/visits/{id}/report-versions` i odczyty wersji JSON/PDF                   | Istnieje; połączyć z ukończonym procesem zatwierdzania.                |
| Pytania uzupełniające        | `POST /api/v1/integration/visits/{id}/supplementation-round/questions`, odpowiedzi w API pacjenta | Podłączyć UI, dostęp i rozmowę uzupełniającą.                          |

Nowe operacje wymagające zaprojektowania: aktualizacja szczegółów wizyty, zarządzanie lekarzami i personelem, odzyskanie/ponowienie przetwarzania wyniku, dostęp do przeglądu raportu z linku oraz osobne API konta pacjenta. Nazwy tras ustalić podczas implementacji; powyższa tabela nie deklaruje ich istnienia.

## 6. Odbiór i testy

- [ ] Integracja API: utworzenie wizyty z pełnymi metadanymi → dostęp z linku → zakończenie głosu/czatu → webhook → typowany wynik → draft → poprawka → zatwierdzenie → zgoda → odczyt lekarza.
- [ ] Ekstrakcja: wiele objawów i leków, powód przyjmowania leku, nieznana dawka, brak alergii zgłoszony wprost, sprzeczne dane, pusta/błędna ekstrakcja oraz polskie znaki.
- [ ] Sesje: zmiana głos/tekst, rozłączenie, duplikat webhooka, odwrotna kolejność callbacków, równoległe importy i poprawka pacjenta przed spóźnionym wynikiem.
- [ ] Dostęp: inna wizyta/placówka/lekarz, brak zgody, cofnięcie zgody, zastąpienie linku, anulowanie, wygaśnięcie i usunięcie. Zamknięta rozmowa nadal pozwala sprawdzić raport w dozwolonym zakresie.
- [ ] Frontend: sygnał łączenia z pełnym sprzątaniem, tagi modelu bez utraty treści pacjenta, karta prawdziwej wizyty, stany oczekiwania i korekta danych na komputerze oraz telefonie.
- [ ] Raporty i uzupełnienia: wspólne ID wersji JSON/PDF, chronologia i uwagi w obu formatach, długie dane, błąd PDF, druga wersja po pytaniach lekarza i zachowanie pierwszej wersji.
- [ ] Powiadomienia i usuwanie: błąd dostawcy, ponowienie bez duplikatu, rzeczywisty link powrotu, brak ponownego dostępu po rozpoczęciu usuwania oraz skuteczne ponowienie błędu.
- [ ] Zachować testy jednostkowe i integracyjne backendu, frontendowe testy kontraktów oraz E2E. Po wdrożeniu wykonać rzeczywistą rozmowę głosową i tekstową; atrapy SDK nie potwierdzają działania mikrofonu ani konfiguracji agenta.
- [ ] Zaktualizować [tworzenie rozmowy przez HTTP](../Backend/requests/elevenlabs-new-conversation.http) i [diagnostykę wyniku](../Backend/requests/elevenlabs-test.http), aby potwierdzały także dane karty, znormalizowaną ekstrakcję, draft i raport dla lekarza.

## 7. Kod objęty przeglądem

- Backend: [endpointy](../Backend/src/DocPrep.Api/Endpoints.cs), [kontrakty](../Backend/src/DocPrep.Application/Contracts/Contracts.cs), [wizyty](../Backend/src/DocPrep.Application/Visits/IntegrationService.cs), [webhook i sesje](../Backend/src/DocPrep.Application/Interviews/ElevenLabsInterviewService.cs), [draft i zatwierdzanie](../Backend/src/DocPrep.Application/Interviews/PatientInterviewService.cs), [dostęp lekarza](../Backend/src/DocPrep.Application/Reports/FacilityReportService.cs).
- Infrastruktura: [adaptery wysyłki/transkrypcji/PDF](../Backend/src/DocPrep.Infrastructure/Services/ApplicationAdapters.cs), [mapowanie bazy](../Backend/src/DocPrep.Infrastructure/Persistence/DocPrepDbContext.cs), [magazyn danych](../Backend/src/DocPrep.Infrastructure/Persistence/DocPrepStore.cs), [sesje i ochrona identyfikacji](../Backend/src/DocPrep.Infrastructure/Services/SecurityServices.cs).
- Frontend: [adapter API](../Frontend/src/lib/agent-api.ts), [cykl życia rozmowy](../Frontend/src/hooks/useAgentConversation.ts), [rzeczywista rozmowa](../Frontend/src/components/LiveConversation.tsx), [widok z linku](../Frontend/src/components/StandaloneConversation.tsx), [panel demo](../Frontend/src/App.tsx), [modele](../Frontend/src/models.ts).
