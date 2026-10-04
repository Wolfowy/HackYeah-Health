# Wspólny przepływ MVP — koordynacja

**Aktualizacja przepływu: 4 października 2026 — odbiór zakończony.** Cel użytkownika: utworzenie wizyty w panelu → link → rozmowa pacjenta → poprawienie danych → jedno zatwierdzenie całego raportu wraz z udostępnieniem → odczyt przez przypisanego lekarza. Zgodnie z nowym wymaganiem nie ma osobnych akceptacji obserwacji, dodatkowej akceptacji udostępnienia ani pobierania JSON w interfejsie pacjenta. Wszystkie operacje aplikacyjne przechodzą przez API i PostgreSQL; sesje wizyt przez Redis.

## Podział pracy

| Chat                                   | Wyłączny zakres edycji                                           | Zadanie                                                                                                                     |
| -------------------------------------- | ---------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| Rozbuduj backend i rozmowy             | `Backend/`                                                       | Domknąć backend przepływu, izolację wizyt lekarza i metadane jego kolejki; sprawdzić import, zatwierdzenie, zgodę i raport. |
| Dodaj frontend Przed wizytą            | `Frontend/`                                                      | Przegląd i edycja poszczególnych danych, jedno zatwierdzenie z udostępnieniem, PDF i obsługa błędów.                        |
| Dodaj panel recepcji                   | `AdminFrontend/`                                                 | Domknąć tworzenie wizyty, otwieranie/kopiowanie linku oraz kolejkę i raport lekarza przez API.                              |
| Koordynator — Podsumuj pozostałe prace | `docs/`, pliki uruchomienia/testu poza katalogami trzech agentów | Uruchomienie systemu, konfiguracja wspólna, test odbioru i aktualizacja tego dokumentu.                                     |

Wspólny checkout zawiera niezacommitowane zmiany. Każdy agent zachowuje te zmiany, nie zmienia gałęzi, nie wykonuje reset/stash ani wspólnego commit/merge. Nie przebudowuje i nie restartuje wspólnego API samodzielnie. Koordynator robi to po zakończeniu zmian backendu. Raport końcowy agenta powinien zawierać pliki, testy i pozostałe ograniczenia; koordynator odbiera go przez narzędzia statusu chatów.

## Uzgodniony kontrakt minimalny

1. Recepcja loguje się przez `/api/v1/auth/login`, tworzy wizytę przez `POST /api/v1/admin/appointments` z `sendInvitation: false`. Dostaje `appointment` i `invitation.url` dla rzeczywistego frontendu pacjenta. Do MVP wystarczy skopiowanie linku; operator SMS/e-mail nie blokuje tego scenariusza.
2. Pacjent otwiera `/i/{token}`; frontend wymienia zaproszenie na ograniczony JWT, usuwa token z URL i korzysta z obecnych endpointów rozmowy oraz `/api/v1/interview`.
3. Wynik pochodzi z podpisanego webhooka lub autoryzowanego recovery dostawcy. Import tworzy draft. Pacjent poprawia dane i używa jednego przycisku **Zatwierdź i udostępnij raport**. `POST /api/v1/interview/approve` otrzymuje `acceptAllObservations: true`, `shareWithFacility: true` i `confirmIncompleteReport` zgodnie z widocznymi brakami. Backend akceptuje oczekujące obserwacje, zapisuje wersję oraz udostępnia po gotowym PDF. Poprzednie celowe odrzucenia/edycje obserwacji pozostają respektowane. Stare żądania bez flag zachowują kompatybilność; osobny endpoint zgody nadal pozwala cofnąć udostępnienie. Nie wolno oznaczać raportu gotowym tylko na podstawie transkrypcji w przeglądarce.
4. Lekarz korzysta z istniejących `GET /api/v1/integration/visits` i `/{id}/status`. Backend ogranicza rolę `Clinician` do wizyt przypisanych do `clinicianId` z poświadczenia. Administracja/system zachowują swój dotychczasowy zakres.
5. Backend rozszerza `AdminVisitView` o `patientName: string | null`, `durationMinutes: number` i `endsAt: string` (ISO 8601), zachowując dotychczasowe pola i paginację `{items,page,pageSize,total}`. Nazwa pochodzi z szyfrowanych danych recepcji; bez danych jest `null`. Kontakt pacjenta nie jest potrzebny w kontrakcie lekarza. AdminFrontend mapuje te pola bez fikcyjnego nazwiska lub czasu.
6. Odczyt raportu/PDF pozostaje na obecnych endpointach `report-versions`; wymaga właściwego przypisania i aktywnej zgody. Recepcja odczytuje status, lekarz zatwierdzoną wersję.

Zmianę kontraktu konieczną do działania należy najpierw zapisać w raporcie/statusie. Koordynator przekazuje ją pozostałym chatom. Nie rozszerzać prac na wszystkie zadania z audytu — pierwszy odbiór dotyczy tej jednej ścieżki.

## Odbiór

- [x] Backend i oba fronty przechodzą build oraz odpowiednie testy.
- [x] Utworzenie wizyty przez rzeczywisty panel zapisuje ją w PostgreSQL i zwraca działający link.
- [x] Link pokazuje dane tej wizyty i pozwala rozpocząć rozmowę.
- [x] Wynik rozmowy trafia do draftu przez backend, bez automatycznej zgody.
- [x] Jedno zatwierdzenie akceptuje cały raport z obserwacjami i udostępnia go lekarzowi bez dodatkowego kroku.
- [x] Pacjent edytuje wybrane wpisy i pola, np. nazwę/dawkę leku, alergen i reakcję; w interfejsie nie ma pobierania JSON.
- [x] Lekarz widzi nazwę pacjenta, właściwą wizytę i tę samą wersję raportu/PDF.
- [x] Testy HTTP potwierdzają odmowę dla innego lekarza i recepcji oraz blokadę po cofnięciu zgody.
- [x] Testy HTTP: osobny PostgreSQL i kontrolowany dostawca. Odbiór obu UI: działające lokalne API, PostgreSQL `docprep` i rzeczywisty ElevenLabs.
- [x] Wspólny lokalny system pozostaje uruchomiony z aktualnym backendem i adresami obu frontów.

## Bieżący status

Trzy istniejące chaty zakończyły pierwszy zakres zmian. Dalsze zmiany zatwierdzania i edycji koordynator przejął po zatrzymaniu chatów na limicie i zakończył ich odbiór. Panel przeszedł build, 15 testów logiki i 22 scenariusze E2E; frontend pacjenta build, 27 testów jednostkowych i 46 scenariuszy E2E (42 przeszły od razu, cztery po poprawce etykiety przycisku). Backend przeszedł build i 31 testów bez pominięć, na osobnej bazie `docprep_mvp_test_20261004`, z kontrolowanym dostawcą rozmowy.

Odbiór rzeczywisty na lokalnym `docprep` potwierdził utworzenie przez panel, sesję tekstową ElevenLabs, podpisany webhook, import, zatwierdzenie (w pierwszym przebiegu jeszcze osobną zgodę) oraz właściwy raport w panelu przypisanego lekarza. Wykrytą pustą stronę PDF naprawiono aktualizacją QuestPDF `2024.3.0` → `2024.3.10`. PDF zapisanej wersji został ponownie wygenerowany przez API i pobrany przyciskiem panelu lekarza: **42 143 B, jedna strona A4, prawidłowa treść i polskie znaki, kontrola tekstu oraz renderu PNG zakończona powodzeniem**. Test zawartości PDF działa również podczas budowy obrazu Docker; sam nagłówek `%PDF` nie wystarcza.

Dowód w bazie: wizyta `86e885d7-e1ff-4565-83b5-181fa295d247`, wywiad `3ac0b51b-62b0-47e0-9641-65775686b64a`, wersja 1 `e6ae844f-c0a8-48a4-b118-0515a3879e26`. Fikcyjny pacjent: **Pacjent MVP Test 004916**, 4 października 2026, 12:00–12:30. Zapisano dwa objawy, dwa leki, ukończoną sesję `elevenlabs`, przetworzony webhook, import `Ready`, aktywną zgodę i status wizyty `Shared`. JSON zatwierdzonej wersji nie był zmieniany przy naprawie PDF.

Pierwsze cztery kroki i zrzuty: `/private/tmp/docprep-mvp-live-IyS7yy/`. Końcowy odczyt lekarza, pobrany PDF i wynik sukcesu: `/private/tmp/docprep-mvp-live-QDvHLx/result.json`. Token zaproszenia pozostaje w ignorowanym `Backend/.local/mvp-live-link.txt`, bez publikowania go w dokumentacji. Zmiany pozostają niezacommitowane; istniejące zmiany pozostałych chatów zachowano.

Drugi odbiór: pacjent przez UI cofnął udostępnienie, poprawił nazwę i dawkę leku oraz dodał alergen i reakcję. Jedno `POST /approve` zaakceptowało wszystkie oczekujące obserwacje i samo udzieliło zgody; nie było osobnych żądań `/decision` ani `/consent` podczas zatwierdzenia, przycisku JSON ani dodatkowego kroku wysyłki. Lekarz odczytał **wersję 2**, `874fdba8-143b-4c89-8b7e-ab60d94c845c`, zawierającą „Paracetamol testowy” i „Pyłki traw — test”, oraz PDF **42 481 B, jedna strona A4**. Treść i render potwierdzono; hash poprzedniego snapshotu pozostał identyczny. W bazie status `Shared`, oba wskaźniki wersji prowadzą do wersji 2, oba PDF mają status `Ready`. Dowody: `/private/tmp/docprep-mvp-live-vvh1sb/result.json`.

Wspólny [Compose](../compose.yaml) buduje i uruchamia API, PostgreSQL, Redis, frontend pacjenta oraz panel. Oba fronty są buildami produkcyjnymi z Nginx, proxy `/api` i fallbackiem SPA. Test pięciu kontenerów na osobnej bazie sprawdził logowanie przez oba proxy, wizytę i link pacjenta, zapis typowanego fikcyjnego raportu, jedno zatwierdzenie z udostępnieniem oraz PDF w rzeczywistym panelu lekarza (**41 840 B**). Dane i zgoda przetrwały `docker compose down` oraz ponowne `up` z tymi samymi wolumenami. Ten test kontenerów nie uruchamiał rozmowy u dostawcy. Dowody: `/private/tmp/docprep-docker-smoke-fMjVEs/result.json`.

## Jak przejść ścieżkę

1. Otwórz panel: **http://127.0.0.1:5174**. Zaloguj się na `admin@docprep.local`, hasło `DocPrepDemo!2026` — konto startowe tylko dla lokalnego środowiska Development.
2. Kliknij **Nowa wizyta**, podaj dane pacjenta, telefon lub e-mail, przyszły termin oraz lekarza przypisanego do konta `doctor@docprep.local` (lokalnie `doctor-demo`, „Demo Clinician”). Kliknij **Dodaj wizytę**.
3. W szczegółach skopiuj link lub kliknij **Otwórz wywiad**. Nie trzeba wysyłać SMS/e-mail. Link otwiera frontend pacjenta na **http://127.0.0.1:5173**.
4. Dla odebranego wariantu wybierz **Czat → Rozpocznij czat**. Przeprowadź wywiad i kliknij **Zakończ rozmowę**. Poczekaj na wynik przetworzony przez backend.
5. Sprawdź treść i popraw wybrane dane przyciskami edycji przy sekcji lub wpisie. Zapisz poprawki. Kliknij **Zatwierdź i udostępnij raport** — całość wraz z obserwacjami zostaje zatwierdzona i udostępniona przypisanemu lekarzowi. Widoczne braki danych obejmuje to samo zatwierdzenie.
6. W osobnej karcie panelu zaloguj się na `doctor@docprep.local`, hasło `DocPrepDemo!2026`. W **Pacjenci na dziś** otwórz raport tej wizyty. Dla innej daty użyj kalendarza lub listy wizyt. Nazwa, termin i raport pochodzą z API, nie z danych demo.

API: **http://127.0.0.1:8080**; gotowość: `/health/ready`. Wizyty, drafty, wersje, zgody i zdarzenia są zapisane w PostgreSQL; sesje dostępu wizyty używają Redis. Te lokalne adresy działają na tym komputerze. Dostęp z telefonu/innego komputera oraz docelowe domeny wymagają osobnej konfiguracji.

## Ponowne uruchomienie lokalne

Najprostszy pełny system uruchomisz jednym Compose według [głównego README](../README.md). W odbiorze kontenery używają portów **5184** (panel), **5183** (pacjent), **8180** (API), z własną bazą; dotychczasowe demo na 5174/5173/8080 zachowuje wcześniejsze dane i konfigurację webhooka. Poniżej starszy wariant developerski z istniejącą bazą.

API korzysta z istniejącego PostgreSQL na porcie `2142`, bazy `docprep` i prywatnego `Backend/.env`. Z katalogu `Backend/`:

```bash
docker compose -f compose.local.yaml --env-file .env up --build -d api
```

W dwóch terminalach, odpowiednio z `Frontend/` i `AdminFrontend/`:

```bash
npm run dev
```

Webhook ElevenLabs w tym odbiorze używał aktywnego tunelu ngrok do portu `8080`. Po zatrzymaniu/zmianie adresu tunelu trzeba zaktualizować URL webhooka u dostawcy na `/api/webhooks/elevenlabs`; nie wpisywać API key ani sekretu podpisu do dokumentów. Compose ma politykę restartu kontenerów; tunel webhooka trzeba uruchomić i skonfigurować osobno.

## Powtarzalny odbiór

`scripts/configure-mvp-agent.mjs` sprawdza pole `interview_json`; `--apply` dodaje/poprawia jego opis, zachowuje inne pola agenta i robi prywatny backup. Konfiguracja używanego agenta została uzupełniona podczas tego odbioru. Opis struktury: [elevenlabs-interview-json.md](elevenlabs-interview-json.md).

`scripts/mvp-system-test.mjs` używa obu prawdziwych UI, API i rzeczywistego ElevenLabs; nie przechwytuje endpointów. Wymaga zainstalowanego Chrome, zależności `Frontend/`, działających usług i `pdftotext` (Poppler). Tworzy jawnie fikcyjną wizytę i zużywa rzeczywistą sesję dostawcy:

```bash
node scripts/mvp-system-test.mjs --live
```

Dowody i zrzuty trafiają do prywatnego katalogu `/private/tmp/docprep-mvp-live-*`. Wznowienie samego odbioru lekarza nie tworzy kolejnej rozmowy:

```bash
node scripts/mvp-system-test.mjs --live --resume /private/tmp/docprep-mvp-live-IyS7yy/result.json
```

Po poprawce renderera można dodać `--regenerate-pdf`, aby przez API ponownie utworzyć PDF istniejącej, niezmienionej wersji JSON. Skrypt sprawdza pobrany z panelu plik: objawy, lek i identyfikator wersji w jego rzeczywistej treści. Nagłówek `%PDF` sam nie wystarcza.

Do odbioru edycji i zatwierdzenia nowej wersji dodaj `--revise-report` wraz z `--resume` wskazującym wynik fikcyjnej wizyty. Nie uruchamia to kolejnej rozmowy u dostawcy; sprawdza niezmienność poprzedniej wersji, nowe dane w UI lekarza oraz PDF.

## Pozostałe warianty poza odebranym MVP

- Rzeczywisty głos z mikrofonem, zmiana głos ↔ czat i odbiór na fizycznym telefonie.
- Runda pytań lekarza przez oba UI. Nowa wersja po korekcie leków/alergii i cofnięcie zgody zostały odebrane; pytania mają na razie testy backendu i UI z atrapami.
- Rzeczywiste dostarczenie SMS/e-mail i produkcyjne domeny. W MVP link można otworzyć bez operatora wiadomości.
- Osobne głosowe dopowiedzenie po zakończeniu rozmowy wymaga `Transcription__Endpoint`; tekstowe dopowiedzenie jest dostępne.
- Szersza niezawodność, zarządzanie personelem i pozostałe prace: [stan-projektu-i-plan-prac.md](stan-projektu-i-plan-prac.md). To odrębne zadania, nie potwierdzenia ukończenia całego produktu.
