# Integracja ElevenLabs Agent z aplikacją web/mobile

## Cel

Aplikacja ma umożliwiać przeprowadzenie wywiadu przez agenta ElevenLabs w dwóch trybach:

- rozmowa głosowa,
- chat tekstowy.

Każda rozmowa musi być jednoznacznie powiązana z:

- wizytą (`Visit`),
- wywiadem (`Interview`),
- opcjonalnie zalogowanym użytkownikiem (`User`),
- konkretną rozmową ElevenLabs (`ConversationId`).

System musi obsługiwać dwa scenariusze:

1. **użytkownik zalogowany** – rozpoczyna wywiad przypisany do swojej wizyty,
2. **użytkownik niezalogowany** – otrzymuje unikalny link do konkretnego wywiadu i może go przeprowadzić bez zakładania konta.

> Kluczowa zasada: `ELEVENLABS_API_KEY` nigdy nie trafia do frontendu. Frontend dostaje wyłącznie krótkotrwały `conversationToken` albo `signedUrl` wygenerowany przez backend.

---

# Proponowana architektura

```text
┌──────────────────────────────┐
│ React Web / React Native     │
│                              │
│ Voice UI / Chat UI           │
└──────────────┬───────────────┘
               │
               │ 1. utworzenie sesji aplikacyjnej
               ▼
┌──────────────────────────────┐
│ ASP.NET Core API             │
│                              │
│ Auth / Visit / Interview     │
│ Session issuing              │
│ ElevenLabs API client        │
└──────────────┬───────────────┘
               │
               │ 2. krótkotrwały token/signed URL
               ▼
┌──────────────────────────────┐
│ ElevenLabs ElevenAgents      │
│                              │
│ Voice / Chat                 │
│ Agent Prompt                 │
│ Data Collection              │
└──────────────┬───────────────┘
               │
               │ 3. post-call webhook
               ▼
┌──────────────────────────────┐
│ ASP.NET Core API             │
│                              │
│ Transcript                   │
│ Analysis                     │
│ Structured data              │
└──────────────┬───────────────┘
               ▼
          PostgreSQL
```

Dla połączeń głosowych rekomendowany jest **WebRTC**. ElevenLabs udostępnia endpoint:

```http
GET /v1/convai/conversation/token
```

który zwraca:

```json
{
  "token": "...",
  "conversation_id": "conv_..."
}
```

Dla połączenia WebSocket można pobrać **signed URL**:

```http
GET /v1/convai/conversation/get-signed-url
```

Signed URL jest krótkotrwały i służy do zestawienia połączenia bez ujawniania klucza API ElevenLabs.

---

# Model domenowy

Nie należy wiązać wizyty bezpośrednio tylko z `ElevenLabsConversationId`.

Lepszym rozwiązaniem jest osobna encja `Interview`, a następnie encja `InterviewSession`.

```text
User
 │
 │ 0..1
 ▼
Visit
 │
 │ 1..n
 ▼
Interview
 │
 │ 1..n
 ▼
InterviewSession
 │
 ▼
ElevenLabs Conversation
```

## Visit

Przykład:

```csharp
public class Visit
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }

    public DateTimeOffset ScheduledAt { get; set; }

    public VisitStatus Status { get; set; }

    public ICollection<Interview> Interviews { get; set; } = [];
}
```

`UserId` może być `null`, jeżeli system pozwala tworzyć wizyty dla osoby, która nie posiada jeszcze konta.

---

## Interview

`Interview` reprezentuje biznesowy wywiad przypisany do wizyty.

```csharp
public class Interview
{
    public Guid Id { get; set; }

    public Guid VisitId { get; set; }

    public InterviewStatus Status { get; set; }

    public string InterviewType { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? FinalReport { get; set; }

    public JsonDocument? StructuredData { get; set; }

    public ICollection<InterviewSession> Sessions { get; set; } = [];
}
```

Przykładowe statusy:

```text
Pending
InProgress
Completed
Cancelled
Expired
```

`Interview` powinien być niezależny od konkretnej sesji ElevenLabs.

Dzięki temu można:

- wznowić wywiad,
- ponowić nieudaną rozmowę,
- przeprowadzić najpierw chat, a później voice,
- zachować historię kilku sesji,
- zmienić dostawcę voice agenta w przyszłości.

---

## InterviewSession

Każde faktyczne rozpoczęcie rozmowy tworzy osobną sesję.

```csharp
public class InterviewSession
{
    public Guid Id { get; set; }

    public Guid InterviewId { get; set; }

    public Guid? UserId { get; set; }

    public InterviewSessionMode Mode { get; set; }

    public string Provider { get; set; } = "elevenlabs";

    public string? ProviderConversationId { get; set; }

    public InterviewSessionStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ConnectedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public JsonDocument? Transcript { get; set; }

    public JsonDocument? Analysis { get; set; }
}
```

Tryb:

```text
Voice
Text
```

Status:

```text
Created
TokenIssued
Connected
Completed
Failed
Abandoned
```

---

# BACKEND

## 1. Konfiguracja ElevenLabs

Na backendzie:

```json
{
  "ElevenLabs": {
    "ApiKey": "...",
    "AgentId": "agent_...",
    "Environment": "production"
  }
}
```

Sekrety przechowywać w:

- environment variables,
- Docker secrets,
- Vault / secret managerze.

Nigdy:

```text
React -> ELEVENLABS_API_KEY
```

Klucz ElevenLabs może znać wyłącznie backend.

---

## 2. Utworzenie wywiadu

Wywiad powinien powstać wcześniej niż sesja ElevenLabs.

Przykład:

```http
POST /api/visits/{visitId}/interviews
Authorization: Bearer ...
```

Response:

```json
{
  "id": "26f4...",
  "visitId": "72cf...",
  "status": "pending"
}
```

Możliwe jest również automatyczne utworzenie `Interview` w momencie utworzenia wizyty.

---

## 3. Rozpoczęcie wywiadu – użytkownik zalogowany

Frontend:

```http
POST /api/interviews/{interviewId}/sessions
Authorization: Bearer <JWT>
Content-Type: application/json

{
  "mode": "voice"
}
```

Backend musi:

1. odczytać użytkownika z JWT,
2. pobrać `Interview`,
3. pobrać przypisaną `Visit`,
4. sprawdzić, czy użytkownik ma prawo do tej wizyty,
5. sprawdzić status wywiadu,
6. utworzyć `InterviewSession`,
7. pobrać credential z ElevenLabs,
8. zapisać `conversation_id`, jeśli jest już dostępny,
9. zwrócić krótkotrwałe dane frontendowi.

Nie należy przyjmować `UserId` z body requestu.

Źródłem tożsamości jest token aplikacji:

```text
JWT
 ↓
ClaimsPrincipal
 ↓
UserId
```

---

## 4. Pobranie WebRTC tokenu dla Voice

Backend wywołuje:

```http
GET https://api.elevenlabs.io/v1/convai/conversation/token
    ?agent_id=agent_xxx
    &environment=production

xi-api-key: ELEVENLABS_API_KEY
```

ElevenLabs zwraca:

```json
{
  "token": "...",
  "conversation_id": "conv_123"
}
```

Backend od razu zapisuje:

```text
InterviewSession.ProviderConversationId = "conv_123"
InterviewSession.Status = TokenIssued
```

Następnie zwraca frontendowi:

```json
{
  "sessionId": "c54e...",
  "mode": "voice",
  "provider": "elevenlabs",
  "conversationToken": "...",
  "conversationId": "conv_123"
}
```

`conversationToken` nie powinien być zapisywany w bazie.

Jest to credential krótkotrwały.

---

## 5. Rozpoczęcie sesji tekstowej

Dla rozmowy tekstowej można użyć połączenia WebSocket i wygenerowanego po stronie serwera signed URL.

Backend:

```http
GET https://api.elevenlabs.io/v1/convai/conversation/get-signed-url
    ?agent_id=agent_xxx
    &include_conversation_id=true

xi-api-key: ELEVENLABS_API_KEY
```

Następnie zwraca frontendowi:

```json
{
  "sessionId": "c54e...",
  "mode": "text",
  "provider": "elevenlabs",
  "signedUrl": "wss://..."
}
```

Signed URL ElevenLabs jest obecnie ważny przez ograniczony czas na rozpoczęcie sesji; nie należy generować go wcześniej „na zapas”.

Jeżeli `conversationId` nie jest znany backendowi w momencie wydania signed URL, frontend po `startSession()` powinien wykonać:

```http
PUT /api/interview-sessions/{sessionId}/provider-conversation
Authorization: Bearer ...

{
  "conversationId": "conv_xxx"
}
```

Backend musi oczywiście sprawdzić, czy `sessionId` należy do tego użytkownika.

---

## 6. Korelacja rozmowy z wizytą

Nie opierać korelacji wyłącznie na informacjach przekazanych przez klienta.

Główny klucz powinien wyglądać tak:

```text
Visit.Id
   ↓
Interview.VisitId
   ↓
InterviewSession.InterviewId
   ↓
InterviewSession.ProviderConversationId
   ↓
ElevenLabs conversation_id
```

Po otrzymaniu webhooka:

```text
conversation_id = conv_123
```

backend wykonuje:

```sql
SELECT *
FROM interview_sessions
WHERE provider_conversation_id = 'conv_123';
```

i w ten sposób jednoznacznie ustala:

```text
Conversation
  -> InterviewSession
  -> Interview
  -> Visit
  -> User
```

---

# BACKEND – UNIKALNY LINK DLA NIEZALOGOWANEGO UŻYTKOWNIKA

## 7. Nie używać VisitId jako tokenu

Niepoprawny link:

```text
https://app.example.com/interview/72cf8c0d-...
```

jeżeli UUID jest jedynym zabezpieczeniem dostępu.

Jeszcze gorsze byłoby przekazanie w URL:

```text
ElevenLabs token
API key
signed URL
```

Zamiast tego aplikacja powinna mieć własny `InterviewInvitation`.

---

## 8. InterviewInvitation

```csharp
public class InterviewInvitation
{
    public Guid Id { get; set; }

    public Guid InterviewId { get; set; }

    public string TokenHash { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? FirstOpenedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public int SessionCount { get; set; }

    public int MaxSessionCount { get; set; } = 3;
}
```

---

## 9. Generowanie tokenu linku

Backend generuje kryptograficznie losowy token, np. 256 bitów.

Przykładowo:

```text
7V93v_zGVHkJx7ZrI_... 
```

Link:

```text
https://app.example.com/i/7V93v_zGVHkJx7ZrI_...
```

W bazie nie zapisujemy samego tokenu.

Zapisujemy:

```text
SHA-256(token)
```

czyli:

```text
URL:
raw token

DB:
hash tokenu
```

Analogicznie do tokenów resetowania hasła.

---

## 10. Otworzenie linku anonimowego

Frontend:

```http
GET /api/public/interviews/{token}
```

Backend:

1. liczy hash tokenu,
2. znajduje `InterviewInvitation`,
3. sprawdza `ExpiresAt`,
4. sprawdza `RevokedAt`,
5. sprawdza status `Interview`,
6. sprawdza limit sesji,
7. zwraca wyłącznie dane potrzebne do ekranu.

Przykład odpowiedzi:

```json
{
  "interview": {
    "displayName": "Wywiad przed wizytą",
    "visitDate": "2026-10-20T14:00:00+02:00",
    "status": "pending"
  }
}
```

Nie trzeba zwracać:

```text
UserId
VisitId
PatientId
ElevenLabsAgentId
```

jeżeli frontend ich nie potrzebuje.

---

## 11. Rozpoczęcie sesji przez anonimowy link

Frontend:

```http
POST /api/public/interviews/{token}/sessions

{
  "mode": "voice"
}
```

Backend wykonuje:

```text
token
 ↓
hash
 ↓
InterviewInvitation
 ↓
InterviewId
 ↓
Visit
```

Następnie:

1. ponownie waliduje invitation,
2. tworzy `InterviewSession`,
3. `UserId = null` lub przypisuje właściciela wizyty wewnętrznie,
4. pobiera WebRTC token / signed URL z ElevenLabs,
5. zapisuje `conversation_id`,
6. zwiększa `SessionCount`,
7. zwraca credential frontendowi.

Dzięki temu ElevenLabs token jest wydawany **dopiero po autoryzacji linkiem aplikacyjnym**.

---

## 12. Dlaczego dwa poziomy tokenów

Powinny istnieć dwa różne tokeny:

```text
A. Interview Invitation Token
   ważność: np. kilka dni
   właściciel: Twoja aplikacja
   cel: prawo dostępu do konkretnego wywiadu

B. ElevenLabs Conversation Token / Signed URL
   ważność: krótka
   właściciel: ElevenLabs
   cel: zestawienie konkretnego połączenia
```

Flow:

```text
email / SMS

https://app.example.com/i/<INVITATION_TOKEN>
                    │
                    ▼
             ASP.NET Core
                    │
          validation invitation
                    │
                    ▼
             ElevenLabs API
                    │
         temporary credential
                    │
                    ▼
                 React
                    │
                    ▼
              ElevenLabs
```

To jest znacznie bezpieczniejsze niż umieszczanie credentiala ElevenLabs bezpośrednio w zaproszeniu.

---

## 13. Opcjonalna anonimowa sesja aplikacyjna

Po pierwszej poprawnej walidacji invitation można wymienić długi token z URL na krótkotrwałą sesję aplikacyjną.

Przykład:

```http
POST /api/public/interviews/{token}/authorize
```

Response:

```json
{
  "accessToken": "eyJ...",
  "expiresIn": 3600
}
```

Token może zawierać claims:

```json
{
  "sub": "anonymous-interview",
  "interview_id": "...",
  "invitation_id": "...",
  "scope": "interview:execute"
}
```

Następne requesty:

```http
Authorization: Bearer <anonymous interview JWT>
```

Zaletą jest to, że token zaproszenia można po walidacji usunąć z paska URL:

```text
/i/ABC123
    ↓
authorization
    ↓
history.replaceState(...)
    ↓
/interview
```

Zmniejsza to ryzyko wycieku tokenu przez:

- historię przeglądarki,
- kopiowanie URL,
- screenshot,
- analytics,
- referrer.

Dla produkcji jest to rekomendowany wariant.

---

# BACKEND – WEBHOOK ELEVENLABS

## 14. Endpoint

```http
POST /api/webhooks/elevenlabs
```

ElevenLabs może wysłać `post_call_transcription` po zakończeniu rozmowy.

Webhook zawiera m.in.:

```text
conversation_id
agent_id
status
user_id
transcript
analysis
metadata
conversation_initiation_client_data
```

---

## 15. Weryfikacja podpisu

Webhook musi być weryfikowany przy pomocy podpisu HMAC dostarczanego przez ElevenLabs.

Nie wolno robić:

```csharp
app.MapPost("/webhooks/elevenlabs", (Webhook payload) =>
{
    Save(payload);
});
```

bez weryfikacji źródła.

Schemat:

```text
HTTP raw body
 +
ElevenLabs-Signature
 +
Webhook secret
 ↓
verify HMAC
 ↓
dopiero wtedy deserialize/process
```

Po poprawnym przetworzeniu endpoint powinien zwrócić `200 OK`.

---

## 16. Przetwarzanie webhooka

```text
post_call_transcription
        │
        ▼
conversation_id
        │
        ▼
InterviewSession
        │
        ▼
Interview
        │
        ▼
Visit
```

Następnie backend zapisuje:

```text
InterviewSession.Transcript
InterviewSession.Analysis
InterviewSession.CompletedAt
InterviewSession.Status = Completed
```

i dane wynikowe:

```text
Interview.StructuredData
Interview.FinalReport
Interview.CompletedAt
Interview.Status = Completed
```

---

## 17. Idempotencja webhooków

Webhook może zostać dostarczony więcej niż raz.

Handler musi być idempotentny.

Przykład:

```text
if session.Status == Completed
and webhook conversation_id już przetworzony
    -> 200 OK
```

Można również prowadzić tabelę:

```text
ExternalWebhookEvents

Id
Provider
ExternalEventId
ReceivedAt
ProcessedAt
PayloadHash
```

---

# BACKEND – API

Minimalny zestaw endpointów:

```text
# Zalogowany użytkownik

GET  /api/visits/{visitId}/interview
POST /api/interviews/{interviewId}/sessions
PUT  /api/interview-sessions/{sessionId}/provider-conversation
GET  /api/interviews/{interviewId}
GET  /api/interviews/{interviewId}/result


# Publiczny link

GET  /api/public/interviews/{invitationToken}
POST /api/public/interviews/{invitationToken}/authorize
POST /api/public/interviews/{invitationToken}/sessions


# Webhook

POST /api/webhooks/elevenlabs
```

Jeżeli używany jest anonymous JWT po `/authorize`, można uprościć publiczne API:

```text
POST /api/public/interviews/authorize

GET  /api/interview
POST /api/interview/sessions
GET  /api/interview/result
```

a kontekst konkretnego wywiadu pobierać z claims anonimowego JWT.

---

# FRONTEND

## 18. Biblioteka ElevenLabs

Dla Reacta można użyć oficjalnego SDK ElevenLabs.

Główna odpowiedzialność frontendu:

```text
UI
+
mikrofon
+
połączenie realtime z ElevenLabs
+
wyświetlanie odpowiedzi
+
obsługa stanu rozmowy
```

Frontend **nie odpowiada** za ustalenie, do jakiej wizyty należy rozmowa.

To robi backend.

---

## 19. Flow użytkownika zalogowanego

```text
/visits/{visitId}/interview
        │
        ▼
GET interview
        │
        ▼
klik "Rozpocznij"
        │
        ▼
POST /interviews/{id}/sessions
        │
        ▼
conversationToken
        │
        ▼
ElevenLabs startSession()
        │
        ▼
rozmowa
```

---

## 20. Start voice session

Przykładowa logika:

```ts
const response = await api.post(
  `/api/interviews/${interviewId}/sessions`,
  {
    mode: "voice"
  }
);

const {
  sessionId,
  conversationToken
} = response.data;

const conversationId = await conversation.startSession({
  conversationToken
});
```

Połączenie voice powinno używać WebRTC.

Przeglądarka musi wcześniej uzyskać zgodę na mikrofon:

```ts
await navigator.mediaDevices.getUserMedia({
  audio: true
});
```

---

## 21. Start chat session

```ts
const response = await api.post(
  `/api/interviews/${interviewId}/sessions`,
  {
    mode: "text"
  }
);

const {
  sessionId,
  signedUrl
} = response.data;

const conversationId = await conversation.startSession({
  signedUrl
});
```

Frontend następnie wysyła wiadomości do aktywnej rozmowy i renderuje eventy agenta w swoim UI.

---

## 22. Powiązanie conversationId po stronie frontendu

`startSession()` zwraca `conversationId`.

Jeżeli backend jeszcze go nie zna, frontend natychmiast wykonuje:

```http
PUT /api/interview-sessions/{sessionId}/provider-conversation

{
  "conversationId": "conv_xxx"
}
```

Dla WebRTC zwykle backend może już znać `conversation_id`, ponieważ endpoint wydający conversation token zwraca go razem z tokenem.

Frontendowe zgłoszenie `conversationId` należy więc traktować jako:

- wymagane tam, gdzie backend go wcześniej nie zna,
- dodatkową kontrolę/spójność tam, gdzie zna go już wcześniej.

---

# FRONTEND – LINK ANONIMOWY

## 23. Routing

Link otrzymany np. e-mailem:

```text
https://app.example.com/i/<TOKEN>
```

Route:

```tsx
<Route
  path="/i/:token"
  element={<InterviewInvitationPage />}
/>
```

---

## 24. Walidacja linku

Po wejściu:

```text
GET /api/public/interviews/{token}
```

UI pokazuje np.:

```text
Wywiad przed wizytą

Wizyta:
20 października 2026, 14:00

Wywiad potrwa około 5–10 minut.

[ Rozpocznij rozmowę ]

lub

[ Przejdź do czatu ]
```

Jeżeli token jest:

```text
expired
revoked
already completed
invalid
```

frontend pokazuje odpowiedni ekran i **nie próbuje kontaktować się z ElevenLabs**.

---

## 25. Rekomendowany flow z anonymous JWT

Najlepszy wariant:

```text
/i/<token>
      │
      ▼
POST /api/public/interviews/{token}/authorize
      │
      ▼
anonymous access JWT
      │
      ▼
usunąć invitation token z URL
      │
      ▼
/interview
```

Od tego momentu frontend zachowuje się prawie identycznie jak dla zalogowanego użytkownika.

Różnica polega tylko na scope tokenu.

Normalny użytkownik:

```text
scope:
visits
profile
interviews
...
```

Anonymous JWT:

```text
scope:
interview:read
interview:execute

interview_id:
26f4...
```

Nie daje dostępu do żadnej innej wizyty ani danych użytkownika.

---

# FRONTEND – UI STANÓW ROZMOWY

## 26. Voice UI

Frontend powinien rozróżniać przynajmniej:

```text
idle
requesting_microphone
connecting
agent_speaking
user_speaking
connected
ending
completed
error
```

Pozwala to sterować np. animowaną kulą:

```text
idle
  ○

connecting
  ◌

agent_speaking
  ◉ pulsowanie

user_speaking
  ● reakcja na poziom audio
```

---

## 27. Chat UI

Minimalne dane wiadomości:

```ts
type ChatMessage = {
  id: string;
  role: "user" | "agent";
  content: string;
  createdAt: Date;
};
```

UI:

```text
┌─────────────────────────────────┐
│ Agent                           │
│ Co jest głównym powodem wizyty? │
└─────────────────────────────────┘

                 ┌────────────────┐
                 │ Od 3 dni boli  │
                 │ mnie brzuch.   │
                 └────────────────┘

┌─────────────────────────────────┐
│ Agent                           │
│ Gdzie dokładnie odczuwa Pan     │
│ ból?                            │
└─────────────────────────────────┘
```

---

# PRZEKAZYWANIE KONTEKSTU DO AGENTA

## 28. Dynamic variables

Do agenta można przekazać kontekst wymagany podczas rozmowy.

Np.:

```json
{
  "visit_type": "pierwsza konsultacja",
  "specialization": "kardiologia",
  "language": "pl"
}
```

Nie należy jednak traktować dynamic variables jako mechanizmu autoryzacji.

Nie należy ufać np.:

```json
{
  "user_id": "...",
  "visit_id": "..."
}
```

przesłanym przez klienta jako dowodowi, że użytkownik ma dostęp do tej wizyty.

Autoryzacja zawsze odbywa się w backendzie.

---

## 29. Identyfikator użytkownika dla ElevenLabs

Dla zalogowanego użytkownika można przekazać do ElevenLabs własny techniczny identyfikator końcowego użytkownika.

Najlepiej użyć:

```text
opaque internal ID
```

zamiast:

```text
email
PESEL
imię + nazwisko
```

Np.:

```text
usr_8efd...
```

Dla anonimowego użytkownika:

```text
anon_<InterviewSessionId>
```

Nie jest to jednak główny mechanizm korelacji.

Głównym mechanizmem pozostaje:

```text
ProviderConversationId
```

przechowywany w `InterviewSession`.

---

# ZAKOŃCZENIE ROZMOWY

## 30. Co robi frontend

Po zakończeniu sesji:

```text
ElevenLabs connection ended
        │
        ▼
frontend pokazuje:
"Wywiad został zakończony"
```

Frontend nie musi czekać na wygenerowanie finalnego raportu.

Analiza post-call może zostać dostarczona webhookiem chwilę później.

UI może odpytywać:

```http
GET /api/interviews/{id}
```

i otrzymać:

```json
{
  "status": "processing"
}
```

a później:

```json
{
  "status": "completed"
}
```

W aplikacji publicznej można po prostu pokazać:

```text
Dziękujemy.
Wywiad został zapisany i przekazany do przygotowania wizyty.
```

---

# DANE DO RAPORTU

## 31. Structured Data

Nie przechowywałbym wyłącznie finalnego tekstowego raportu.

Warto zachować:

```text
1. transcript,
2. wynik ElevenLabs Data Collection,
3. finalny raport,
4. metadata rozmowy.
```

Przykład:

```json
{
  "reasonForVisit": "ból brzucha",
  "symptomDuration": "3 dni",
  "painLevel": 6,
  "medications": [
    "..."
  ],
  "allergies": [],
  "additionalInformation": "..."
}
```

Na tej podstawie można później:

- zmienić format raportu,
- wygenerować nowy raport bez ponownego wywiadu,
- analizować dane,
- integrować je z innymi systemami.

---

# BEZPIECZEŃSTWO

## 32. Najważniejsze zasady

### API key

```text
ELEVENLABS_API_KEY
```

wyłącznie backend.

### Invitation token

- co najmniej 128–256 bitów entropii,
- generowany przez CSPRNG,
- weryfikacja przez hash; od MVP recepcji także szyfrowana kopia do ponownego pobrania linku przez administrację,
- możliwość wygaśnięcia,
- możliwość ręcznego unieważnienia.

### Conversation credential

Generowany dopiero tuż przed rozpoczęciem rozmowy.

### Rate limiting

Szczególnie:

```text
POST /api/public/interviews/*/authorize
POST /api/public/interviews/*/sessions
```

### Single interview scope

Anonymous JWT powinien pozwalać wyłącznie na:

```text
interview_id = X
```

i nic więcej.

### Webhook

Weryfikować HMAC `ElevenLabs-Signature`.

### Logi

Nie logować:

```text
invitation raw token
anonymous JWT
ElevenLabs conversationToken
signed URL
pełnych danych medycznych
```

do zwykłych logów aplikacyjnych.

---

# Rekomendowany przepływ końcowy

## Użytkownik zalogowany

```text
Login
  │
  ▼
Visit
  │
  ▼
Interview
  │
  ▼
POST /sessions
  │
  ├── backend sprawdza JWT
  ├── backend sprawdza dostęp do Visit
  ├── backend tworzy InterviewSession
  ├── backend pobiera ElevenLabs credential
  └── backend zapisuje conversation_id
  │
  ▼
Frontend
  │
  ▼
ElevenLabs
  │
  ▼
conversation
  │
  ▼
post-call webhook
  │
  ▼
InterviewSession
  │
  ▼
Interview
  │
  ▼
Report
```

---

## Użytkownik niezalogowany

```text
SMS / email
  │
  ▼
https://app.example.com/i/<INVITATION_TOKEN>
  │
  ▼
Backend
  │
  ├── hash tokenu
  ├── invitation lookup
  ├── expiration
  ├── revoked?
  ├── completed?
  └── session limit
  │
  ▼
anonymous JWT
  │
  ▼
Frontend usuwa raw token z URL
  │
  ▼
POST /interview/sessions
  │
  ▼
Backend
  │
  ├── InterviewSession
  ├── ElevenLabs token
  └── conversation_id
  │
  ▼
Frontend ↔ ElevenLabs
  │
  ▼
post-call webhook
  │
  ▼
Backend
  │
  ▼
Interview + Visit
```

---

# Minimalny zakres MVP

## Backend

1. `Visit`
2. `Interview`
3. `InterviewSession`
4. `InterviewInvitation`
5. endpoint tworzący ElevenLabs session,
6. obsługa użytkownika zalogowanego,
7. obsługa anonymous invitation,
8. ElevenLabs WebRTC token,
9. ElevenLabs signed URL dla text chat,
10. post-call webhook,
11. HMAC verification,
12. zapis transkrypcji i Data Collection,
13. status wywiadu.

## Frontend

1. ekran wywiadu,
2. obsługa linku `/i/:token`,
3. wymiana invitation token → anonymous JWT,
4. Voice UI,
5. Chat UI,
6. ElevenLabs React SDK,
7. WebRTC voice,
8. WebSocket chat,
9. obsługa statusów sesji,
10. ekran końcowy.

---

# Sugerowane nazwy encji

```text
Visit
Interview
InterviewSession
InterviewInvitation
InterviewResult
```

Opcjonalnie:

```text
InterviewTemplate
```

jeżeli system ma obsługiwać różne typy wywiadów.

`InterviewTemplate` może definiować np.:

```text
Name
AgentId
VersionId
InterviewType
Specialization
Configuration
RequiredFields
```

Dzięki temu jedna aplikacja może obsługiwać:

```text
kardiologia
ortopedia
dermatologia
wywiad ogólny
kontrola
pierwsza wizyta
```

przy użyciu różnych konfiguracji ElevenLabs.

---

# Dokumentacja ElevenLabs wykorzystana przy projektowaniu

Aktualne punkty integracyjne:

- React SDK:
  https://elevenlabs.io/docs/eleven-agents/libraries/react

- Agent authentication:
  https://elevenlabs.io/docs/eleven-agents/customization/authentication

- WebRTC conversation token:
  https://elevenlabs.io/docs/eleven-agents/api-reference/conversations/get-webrtc-token

- Signed URL:
  https://elevenlabs.io/docs/eleven-agents/api-reference/conversations/get-signed-url

- Post-call webhooks:
  https://elevenlabs.io/docs/eleven-agents/workflows/post-call-webhooks

---

# Podsumowanie decyzji architektonicznych

Najważniejszy podział odpowiedzialności:

```text
FRONTEND
- UI voice/chat
- mikrofon
- połączenie realtime
- wyświetlanie rozmowy

BACKEND
- użytkownicy
- wizyty
- wywiady
- autoryzacja
- anonymous invitation
- wydawanie credentiali ElevenLabs
- mapowanie conversation → interview → visit
- webhook
- raport
- dane strukturalne

ELEVENLABS
- agent
- STT
- LLM / conversation
- TTS
- realtime
- transcript
- post-call analysis
- data collection
```

Najważniejsze powiązanie w bazie:

```text
Visit
  ↓
Interview
  ↓
InterviewSession
  ↓
ProviderConversationId
```

a dla dostępu bez logowania:

```text
Interview
  ↓
InterviewInvitation
  ↓
hashed random token
  ↓
anonymous short-lived JWT
  ↓
InterviewSession
```

Taki model pozwala bezpiecznie obsłużyć zarówno użytkownika zalogowanego, jak i osobę posiadającą wyłącznie jednorazowy/linkowany dostęp do konkretnego wywiadu.
