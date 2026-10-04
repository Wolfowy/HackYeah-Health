const messages: Record<string, string> = {
  'draft.revision_conflict':
    'Raport zmienił się w innej sesji. Odśwież go przed ponownym zapisem. Twoje poprawki pozostają w formularzu.',
  'incomplete_report.confirmation_required':
    'Potwierdź, że chcesz zatwierdzić raport z brakującymi informacjami.',
  'observation.decision_required': 'Zaakceptuj lub odrzuć każdą propozycję obserwacji.',
  'supplementation.answers_missing': 'Odpowiedz na wszystkie pytania od lekarza.',
  'report.generation_failed':
    'Treść raportu została zatwierdzona, ale nie udało się przygotować PDF. Ponów generowanie PDF.',
  'report.not_ready': 'Raport PDF nie jest jeszcze gotowy. Ponów jego generowanie.',
  'extraction.not_available': 'Brak danych do ponownego importu. Możesz uzupełnić raport ręcznie.',
  'import.manual_changes':
    'Raport zawiera Twoje nowsze poprawki. Ponowny import nie może ich nadpisać.',
  'visit.expired': 'Zakończył się czas na przygotowanie tej wizyty.',
  'visit.cancelled': 'Ta wizyta została anulowana.',
  'transcription.unavailable':
    'Transkrypcja głosu nie jest skonfigurowana na serwerze. Możesz dopowiedzieć tekstowo.',
  'transcription.failed':
    'Nie udało się rozpoznać nagrania. Spróbuj ponownie lub dopowiedz tekstowo.',
  'transcription.empty': 'Nie rozpoznano tekstu w nagraniu. Spróbuj ponownie.',
  'patient_account.invalid_credentials': 'Nieprawidłowy adres e-mail lub hasło.',
  'patient_account.invalid_token': 'Sesja konta wygasła. Zaloguj się ponownie.',
  'patient_account.exists': 'Konto dla tego pacjenta lub adresu e-mail już istnieje. Zaloguj się.',
}
export class AgentApiError extends Error {
  constructor(
    public status: number,
    public code?: string,
  ) {
    super(
      messages[code ?? ''] ??
        (status === 401 || status === 403
          ? 'Nie masz dostępu do tej rozmowy. Otwórz ponownie link otrzymany od placówki.'
          : status === 404
            ? 'Nie znaleziono danych. Sprawdź otrzymany link.'
            : status === 410 ||
                (status === 409 &&
                  (code?.startsWith('agent_invitation.') ||
                    code === 'agent_interview.inactive' ||
                    code === 'visit.inactive'))
              ? 'Ta rozmowa jest zakończona albo link wygasł lub został unieważniony. Poproś placówkę o nowy.'
              : status === 429
                ? 'Wykorzystano limit żądań. Spróbuj ponownie później.'
                : status === 400
                  ? 'Sprawdź wprowadzone dane i spróbuj ponownie.'
                  : status === 503
                    ? 'Rozmowa jest chwilowo niedostępna. Spróbuj ponownie później.'
                    : 'Nie udało się wykonać operacji. Spróbuj ponownie.'),
    )
  }
}

export async function backendResponse(
  baseUrl: string,
  fetcher: typeof fetch,
  path: string,
  token = '',
  body?: unknown,
  method = 'GET',
  signal?: AbortSignal,
  timeoutMs = 15000,
) {
  const headers: Record<string, string> = { Accept: 'application/json, application/pdf' }
  if (token) headers.Authorization = `Bearer ${token}`
  const multipart = body instanceof FormData
  if (body !== undefined && !multipart) headers['Content-Type'] = 'application/json'
  let response: Response
  try {
    const timeout = AbortSignal.timeout(timeoutMs)
    response = await fetcher(`${baseUrl.replace(/\/$/, '')}${path}`, {
      method,
      headers,
      credentials: 'omit',
      body: body === undefined ? undefined : multipart ? body : JSON.stringify(body),
      signal: signal ? AbortSignal.any([signal, timeout]) : timeout,
    })
  } catch (cause) {
    if (signal?.aborted) throw cause
    throw new Error('Nie udało się połączyć z serwerem. Sprawdź połączenie i spróbuj ponownie.')
  }
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as { code?: string } | null
    throw new AgentApiError(response.status, problem?.code)
  }
  return response
}
