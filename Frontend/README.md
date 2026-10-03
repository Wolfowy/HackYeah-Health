# Przed wizytą — frontend

Samodzielny frontend React + TypeScript + Vite. Wygląd oparty na dostarczonym mockupie: jasny interfejs, animowana sfera, rozmowa głosowa i tekstowa oraz podsumowanie wywiadu. Bez połączenia z backendem.

## Uruchomienie

Wymagany Node.js 22.12+ lub aktualna wersja LTS (wymagania [Vite](https://vite.dev/guide/)).

```sh
cd Frontend
npm install
npm run dev
```

Otwórz `http://127.0.0.1:5173`.

```sh
npm run build
npm test
npm run test:e2e
```

Testy E2E używają zainstalowanego Google Chrome, obejmują desktop i telefon. Można zmienić `channel` w `playwright.config.ts`, aby użyć innej instalacji Chromium.

## Działające ścieżki demo

- **Gość:** aplikacja otwiera przykładową wizytę, pozwala rozmawiać, sprawdzić jej szczegóły, poprawić podsumowanie, zatwierdzić je i osobno udzielić lub cofnąć zgodę. Przykładowy kod wizyty: `DEMO2026`. Nieprawidłowy kod nie otwiera wywiadu.
- **Konto:** przycisk „Zaloguj się” → „Wejdź na konto demonstracyjne”. Formularz również działa z fikcyjnym e-mailem i dowolnym niepustym hasłem demo. Dodatkowo dostępne są dwie nadchodzące wizyty, profil z avatarem, edycja danych i pole dodatkowego opisu raportu. Odpowiedzi są odseparowane dla każdej wizyty.
- **Głos:** przycisk mikrofonu uruchamia symulację. Sfera zmienia ruch podczas pytania, słuchania i pauzy. Wybierz przykładową wypowiedź lub wpisz transkrypcję, popraw ją i zatwierdź. Można opcjonalnie włączyć odczytywanie pytań przez przeglądarkowe `speechSynthesis`. Demo nie otwiera mikrofonu, nie nagrywa i nie wykonuje rozpoznawania mowy.
- **Czat:** odpowiedzi własne lub przykładowe; Enter wysyła, Shift+Enter dodaje nową linię. Przełączenie z głosu do czatu zachowuje zatwierdzone odpowiedzi i przenosi niewysłaną transkrypcję do edytora.
- **Raport:** edycja i usuwanie informacji, oznaczanie braków, jawne potwierdzenie niepełnego raportu, niezmienne zatwierdzone wersje, osobna zgoda dla wizyty i placówki. Nowa wersja robocza nie zmienia wcześniej zatwierdzonego raportu widocznego placówce. Ponowne zatwierdzenie przy aktywnej zgodzie udostępnia nową wersję.
- **Eksport:** JSON ostatniej zatwierdzonej wersji i widok do drukowania / zapisania jako PDF w przeglądarce. Oba używają tego samego snapshotu i identyfikatora wersji. Typowy wywiad mieści się na A4; bardzo długie odpowiedzi mogą zająć więcej stron.

Wszystkie dane są fikcyjne i przechowywane **wyłącznie w pamięci otwartej strony**. Odświeżenie lub wylogowanie usuwa dane demo. Aplikacja nie używa `localStorage`, nie wysyła haseł, wypowiedzi ani raportów do API. Zewnętrzne Google Fonts dostarczają wyłącznie fonty; interfejs działa też z fontami systemowymi.

## Struktura i modele

- `src/models.ts` — sesja gościa/konta, profil pacjenta, placówka, lekarz, wizyta i terminy, statusy procesu, pytania, wiadomości i odpowiedzi, pola raportu wraz z pochodzeniem i stanem (`provided`, `missing`, `unknown`, `conflicting`), struktury objawów i leków (z powodem przyjmowania), obserwacje, zatwierdzone wersje, zgody i rundy uzupełniające. PESEL i historyczne źródła obserwacji nie są przekazywane do przeglądarki.
- `src/data/mock.ts` — fikcyjne wizyty, konto, pytania oraz implementacja granicy `PatientService`.
- `src/lib/interview.ts` — operacje na wywiadzie, zatwierdzanie, wersjonowanie, zgody i `reportForFacility`, zwracające tylko ostatnią zatwierdzoną wersję przy aktywnej zgodzie.
- `src/components` — rozmowa, sfera, kontekst wizyty, lista i szczegóły wizyt, podsumowanie, logowanie, profil, dostępne klawiaturą okna dialogowe.
- `src/App.tsx` — stan sesji i niezależnych wywiadów; proste trasy w hash URL.
- `tests/app.spec.ts` — pełne przepływy na desktopie i telefonie; `src/lib/interview.test.ts` — reguły danych i zgód.

Mock zadaje deterministyczne pytania i zapisuje wypowiedzi bez dopisywania faktów. Nie wykonuje analizy medycznej, ekstrakcji nazw leków, dawkowania ani objawów. Struktury `Symptom`, `Medication`, `Observation` i `SupplementRound` przygotowano pod docelową integrację; rozbudowany panel lekarza, porównywanie historii i rundy pytań nie są częścią obecnego interfejsu pacjenta.

## Przygotowanie integracji

Istniejący folder `Backend` nie jest podłączony ani modyfikowany. Docelowy adapter powinien zastąpić mock oraz lokalne mutacje:

| Działanie                       | Istniejący endpoint                                                         |
| ------------------------------- | --------------------------------------------------------------------------- |
| Dostęp z linku                  | `GET /api/v1/patient/interviews/by-token/{token}`                           |
| Lista i szczegóły wizyt         | `GET /api/v1/patient/appointments`, `GET /api/v1/patient/appointments/{id}` |
| Wysłanie odpowiedzi             | `POST /api/v1/patient/appointments/{id}/answers`                            |
| Edycja raportu                  | `PUT /api/v1/patient/appointments/{id}/summary`                             |
| Zatwierdzenie                   | `POST /api/v1/patient/appointments/{id}/approve`                            |
| Udostępnienie / cofnięcie zgody | `PUT /api/v1/patient/appointments/{id}/consent`                             |
| PDF                             | `GET /api/v1/patient/appointments/{id}/report.pdf`                          |

Frontend ma szerszy model niż aktualne DTO backendu: dane lekarza i placówki, profil/logowanie, alergie i choroby jako osobne pola, powód przyjmowania leku, dodatkowe opisy, pełne statusy i niezależne decyzje o obserwacjach wymagają rozszerzenia kontraktu lub mapowania. Aktualny backend używa `X-Patient-Id`; produkcyjne uwierzytelnianie i walidacja dostępu z linku/kodu muszą odbywać się na serwerze. Występujące w backendzie `Approved` jest etapem wewnętrznym — w UI zatwierdzony, nieudostępniony raport pozostaje `awaiting_approval`.

Zgodnie z nową prośbą tryb konta stanowi rozszerzenie założeń w `docs`, które pierwotnie opisywały wyłącznie pacjenta bez konta. Pozostałe reguły dotyczące kontroli treści, wersji i osobnej zgody zachowano. Dostępne są wyłącznie wizyty nadchodzące, bez przeglądania wcześniejszych wywiadów. Do wdrożenia produkcyjnego pozostają rzeczywiste AI i rozpoznawanie mowy, uwierzytelnienie, trwały zapis, serwerowa kontrola terminów i uprawnień oraz generowanie PDF.
