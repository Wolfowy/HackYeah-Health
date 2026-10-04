# DocPrep — panel recepcji i lekarza

Osobny frontend personelu przychodni w React, TypeScript i [Ant Design](https://ant.design/). Recepcja zarządza terminami i zaproszeniami, a lekarz przegląda swój plan dnia oraz zatwierdzone raporty pacjentów. Aplikacja ma dwa jawnie rozdzielone źródła danych: działające API i lokalne demo.

## Uruchomienie

Wymagany Node.js 22.12+ lub 24 LTS oraz npm:

```sh
cd AdminFrontend
npm ci
npm run dev
```

Panel: **http://127.0.0.1:5174**. Frontend pacjenta działa osobno na 5173.

Domyślna zakładka **Konto placówki** wysyła logowanie do backendu. Serwer Vite przekazuje `/api` do `http://127.0.0.1:8080`; inny adres można wskazać przez `DOCPREP_API_TARGET`. W środowisku docelowym serwer WWW musi również przekazywać `/api` do backendu. Aplikacja nie zawiera klucza systemowego ani klucza integracji.

```sh
DOCPREP_API_TARGET=http://127.0.0.1:8080 npm run dev
```

Opcjonalnie skopiuj `.env.example` do `.env.local` i ustaw `VITE_PATIENT_FRONTEND_URL`, czyli adres aplikacji pacjenta używany w linkach `/i/{token}`. Vite wymaga restartu po zmianie konfiguracji. Ten adres jest publiczną konfiguracją, nie sekretem.

Przyciski **Otwórz wersję demo** i **Otwórz demo lekarza** nie wykonują żądań API. Zakładka **Demo** pozwala też zalogować się fikcyjnym kontem `admin@docprep.local` lub `doctor@docprep.local`, hasło `DocPrepDemo!2026`. W trybie **Konto placówki** te same dane działają tylko wtedy, gdy backend ma włączony deweloperski seed kont. Lekarz z seeda API ma identyfikator `doctor-demo`; lekarz w lokalnym demo ma własny fikcyjny identyfikator.

## Gotowe funkcje

- Logowanie personelu JWT, odnowienie tokenu po 401 i wylogowanie. Tokeny, raporty i linki są wyłącznie w pamięci. Odświeżenie strony w trybie API wymaga ponownego logowania. `sessionStorage` zawiera tylko niesekretny znacznik roli demo.
- Kalendarz tygodnia i miesiąca, mini kalendarz, lista wizyt, wyszukiwanie, filtry lekarza i statusu, paginacja tabeli oraz ręczne odświeżanie. Pobierane są wszystkie strony API, po 100 wizyt, następnie filtrowane lokalnie.
- **Pacjenci na dziś:** startowy widok lekarza, chronologiczna kolejka jego wizyt, nazwisko pacjenta, czas trwania i godzina zakończenia zwrócone przez API, wybór dnia i powrót do dzisiaj. Zawiera również wizyty bez raportu, z czytelnym statusem i szczegółami. Rozszerzone API ogranicza listę i szczegóły do `clinicianId` zalogowanego lekarza; panel zachowuje dodatkowy filtr. Recepcja ma podgląd planu wybranego lekarza.
- Szczegóły wizyty, odświeżenie statusu, zmiana terminu/czasu trwania/końca dostępu/gabinetu z `expectedVersion` oraz anulowanie z potwierdzeniem. Brak ręcznego przełączania statusów procesu pacjenta.
- Rejestracja wizyty przez recepcję: nazwisko pacjenta, telefon/e-mail, lekarz z katalogu placówki, termin, czas trwania (5–240 min), gabinet i rodzaj wizyty. Numer wizyty i PESEL są opcjonalne. Utworzenie wizyty nie wysyła wiadomości (`sendInvitation: false`). Backend domyślnie ustala ważność wywiadu na koniec dnia wizyty.
- Zaproszenia: po rejestracji panel od razu pokazuje rzeczywisty `invitation.url` z odpowiedzi API, pozwala go skopiować lub otworzyć wywiad w nowej karcie. Wysyłka SMS/e-mail jest osobną, opcjonalną akcją. Dla istniejących wizyt panel pobiera zapisany link; regeneracja wymaga potwierdzenia unieważnienia starego zaproszenia. Stan `deliveryMode: demo` jest oznaczony także przy połączeniu z prawdziwym API. Dla starych wizyt bez zapisanych danych recepcji można wpisać kontakt i utworzyć odzyskiwalny link przez istniejący endpoint integracyjny.
- Zatwierdzone raporty lekarza: wybór wersji, odświeżanie dostępu, treść, pobranie chronionego PDF oraz pytania uzupełniające (do 20 pytań, każde do 1000 znaków). Wcześniejszy raport pozostaje dostępny podczas uzupełniania. Administracja w trybie API widzi status, ale nie pobiera treści raportu.
- **Podsumowanie rozmowy na górze raportu:** skrót z zatwierdzonych pól: powodu konsultacji, objawów, leków, pytań i uwag. Aktualny `ReportSnapshot` nie zawiera osobnego podsumowania agenta, dlatego panel jawnie podpisuje skrót „Na podstawie zatwierdzonego raportu”. Nie pobiera prywatnego wyniku rozmowy przez uprawnienia pacjenta i nie dodaje interpretacji klinicznej.
- Responsywny, jasny układ z fioletowym akcentem, polskim locale kontrolek i datami w `Europe/Warsaw`. Fikcyjne dane demo są wyraźnie oznaczone. Demo ma przykłady wizyt na dzisiaj także w weekend.

## Podłączone operacje API

| Akcja                                            | Endpoint                                                                  | Rola w panelu     |
| ------------------------------------------------ | ------------------------------------------------------------------------- | ----------------- |
| Logowanie / odnowienie / wylogowanie             | `POST /api/v1/auth/login`, `/refresh`, `/logout`                          | Personel          |
| Placówka i katalog lekarzy                       | `GET /api/v1/admin/facility`, `/admin/doctors`                            | Recepcja          |
| Lista/szczegóły wizyt recepcji                   | `GET /api/v1/admin/appointments`, `/{id}`                                 | Recepcja          |
| Rejestracja                                      | `POST /api/v1/admin/appointments`                                         | Recepcja          |
| Zmiana terminu/czasu/gabinetu                    | `PUT /api/v1/admin/appointments/{id}`                                     | Recepcja          |
| Anulowanie                                       | `POST /api/v1/admin/appointments/{id}/cancel`                             | Recepcja          |
| Odczyt linku                                     | `GET /api/v1/admin/appointments/{id}/invitation`                          | Recepcja          |
| Wysyłka istniejącego linku                       | `POST /api/v1/admin/appointments/{id}/invitation/send`                    | Recepcja          |
| Nowy link                                        | `POST /api/v1/admin/appointments/{id}/invitation/regenerate`              | Recepcja          |
| Odczyt wizyt/statusu lekarza                     | `GET /api/v1/integration/visits`, `/{id}/status`                          | Lekarz            |
| Wersje raportów                                  | `GET /api/v1/integration/visits/{id}/report-versions`                     | Przypisany lekarz |
| Treść/PDF wersji                                 | `GET /api/v1/integration/visits/{id}/report-versions/{versionId}`, `/pdf` | Przypisany lekarz |
| Pytania uzupełniające                            | `POST /api/v1/integration/visits/{id}/supplementation-round/questions`    | Przypisany lekarz |
| Migracja starego zaproszenia z podanym kontaktem | `POST /api/v1/integration/visits/{id}/invitations`                        | Recepcja          |

Przy zmianie rezerwacji adapter odczytuje też integracyjny status, aby zachować instrukcje lokalizacji niewystawiane w `AppointmentView`. Wszystkie żądania personelu używają Bearer JWT. Nie ma automatycznego przejścia na demo przy błędzie API. Odmowa dostępu usuwa treść raportu z podglądu; wygaśnięcie sesji czyści widoki personelu. Błędy sieci, dostępu, walidacji, kolizji, nieaktualnej wersji i limitu prób mają komunikaty po polsku.

## Granice obecnego kontraktu

- Recepcja korzysta z `AppointmentView`, który zawiera dane pacjenta i czas wizyty. Lekarz odczytuje `patientName`, `durationMinutes` oraz `endsAt` z rozszerzonego `AdminVisitView`. Jeśli nazwisko jest `null`, panel pokazuje `Wizyta {externalVisitId}`. Przy starszej odpowiedzi bez czasu trwania nie wymyśla długości ani godziny końca. Kontakt pacjenta nie jest wystawiany lekarzowi i panel nie wywołuje endpointów recepcji jego kontem.
- Kalendarz wizyty bez znanego czasu używa 30-minutowego znacznika do rozmieszczenia, bez deklarowania rzeczywistej długości. `serviceExpiresAt` oznacza koniec dostępu do wywiadu, nie długość konsultacji.
- Backend recepcji rozstrzyga kolizje lekarza/gabinetu, serializuje rezerwacje i sprawdza `expectedVersion`. Demo ma lokalny mechanizm kolizji. Grafiki pracy, urlopy i cykliczne sloty pozostają poza tym panelem.
- Pełna historia zdarzeń nie jest dostępna w odczycie API; zakładka aktywności pokazuje brak udostępnionej historii. Stare zaproszenia zawierające tylko hash mogą wymagać jednorazowej regeneracji. Stan wysyłki wynika z odpowiedzi backendu i operatora.
- Rozszerzony backend ogranicza listę i szczegóły integracyjne lekarza do jego wizyt. Dostęp do treści/PDF wymaga przypisania i aktywnej zgody pacjenta. Odmowa API pozostaje błędem dostępu, bez przejścia na dane demo.
- Osobne moduły edycji katalogu lekarzy, zarządzania kontami personelu, żądań usunięcia danych i cofania zgody przez recepcję nie należą do tego widoku pracy z kalendarzem. Ich endpointy nie są wywoływane. Wynik rozmowy, konto pacjenta, edycja draftu i sesje głosowe pozostają w aplikacji pacjenta.
- Demo ma symulowane logowanie, wysyłkę i pytania; nie ma eksportu PDF. Zmiany są w pamięci. Font Inter jest opcjonalnie pobierany z Google Fonts; bez sieci używany jest font systemowy.

## Pliki i sprawdzenie

`src/services/http.ts` obsługuje transport i JWT; `src/services/api.ts` mapuje aktualne kontrakty backendu; `src/services/reception.ts` wybiera jawnie API albo adapter demo. Komponenty nie wykonują własnego `fetch`. `DoctorToday`, `VisitReport`, `EditVisit` i `AppointmentDrawer` obsługują przebieg pracy. Modele wzorowane są na `Backend/src/DocPrep.Application/Contracts/Contracts.cs`, `ReceptionContracts.cs` oraz endpointach `Endpoints.cs` i `ReceptionEndpoints.cs`.

```sh
npm run build
npm test
npm run test:e2e
npm run format:check
```

E2E używa zainstalowanego Google Chrome i sprawdza desktop oraz telefon. Operacje zapisu w scenariuszach API są przechwycone i wykonywane na fikcyjnych odpowiedziach. Można uruchomić testy na osobnym porcie, gdy inny agent pracuje na 5174:

```sh
ADMIN_TEST_PORT=5175 ADMIN_TEST_URL=http://127.0.0.1:5175 npm run test:e2e
```

Pełna lista zmian oraz wynik sprawdzeń: [ZMIANY.md](ZMIANY.md).
