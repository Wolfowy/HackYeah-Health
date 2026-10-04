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

## Ważne granice

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

Wynik: build produkcyjny oraz 14 testów logiki zakończone sukcesem. Wszystkie 22 scenariusze E2E (11 na desktopie i 11 na telefonie) przeszły; dwa scenariusze zaproszenia powtórzono po doprecyzowaniu selektora okna potwierdzenia. Formatowanie sprawdzono przez Prettier. Gałąź przygotowano do lokalnego scalenia do `main`; finalny status scalenia zapisano w historii Gita.
