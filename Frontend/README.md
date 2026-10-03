# Przed wizytą — frontend

React + TypeScript + Vite. Panel pacjenta pozostaje demonstracją, a oddzielny ekran rozmowy obsługuje backend ASP.NET i oficjalny SDK ElevenLabs. Animowana sfera ma przezroczysty środek, pięć fal zmieniających kolor i krycie oraz reakcję na poziom audio.

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
| `/`                           | Panel demo: wywiad, wizyta, edytowalny raport; po logowaniu lista wizyt, profil i dodatkowy opis. |
| `/i/demo-appointment-1`       | Sama rozmowa demo dla pierwszej wizyty, bez sidebaru i kart.                                      |
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

Pusty adres API używa proxy / tej samej domeny. Nie ma mostka `X-Patient-Id`. Aplikacja nadrzędna może przekazać sesję pacjenta otrzymaną z `/api/v1/patient-access/link/exchange` lub `/code/exchange` przez `agentApi.setPatientSession(token)`; adapter przechowuje ją w pamięci i wysyła jako bearer token. Przy bezpośrednim otwarciu ścieżki wizyty bez takiej sesji serwer odmówi dostępu. Logowanie demo, lista wizyt i profil nie są jeszcze podłączone do API. Uwierzytelnienie JWT personelu backendu jest osobnym mechanizmem. Nigdy nie dodawaj klucza ElevenLabs do zmiennych `VITE_*`.

- Głos: kliknięcie mikrofonu → zgoda na mikrofon → backend wydaje `conversationToken` → SDK zestawia WebRTC. Stany połączenia, mówienia, słuchania i wyciszenia sterują sferą. Zakończenie i opuszczenie ekranu zamykają transport SDK.
- Tekst: backend wydaje `signedUrl` → SDK używa WebSocket z `textOnly: true`; mikrofon nie jest potrzebny. Enter wysyła, Shift+Enter dodaje linię.
- Zmiana trybu: poprzedni transport jest zamykany i oznaczany jako kontynuacja wywiadu; historia pozostaje widoczna i trafia do nowej sesji jako kontekst. Każde rozpoczęcie zużywa jedną z maksymalnie trzech sesji zaproszenia.
- Wynik: backend zapisuje podpisany webhook. Adapter mapuje `finalReport` i `structuredDataJson` na model UI. Interfejs odpytuje wynik przez minutę i pozwala sprawdzić go ponownie. Nie tworzy lokalnie raportu z rozmowy ElevenLabs ani nie przekazuje go automatycznie placówce.
- Błędne, wygasłe, unieważnione lub ukończone zaproszenie blokuje uruchomienie SDK. Brak mikrofonu pozwala przejść do tekstu.

Implementację zweryfikowano z [dokumentacją JavaScript SDK](https://elevenlabs.io/docs/eleven-agents/libraries/java-script). `Conversation.startSession()` zwraca instancję; identyfikator odczytujemy przez `getId()`.

## Zakres demo

Panel i dwa jawne linki `demo-appointment-*` używają fikcyjnych danych w pamięci. Symulacja głosu pozwala wpisać lub wybrać transkrypcję; nie nagrywa mikrofonu. Konto demo przyjmuje fikcyjny e-mail i dowolne niepuste hasło, którego nie zapisuje ani nie wysyła. Odświeżenie/wylogowanie czyści stan.

Raport demo można edytować, zatwierdzić i osobno udostępnić lub cofnąć zgodę. Zatwierdzone wersje są niezmienne; eksport JSON i druk/PDF korzystają z tego samego snapshotu. Rzeczywisty wynik ElevenLabs jest obecnie wyświetlany jako podsumowanie z serwera; edycja, zatwierdzanie i udostępnianie tego wyniku przez dotychczasowy panel nie są jeszcze podłączone.

## Kod

- `src/models.ts`, `src/data/mock.ts`, `src/lib/interview.ts` — modele i reguły wersji/zgód demo.
- `src/lib/routes.ts`, `src/components/StandaloneConversation.tsx` — linki i ekran bez sidebaru.
- `src/lib/agent-api.ts` — kontrakt API, autoryzacja i credentiale.
- `src/hooks/useAgentConversation.ts` — cykl życia SDK, historia, mikrofon, poziom audio i wynik.
- `src/components/Orb.tsx` — SVG fal bez wypełnienia środka.
- `src/lib/*.test.ts`, `tests/*.spec.ts` — reguły danych, kontrakt API i przepływy przeglądarkowe.
