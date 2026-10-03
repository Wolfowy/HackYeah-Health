# DocPrep - karta projektu

**Cel:** pomóc pacjentowi przygotować się do wizyty lekarskiej poprzez uporządkowanie informacji o objawach, lekach i pytaniach do lekarza, usprawnić przygotowanie lekarza do konsultacji oraz ograniczyć ponowne zbieranie podstawowych informacji.

**Grupa docelowa:** osoby przygotowujące się do konsultacji, które mają trudność z zapamiętaniem i zwięzłym przedstawieniem przebiegu dolegliwości; lekarze potrzebujący szybkiego przeglądu informacji przed wizytą; personel przychodni monitorujący przygotowanie pacjentów do konsultacji.

**Problem:** informacje istotne podczas wizyty bywają rozproszone lub zapominane. Pacjent potrzebuje prostego sposobu zebrania ich w czytelne podsumowanie. Lekarz poświęca część konsultacji na ponowne zbieranie podstawowych danych, a przychodnia potrzebuje widoczności, czy pacjent uzupełnił wywiad przed wizytą.

## Zakres funkcjonalny MVP na hackathon

1. **Wywiad tekstowy i głosowy wspierany przez AI** - pacjent rozpoczyna proces przy użyciu bezpiecznego linku albo kodu wizyty otrzymanego od placówki i wybiera formę rozmowy. W trybie głosowym aplikacja odczytuje pytania i rozpoznaje odpowiedzi. Pacjent może poprawić transkrypcję lub przejść do pisania. Nagranie jest usuwane po utworzeniu transkrypcji.
2. **Chronologia objawów** - uporządkowanie zgłoszonych dolegliwości i ich zmian w czasie z możliwością poprawienia dat i opisów.
3. **Lista leków i istotnych informacji** - zebranie przyjmowanych leków, dawek, alergii oraz chorób przewlekłych podczas wywiadu lub przez formularz.
4. **Pytania do lekarza** - zapis własnych pytań oraz propozycje pytań wynikających z informacji podanych przez pacjenta.
5. **Podgląd i zatwierdzenie podsumowania** - pacjent może poprawić lub usunąć każdą informację. Aplikacja oznacza informacje nieuzupełnione i sprzeczne, nie dopisuje faktów oraz pozwala zatwierdzić niepełny raport po świadomym potwierdzeniu. Zatwierdzenie treści i zgoda na udostępnienie są osobnymi krokami.
6. **Raport na wizytę** - wygenerowanie jednostronicowego PDF i odpowiadającego mu JSON zawierających powód konsultacji, chronologię objawów, leki, alergie, braki, pytania oraz zatwierdzone obserwacje.
7. **API integracyjne dla dostawcy usług medycznych** - udostępnia te same dozwolone informacje i działania co panel placówki: utworzenie wywiadu powiązanego z PESEL-em i wizytą, wygenerowanie lub ponowienie linku, pobieranie statusów, anulowanie procesu, przekazywanie pytań uzupełniających oraz pobieranie zatwierdzonych wersji w JSON i PDF. Dostęp wymaga uwierzytelnienia i jest ograniczony do placówki. Treść jest dostępna wyłącznie po zgodzie pacjenta.
8. **Historia wywiadów wykorzystywana przez system** - zatwierdzone wywiady tego samego pacjenta są łączone na podstawie PESEL-u przekazanego przez placówkę. Pacjent nie przegląda wcześniejszych wywiadów ani źródeł obserwacji w MVP. System używa historii wyłącznie do zaproponowania obserwacji dla bieżącego raportu.
9. **Zmiany i powtarzalne trendy między wywiadami** - porównanie bieżącego wywiadu ze wszystkimi wcześniejszymi wywiadami powiązanymi PESEL-em i wskazanie nowych, ponownie zgłoszonych lub zmieniających się objawów oraz powtarzalnych wzorców. Brak wzmianki o objawie nie oznacza jego ustąpienia. Przy niewystarczających danych aplikacja oznacza brak podstaw do określenia trendu. Pacjent może zaakceptować, odrzucić lub edytować obserwację przed dodaniem jej do raportu.
10. **Panel przygotowania do wizyt** - lista nadchodzących wizyt ze statusami: `nierozpoczęty`, `w trakcie`, `oczekuje na zatwierdzenie`, `udostępniony`, `wymaga uzupełnienia`, `wygasły` i `anulowany`. Personel administracyjny widzi statusy oraz informacje o dostarczeniu linku, ale nie widzi treści klinicznej.
11. **Szybki przegląd dla lekarza** - uporządkowany widok powodu konsultacji, objawów, leków, alergii, pytań pacjenta, braków i zatwierdzonych obserwacji. Lekarz widzi wcześniejszy wpis źródłowy tylko wtedy, gdy pacjent osobno udostępnił ten wywiad tej placówce.
12. **Braki i niejasności do wyjaśnienia** - wskazanie nieuzupełnionych lub sprzecznych informacji, na przykład nieznanej dawki leku albo rozbieżnych dat początku objawów. Lekarz otrzymuje listę tematów wymagających potwierdzenia podczas wizyty.
13. **Prośba o uzupełnienie wywiadu** - lekarz może przesyłać dodatkowe pytania. Nowe pytania są dopisywane do jednej otwartej rundy. Pacjent odpowiada tekstowo lub głosowo i zatwierdza nową wersję podsumowania. Do tego czasu lekarz widzi poprzednią zatwierdzoną wersję, która pozostaje również w historii po aktualizacji.
14. **Wsparcie dokumentacji wizyty** - możliwość skopiowania zatwierdzonego podsumowania do dokumentacji medycznej oraz pobrania go przez API. Widok rozróżnia informacje zgłoszone przez pacjenta, obserwacje AI oraz zmiany wprowadzone przez pacjenta.

## Dostęp, zgody i dane

- Pacjent nie posiada konta DocPrep. Każdy wywiad rozpoczyna się od ważnego linku lub kodu wizyty.
- PESEL przekazuje placówka. Służy do łączenia wywiadów, lecz nie jest mechanizmem dostępu.
- Link jest wysyłany SMS-em lub e-mailem, działa do końca obsługi wizyty, a ponowne wygenerowanie unieważnia poprzedni.
- Zgoda dotyczy jednego wywiadu i jednej placówki. Kolejne zatwierdzone wersje tego wywiadu pozostają objęte aktywną zgodą.
- Cofnięcie zgody blokuje dalszy dostęp w panelu i API, ale nie usuwa kopii pobranych wcześniej przez placówkę.
- Po wygaśnięciu placówka zachowuje dostęp do wcześniej udostępnionego raportu. Anulowanie wizyty blokuje ten dostęp.
- Zweryfikowane przez placówkę żądanie usunięcia obejmuje wszystkie dane DocPrep powiązane z PESEL-em. Nie obejmuje kopii przechowywanych poza DocPrep.

## Ścieżki użytkowników

**Ścieżka pacjenta:** otrzymanie linku lub kodu wizyty -> wywiad tekstowy albo głosowy -> propozycje obserwacji na podstawie historii -> sprawdzenie braków i całego podsumowania -> zatwierdzenie treści -> osobna decyzja o udostępnieniu -> ewentualna odpowiedź na pytania lekarza -> zatwierdzenie zaktualizowanej wersji.

**Ścieżka placówki:** utworzenie wywiadu i wysłanie linku -> monitorowanie statusu bez dostępu personelu administracyjnego do treści -> przegląd udostępnionego raportu przez lekarza -> ewentualna prośba o uzupełnienie -> wykorzystanie aktualnej zatwierdzonej wersji w panelu, PDF, JSON lub dokumentacji medycznej.

## Poza zakresem MVP

Diagnozowanie, sugerowanie diagnozy, zalecanie leczenia, ocena pilności objawów, wykrywanie stanów nagłych, komunikaty dla nagłych przypadków, analiza badań, obsługa przebiegu wizyty po jej rozpoczęciu, samodzielne przeglądanie historii przez pacjenta, rozbudowane statystyki oraz konfigurator wywiadów dla specjalizacji. Podłączenie do konkretnego produkcyjnego systemu medycznego wymaga dostępu do jego dokumentacji i środowiska integracyjnego.

## Kryterium sukcesu

Pacjent rozpoczyna proces za pomocą linku lub kodu wizyty, kończy wywiad tekstowy albo głosowy i otrzymuje raport zgodny z podanymi informacjami. Przy kolejnych wywiadach system wskazuje zmiany i powtarzalne wzorce poparte wewnętrznymi źródłami, a pacjent kontroluje, które obserwacje trafiają do bieżącego raportu.

Demo obejmuje trzy fikcyjne wywiady tego samego pacjenta pokazujące nowy objaw, zmianę nasilenia i powtarzającą się dolegliwość. Placówka inicjuje wywiad przez API i monitoruje status. Pacjent zatwierdza oraz udostępnia raport. Lekarz przegląda raport i braki, a następnie zadaje pytania uzupełniające. Po odpowiedzi i ponownym zatwierdzeniu lekarz otrzymuje zaktualizowaną wersję w panelu i przez API, podczas gdy poprzednia wersja pozostaje dostępna w historii.

