# Panel personelu — wykonane zmiany

Data: 4 października 2026. Lokalna gałąź: `codex/reception-clinician-api`. Prace odbyły się w osobnym zarządzanym worktree, bez przełączania głównego katalogu używanego przez równoległych agentów. Zakres scalenia: `AdminFrontend/` i link w głównym `README.md`.

## Pierwszy etap: osobny frontend recepcji

- Utworzono `AdminFrontend`, niezależny od aplikacji pacjenta. React 19, TypeScript, Vite, Ant Design, ikony Ant Design i Day.js.
- Dodano ekran logowania, wylogowanie, polski interfejs, responsywny układ i lokalne dane demonstracyjne.
- Dodano kalendarz tygodnia/miesiąca, mini kalendarz, listę i filtry wizyt, szczegóły w panelu bocznym oraz statusy wywiadu i dostarczenia.
- Dodano lokalne tworzenie wizyty z kontrolą kolizji, kopiowanie linku `.example`, symulację zaproszenia i historię demo.
- Dodano odczyt fikcyjnego zatwierdzonego raportu, jego sekcje kliniczne, brak raportu i uzupełnienia.
- Dodano dokumentację uruchomienia, modeli, funkcji, granic mockupu i testów.

## Drugi etap: lekarz i aktualne API

- **Plan dnia lekarza:** domyślne otwarcie na dzisiaj, wizyty w kolejności godzin, wybór dnia, powrót do dzisiaj, status każdej wizyty i szybkie otwarcie raportu. Wizyta bez raportu nadal jest widoczna i otwiera szczegóły. Wszystkie widoki lekarza ograniczają dane do jego `clinicianId`; recepcja może wybrać lekarza w podglądzie planu.
- **Osobne demo lekarza:** fikcyjne przykłady na dzisiaj również w weekend, bez wywołań API. Dotychczasowe demo recepcji zachowano.
- **JWT personelu:** logowanie do API, rotacja refresh tokenu współdzielona przez równoczesne żądania, wylogowanie i obsługa wygasłej sesji. Tokeny nie są zapisywane w przeglądarkowym storage. Opóźnione odpowiedzi nie mogą przywrócić starej sesji po jej zmianie.
- **Jawny wybór danych:** zakładka konta placówki wywołuje backend; przyciski demo używają wyłącznie fikcyjnych danych. Błędy API nie przełączają aplikacji na demo.
- **Recepcja korzysta z nowych kontraktów:** podczas pracy do `main` scalono `codex/admin-backend-mvp`. Adapter został dostosowany do `ReceptionContracts.cs` i `ReceptionEndpoints.cs`: dane pacjenta, czas trwania, katalog lekarzy, placówka, rezerwacja bez automatycznej wysyłki oraz wersja rezerwacji.
- **Operacje rezerwacji:** lista wszystkich stron, szczegóły i status, rejestracja, zmiana terminu/czasu/gabinetu oraz anulowanie z potwierdzeniem. Edycja przesyła `expectedVersion` i zachowuje instrukcje lokalizacji. Kolizje i równoczesne zmiany rozstrzyga backend; komunikaty konfliktów są po polsku.
- **Zaproszenia:** istniejący link pobierany przez API, kopiowanie, wysyłka przez zapisany SMS/e-mail, osobna regeneracja z potwierdzeniem unieważnienia starego linku. Tryb operatora `demo` jest czytelnie oznaczony. Starsze wizyty bez odzyskiwalnego linku mogą wymagać podania kontaktu i jednorazowej regeneracji przez integrację.
- **Raporty lekarza:** odczyt zatwierdzonych wersji, wybór wersji, odświeżanie, pobranie PDF z nagłówkiem Bearer oraz pytania uzupełniające z potwierdzeniem. Recepcja nie pobiera medycznej treści w trybie API. Odmowa dostępu usuwa treść z podglądu.
- **Podsumowanie rozmowy:** nowa sekcja na górze raportu. To deterministyczny skrót zatwierdzonych danych pacjenta: powodu konsultacji, objawów, leków, pytań i uwag. Jest podpisana „Na podstawie zatwierdzonego raportu”. Kontrakt nie udostępnia personelowi osobnego podsumowania agenta, więc nie pobrano wyniku rozmowy przez token pacjenta ani nie wygenerowano nowych faktów.
- **Konfiguracja:** proxy `/api` w Vite, `DOCPREP_API_TARGET`, publiczny adres aplikacji pacjenta w `.env.example`, testy na osobnym porcie dla równoległej pracy.

## Granice na zakończenie drugiego etapu

1. Nowy kontrakt danych recepcji jest dostępny tylko roli administracji. Lekarz nadal otrzymuje starszy `AdminVisitView`, bez nazwiska, kontaktu i czasu trwania. Widok API lekarza używa `Wizyta {externalVisitId}` zamiast wymyślonych danych. Nazwiska w tej kolejce wymagają autoryzowanego rozszerzenia API lekarza. Lokalny przykład prezentuje docelowy wygląd z fikcyjnymi nazwiskami.
2. Lista integracyjna backendu udostępnia lekarzowi wizyty placówki; frontend filtruje je po identyfikatorze lekarza. Treść/PDF chroni serwer przez przypisanie i zgodę. Filtrowanie po stronie klienta nie zastępuje docelowego ograniczenia samej listy w API.
3. API nie wystawia pełnej historii zdarzeń ani osobnego podsumowania rozmowy w zatwierdzonym raporcie. PDF w lokalnym demo jest wyłączony.
4. Nie dodano osobnego modułu zarządzania kontami/katalogiem lekarzy, usuwania danych ani administracyjnego cofania zgody. Ekrany realizują przebieg pracy z wizytą, zaproszeniem i raportem. Nie zmieniano aplikacji pacjenta ani backendu w tej gałęzi.
5. Build Vite zgłasza ostrzeżenie o dużym pakiecie Ant Design. Aplikacja buduje się poprawnie; dalszy podział pakietów można wykonać przed wdrożeniem produkcyjnym.

## Weryfikacja

- Build produkcyjny TypeScript/Vite.
- Testy logiki: daty i zmiana czasu, filtrowanie, kolizje, układ kalendarza, podsumowanie raportu, mapowanie danych API, wspólna rotacja tokenu, brak fallbacku na demo, paginacja, autoryzowany PDF, rejestracja i zachowanie kontekstu/wersji przy edycji.
- Scenariusze przeglądarkowe na desktopie i telefonie: demo recepcji/lekarza, kolejka z brakiem raportu, przypisanie lekarza, wybór wersji, PDF, pytania, ograniczenie roli recepcji, odczyt/wysyłka/regeneracja linku, utworzenie/edycja/anulowanie rezerwacji, wygaśnięcie sesji i szerokość interfejsu.
- Żądania zapisujące i wysyłające w E2E są przechwytywane i obsługiwane fikcyjnymi odpowiedziami; nie wykonano testowych anulowań ani wysyłek na danych działającego API.
- Na działającym lokalnym API sprawdzono logowanie i zakończenie sesji obu ról, odczyt profilu, listę wizyt oraz nowe endpointy placówki, katalogu lekarzy i rezerwacji. Odpowiedzi mają kontrakty zgodne z adapterem.

Wynik: build produkcyjny oraz 14 testów logiki zakończone sukcesem. Wszystkie 22 scenariusze E2E (11 na desktopie i 11 na telefonie) przeszły; dwa scenariusze zaproszenia powtórzono po doprecyzowaniu selektora okna potwierdzenia. Formatowanie sprawdzono przez Prettier. Scalono lokalnie do `main`: commit funkcjonalny `57f87e8`, merge `cc76f2f`. Po scaleniu ponownie przeszły build, 14 testów logiki i sprawdzenie formatowania. Porównanie SHA-256 potwierdziło zachowanie identycznej zawartości wszystkich 34 zastanych zmienionych lub nieśledzonych plików innych agentów. W finalnym podglądzie skorygowano również zawijanie akcji kolejki w węższym panelu przeglądarki, aby nazwiska zachowywały czytelną szerokość.

## Trzeci etap: wspólny przepływ MVP

Prace w istniejącym checkout, wyłącznie w `AdminFrontend/`, zgodnie z podziałem między trzy chaty. Nie wykonywano operacji Git, zmian backendu/aplikacji pacjenta ani restartu wspólnego API. Koordynator odpowiada za wspólny runtime i odbiór całej ścieżki na bazie testowej.

- Utworzenie wizyty od razu udostępnia `invitation.url` z odpowiedzi API. Nie wymaga powtórnego odczytu linku ani wysłania SMS/e-mail. Dodano **Otwórz wywiad** w nowej karcie obok funkcji kopiowania. Odświeżenie statusu zachowuje link w pamięci bieżącej sesji.
- Kolejka i szczegóły lekarza odczytują rzeczywiste `patientName`, `durationMinutes` i `endsAt` z rozszerzonego `AdminVisitView`. Wcześniejsze ograniczenia 1–2 drugiego etapu zastępuje nowy kontrakt: serwer ogranicza wizyty przez tożsamość lekarza. Dla `patientName: null` pozostaje identyfikator wizyty; starsze API bez metadanych nie powoduje wymyślania danych.
- Wizyty bez raportu pozostają w kolejce. Podsumowanie zatwierdzonych danych, wybór wersji i chroniony PDF zachowują dotychczasowe uprawnienia. Obserwacje rozpoznają zarówno backendowe `AiObservation`, jak i demonstracyjne `ai_observation`.
- Skorygowano zachowanie linku po regeneracji starszego zaproszenia przez integrację: nowy token jest od razu mapowany do adresu aplikacji pacjenta.
- Test transportu obejmuje nowe metadane w liście i odświeżonych szczegółach. Scenariusz przeglądarkowy tworzy wizytę, kopiuje i otwiera zwrócony link, odświeża status oraz edytuje rezerwację bez żądania wysyłki. Odczyt linku jest w tym teście celowo niedostępny, co sprawdza użycie odpowiedzi z rejestracji. Scenariusz lekarza sprawdza nazwisko, czas, koniec wizyty i etykietę obserwacji AI oraz PDF.

Testy przeglądarkowe przechwytują API; otwarcie wywiadu ma kontrolowaną stronę testową. To sprawdzenie panelu, a pełną ścieżkę rozmowa → zatwierdzenie → osobna zgoda → lekarz na rzeczywistym API odbiera koordynator.

Wynik: build produkcyjny, 15 testów logiki i formatowanie przeszły. Z 22 scenariuszy E2E 20 przeszło w pełnym uruchomieniu; dwa scenariusze rejestracji powtórzono po dodaniu jednoznacznej etykiety przycisku **Odśwież status** i oba przeszły (desktop i telefon). Końcowy build oraz sprawdzenie formatowania ponownie zakończone sukcesem. Pozostaje dotychczasowe ostrzeżenie Vite o rozmiarze pakietu Ant Design.

Ścieżka ręcznego odbioru: **Konto placówki → logowanie recepcji → Nowa wizyta → wybór aktywnego lekarza i terminu → Dodaj wizytę → Kopiuj link zaproszenia / Otwórz wywiad → Odśwież status**. Po zakończeniu i udostępnieniu raportu w aplikacji pacjenta: **Wyloguj → logowanie lekarza → Pacjenci na dziś (lub wybór dnia wizyty) → Otwórz raport → Pobierz PDF**. Wysyłka wiadomości nie jest wymagana do przekazania linku.
