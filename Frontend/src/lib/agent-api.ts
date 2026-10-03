import type { ConversationMode } from '../models'

export interface AgentInterviewInfo {
  id: string
  displayName: string
  visitDate: string
  status: 'pending' | 'in_progress' | 'processing' | 'completed'
}
export interface AgentResult {
  status: AgentInterviewInfo['status']
  summary: string | null
  structuredData: Record<string, unknown> | null
}
export type AgentAccess =
  | { kind: 'anonymous'; accessToken: string; interviewId: string }
  | { kind: 'patient'; interviewId: string }
export type AgentCredential = {
  sessionId: string
  provider: 'elevenlabs'
  userId: string
  dynamicVariables: Record<string, string | number | boolean>
  conversationId?: string
} & ({ mode: 'voice'; conversationToken: string } | { mode: 'text'; signedUrl: string })

export class AgentApiError extends Error {
  constructor(
    public status: number,
    public code?: string,
  ) {
    super(
      status === 401 || status === 403
        ? 'Nie masz dostępu do tej rozmowy. Otwórz ponownie link otrzymany od placówki.'
        : status === 404
          ? 'Nie znaleziono rozmowy. Sprawdź otrzymany link.'
          : status === 410 ||
              (status === 409 &&
                (code?.startsWith('agent_invitation.') ||
                  code === 'agent_interview.inactive' ||
                  code === 'visit.inactive'))
            ? 'Ta rozmowa jest zakończona albo link wygasł lub został unieważniony. Poproś placówkę o nowy.'
            : status === 429
              ? 'Wykorzystano limit rozpoczęć rozmowy. Spróbuj później lub poproś placówkę o nowy link.'
              : status === 503
                ? 'Rozmowa jest chwilowo niedostępna. Spróbuj ponownie później.'
                : 'Nie udało się połączyć. Spróbuj ponownie.',
    )
  }
}

/** Application credentials stay in memory; ElevenLabs API keys never enter this client. */
export class AgentApi {
  constructor(
    private baseUrl = import.meta.env?.VITE_API_BASE_URL ?? '',
    private fetcher: typeof fetch = globalThis.fetch.bind(globalThis),
    private patientSessionToken = '',
  ) {}

  /** The host can supply a patient session returned by DocPrep's link/code exchange. */
  setPatientSession(token: string) {
    this.patientSessionToken = token
  }

  private async request<T>(
    path: string,
    access?: AgentAccess,
    body?: unknown,
    method = 'GET',
    signal?: AbortSignal,
  ): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json' }
    if (body !== undefined) headers['Content-Type'] = 'application/json'
    if (access?.kind === 'anonymous') headers.Authorization = `Bearer ${access.accessToken}`
    else if (access?.kind === 'patient' && this.patientSessionToken)
      headers.Authorization = `Bearer ${this.patientSessionToken}`
    const timeout = AbortSignal.timeout(15000)
    let response: Response
    try {
      response = await this.fetcher(`${this.baseUrl.replace(/\/$/, '')}${path}`, {
        method,
        headers,
        credentials: 'omit',
        body: body === undefined ? undefined : JSON.stringify(body),
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
    if (response.status === 204) return undefined as T
    return response.json() as Promise<T>
  }

  async authorizeInvitation(token: string, signal?: AbortSignal) {
    const path = `/api/public/interviews/${encodeURIComponent(token)}`
    const { interview } = await this.request<{ interview: AgentInterviewInfo }>(
      path,
      undefined,
      undefined,
      'GET',
      signal,
    )
    const { accessToken, interviewId } = await this.request<{
      accessToken: string
      expiresIn: number
      interviewId: string
    }>(`${path}/authorize`, undefined, {}, 'POST', signal)
    if (!accessToken || !interviewId) throw new AgentApiError(502)
    return { interview, access: { kind: 'anonymous', accessToken, interviewId } as AgentAccess }
  }
  async getVisitInterview(visitId: string, signal?: AbortSignal) {
    const interview = await this.request<AgentInterviewInfo>(
      `/api/visits/${encodeURIComponent(visitId)}/interview`,
      { kind: 'patient', interviewId: '' },
      undefined,
      'GET',
      signal,
    )
    return { interviewId: interview.id, interview }
  }
  async startSession(access: AgentAccess, mode: ConversationMode, signal?: AbortSignal) {
    const path = `/api/interviews/${encodeURIComponent(access.interviewId)}/sessions`
    const credential = await this.request<AgentCredential>(path, access, { mode }, 'POST', signal)
    return {
      ...credential,
      userId: credential.userId ?? `anon_${credential.sessionId.replace(/-/g, '')}`,
      dynamicVariables: credential.dynamicVariables ?? {
        language: 'pl',
        visit_type: 'wywiad przed wizytą',
      },
    }
  }
  bindConversation(
    access: AgentAccess,
    sessionId: string,
    conversationId: string,
    signal?: AbortSignal,
  ) {
    return this.request<void>(
      `/api/interview-sessions/${encodeURIComponent(sessionId)}/provider-conversation`,
      access,
      { conversationId },
      'PUT',
      signal,
    )
  }
  endSession(access: AgentAccess, sessionId: string, continuesInterview = false) {
    return this.request<void>(
      `/api/interview-sessions/${encodeURIComponent(sessionId)}/end`,
      access,
      { continuesInterview },
      'POST',
    )
  }
  async getResult(access: AgentAccess, signal?: AbortSignal): Promise<AgentResult> {
    const path = `/api/interviews/${encodeURIComponent(access.interviewId)}/result`
    const result = await this.request<{
      status: AgentInterviewInfo['status']
      finalReport: string | null
      structuredDataJson: string | null
    }>(path, access, undefined, 'GET', signal)
    return {
      status: result.status,
      summary: result.finalReport,
      structuredData: result.structuredDataJson ? JSON.parse(result.structuredDataJson) : null,
    }
  }
}

export const agentApi = new AgentApi()
