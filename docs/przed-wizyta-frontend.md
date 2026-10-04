# Przed wizytą — opis frontendu

Aktualizacja 4.10.2026: rzeczywiste konto pacjenta i raport są podłączone do backendu. Szczegóły endpointów, transkrypcji i testów: [README frontendu](../Frontend/README.md#raport-i-konto--integracja-z-aktualnym-backendem).

## Cel aplikacji

Frontend pomaga pacjentowi zebrać informacje przed umówioną wizytą: powód konsultacji, objawy, czas ich występowania, leki i powody ich przyjmowania, alergie, choroby przewlekłe oraz pytania do lekarza. Głównym elementem jest rozmowa głosowa z asystentem AI; w dowolnej chwili można przejść do pisania.

Interfejs jest po polsku i dostosowuje się do komputera oraz telefonu. Rozmowie towarzyszy animowana sfera z przezroczystym środkiem. Nakładające się fale zmieniają kolor, krycie i kształt; podczas rzeczywistej rozmowy reagują na poziom dźwięku. Pauza zatrzymuje ruch, a preferencja ograniczenia animacji jest respektowana.

## Dwa sposoby korzystania

### Panel pacjenta

Panel rzeczywistego konta zawiera własne wizyty i profil z avatarem. Wybrana wizyta otwiera rozmowę albo gotowe podsumowanie. Każda wizyta ma własną historię odpowiedzi oraz raport. Pacjent może poprawić odpowiedzi i dodać dodatkowy opis, zatwierdzić wersję raportu, osobno udostępnić ją placówce lub cofnąć zgodę.

Strona główna udostępnia prawdziwe konto pacjenta: rejestrację ze zweryfikowanego linku, logowanie, odnowienie JWT, profil z avatarem i listę własnych wizyt. Otwarcie wizyty wydaje osobną sesję ograniczoną do niej. Tokeny pozostają w pamięci; po odświeżeniu potrzebne jest ponowne logowanie. Dotychczasowa makieta i jej pełny sidebar są zachowane pod `/demo`.

### Rozmowa z linka, bez logowania

Adres `/i/{token}` otwiera ekran zawierający wyłącznie rozmowę oraz podstawowy kontekst terminu wizyty. Nie ma bocznej nawigacji, profilu ani listy pozostałych wizyt.

Backend DocPrep sprawdza zaproszenie i wymienia jego token na anonimowy JWT ograniczony do jednego wywiadu. Odpowiedź zawiera także `interviewId`, używany w kolejnych żądaniach. Frontend usuwa token zaproszenia z adresu i przechodzi na `/rozmowa`. Sesja pozostaje w pamięci strony; po odświeżeniu należy ponownie otworzyć otrzymany link. Nieprawidłowe, wygasłe i unieważnione zaproszenia blokują dostęp. Ukończony link otwiera podsumowanie, poprawki i zatwierdzenie, bez uruchamiania nowej sesji agenta.

## Dostępne adresy

| Adres                         | Przeznaczenie                                                            |
| ----------------------------- | ------------------------------------------------------------------------ |
| `/`                           | Rzeczywiste konto pacjenta i własne wizyty.                              |
| `/demo`                       | Panel demonstracyjny; wewnętrzna nawigacja używa hash URL.               |
| `/i/demo-appointment-1`       | Rozmowa demonstracyjna dla pierwszej wizyty, bez sidebaru.               |
| `/i/demo-appointment-2`       | Rozmowa demonstracyjna dla drugiej wizyty, bez sidebaru.                 |
| `/i/{token}`                  | Rozmowa ElevenLabs po walidacji zaproszenia przez backend.               |
| `/visits/{visitId}/interview` | Rozmowa przez API pacjenta; wymaga tożsamości rozpoznanej przez backend. |
| `/rozmowa`                    | Ekran po autoryzacji zaproszenia, bez tokenu w URL.                      |

## Przebieg rzeczywistej rozmowy

1. Frontend pobiera metadane i autoryzuje dostęp do wywiadu.
2. Pacjent wybiera głos albo tekst. Mikrofon jest otwierany dopiero po kliknięciu rozpoczęcia rozmowy głosowej.
3. Backend tworzy sesję i wydaje tymczasowy credential ElevenLabs. Klucz API dostawcy pozostaje na serwerze.
4. Oficjalny SDK zestawia WebRTC dla głosu lub WebSocket z `textOnly: true` dla tekstu. Wiadomości pojawiają się w interfejsie, a poziom audio steruje sferą.
5. Frontend potwierdza identyfikator rozmowy dostawcy. Przełączenie trybu zamyka poprzedni transport i rozpoczyna kolejną sesję, przekazując historię jako kontekst.
6. Po zakończeniu połączenia UI pokazuje przetwarzanie. Backend odbiera i weryfikuje podpisany webhook, zapisuje transkrypcję, analizę i podsumowanie. Przy zmianie trybu poprzednia sesja jest wcześniej oznaczana jako kontynuacja, aby jej webhook nie zakończył całego wywiadu.
7. Frontend odpytuje wynik przez minutę; później można odświeżyć go przyciskiem. Sam koniec połączenia nie oznacza zatwierdzenia ani udostępnienia raportu placówce.

Rozmowa rozróżnia stany oczekiwania, prośby o mikrofon, łączenia, aktywnego połączenia, wyciszenia, kończenia, przetwarzania, ukończenia i błędu. Brak zgody na mikrofon pozwala kontynuować tekstowo. Opuszczenie ekranu zamyka transport SDK. Każda nowa sesja, także po zmianie trybu, zużywa jedno rozpoczęcie z limitu zaproszenia.

## Modele danych

Modele panelu znajdują się w `Frontend/src/models.ts`, a kontrakty integracji w `Frontend/src/lib/agent-api.ts`.

| Model                                     | Odpowiedzialność                                                           |
| ----------------------------------------- | -------------------------------------------------------------------------- |
| `Session`, `PatientProfile`               | Tryb gościa lub konta oraz profil demonstracyjny.                          |
| `Appointment`                             | Termin, placówka, lekarz, specjalizacja i status przygotowania wizyty.     |
| `Interview`, `Message`, `InterviewAnswer` | Stan wywiadu, historia rozmowy i zatwierdzone odpowiedzi.                  |
| `InterviewReport`, `ReportSection`        | Pola raportu, pochodzenie informacji oraz oznaczenie braków i niepewności. |
| `Symptom`, `Medication`, `Observation`    | Przygotowane struktury objawów, leków i obserwacji.                        |
| `SummaryVersion`, `SharingConsent`        | Niezmienna zatwierdzona wersja oraz osobna zgoda na udostępnienie.         |
| `AgentAccess`, `AgentCredential`          | Dostęp do jednego wywiadu i credential właściwy dla głosu lub tekstu.      |
| `AgentInterviewInfo`, `AgentResult`       | Metadane rozmowy oraz status i wynik otrzymany z backendu.                 |

Daty mają format ISO 8601. Tokeny sesji i odpowiedzi demo nie są zapisywane w `localStorage`. Backend sam ustala powiązanie rozmowy z wizytą; identyfikatory przekazane przez klienta nie zastępują autoryzacji.

## Organizacja kodu

- `src/App.tsx` — panel, stan konta i wybór widoku rozmowy z linka.
- `src/components/StandaloneConversation.tsx` — walidacja linka i ekran bez sidebaru.
- `src/components/LiveConversation.tsx` — głos, czat, błędy połączenia i wynik z serwera.
- `src/hooks/useAgentConversation.ts` — cykl życia SDK, przełączanie trybu, mikrofon, historia i odpytywanie wyniku.
- `src/components/Orb.tsx` oraz `src/styles.css` — sfera SVG, animacja i responsywny wygląd.
- `src/components/Conversation.tsx`, `Summary.tsx`, `Appointments.tsx`, `Login.tsx`, `Profile.tsx` — przepływy demonstracyjne panelu.
- `src/lib/agent-api.ts` — klient API; `src/lib/routes.ts` — rozpoznawanie adresów.
- `src/data/mock.ts`, `src/lib/interview.ts` — dane demo i reguły odpowiedzi, wersji oraz zgód.

Technologie: React, TypeScript, Vite, `@elevenlabs/client`, ikony Lucide, CSS oraz Playwright. SDK ElevenLabs jest ładowany dopiero przy rozpoczęciu rzeczywistej sesji.

## Uruchomienie i weryfikacja

Z katalogu `Frontend`, przy Node.js 22.12+:

```sh
npm ci
npm run dev
```

Frontend działa pod `http://127.0.0.1:5173`. Lokalne proxy `/api` wskazuje na backend pod `http://127.0.0.1:8080`. Opcjonalny adres API opisuje `Frontend/.env.example`; konfiguracja ElevenLabs należy wyłącznie do backendu. Ścieżka `/visits/{visitId}/interview` wymaga sesji pacjenta DocPrep przekazanej przez aplikację nadrzędną metodą `agentApi.setPatientSession(token)`. Nie używa nagłówka `X-Patient-Id`.

```sh
npm run build
npm test
npm run test:e2e
npm run format:check
```

Testy obejmują reguły raportu i zgód, kontrakt API, linki zaproszeń, widoki komputera i telefonu oraz przepływ SDK ze zmianą trybu. Testy przeglądarkowe zastępują transport ElevenLabs atrapą i nie wykonują płatnych rozmów.

## Obecne granice integracji

Rzeczywiste rozmowy wymagają skonfigurowanego agenta, kluczy serwerowych i webhooka HTTPS. Wynik zasila draft, który pacjent może edytować, uzupełniać, zatwierdzać i osobno udostępnić placówce. Konto, profil i lista wizyt korzystają z API pacjenta; JWT personelu jest osobnym mechanizmem. Głosowe dopowiedzenie do zakończonego raportu wymaga dodatkowo backendowej usługi `Transcription__Endpoint`, której obecny lokalny compose nie konfiguruje. Panel personelu i produkcyjne powiadomienia pozostają osobnym zakresem.

Szczegóły konfiguracji: [README frontendu](../Frontend/README.md), [README backendu](../Backend/README.md) i [założenia integracji ElevenLabs](elevenlabs-agent-integracja-front-back.md).
