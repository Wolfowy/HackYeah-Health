import type { ConversationMode, VisitDetails } from '../models'
import { AgentApiError, backendResponse } from './backend-http'
import {
  replaceDraftCommand,
  type ReportDraft,
  type PatientInterview,
  type ReportVersion,
  type ObservationDecision,
  type ReportIssue,
} from './patient-report'
export { AgentApiError } from './backend-http'

export interface AgentInterviewInfo {
  id: string
  displayName: string
  visitDate: string
  visit?: VisitDetails | null
  sessionCount?: number
  status: 'pending' | 'in_progress' | 'processing' | 'completed'
}
export interface AgentResult {
  status: AgentInterviewInfo['status']
  summary: string | null
  structuredData: Record<string, unknown> | null
  schemaVersion?: number | null
  extractionStatus?: string
  importStatus?: string
  issues?: ReportIssue[]
}

/** Completed transport alone does not mean that the editable draft has been imported. */
export function resultCanBeReviewed(result: AgentResult) {
  return (
    result.status === 'completed' &&
    result.extractionStatus !== 'pending' &&
    result.importStatus !== 'pending'
  )
}
export type AgentAccess =
  | { kind: 'anonymous'; accessToken: string; interviewId: string }
  | { kind: 'patient'; interviewId: string; sessionToken?: string }
export type AgentCredential = {
  sessionId: string
  provider: 'elevenlabs'
  userId: string
  dynamicVariables: Record<string, string | number | boolean>
  conversationId?: string
} & ({ mode: 'voice'; conversationToken: string } | { mode: 'text'; signedUrl: string })

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
    const response = await backendResponse(
      this.baseUrl,
      this.fetcher,
      path,
      this.token(access),
      body,
      method,
      signal,
    )
    if (response.status === 204 || response.status === 202) return undefined as T
    return response.json() as Promise<T>
  }

  private token(access?: AgentAccess) {
    return access?.kind === 'anonymous'
      ? access.accessToken
      : access?.kind === 'patient'
        ? (access.sessionToken ?? this.patientSessionToken)
        : ''
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
  async getVisitInterview(visitId: string, signal?: AbortSignal, sessionToken?: string) {
    const interview = await this.request<AgentInterviewInfo>(
      `/api/visits/${encodeURIComponent(visitId)}/interview`,
      { kind: 'patient', interviewId: '', sessionToken },
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
  private mapResult(result: {
    status: AgentInterviewInfo['status']
    finalReport: string | null
    structuredDataJson?: string | null
    structuredData?: Record<string, unknown> | null
    schemaVersion?: number | null
    extractionStatus?: string
    importStatus?: string
    issues?: ReportIssue[]
  }): AgentResult {
    let data = result.structuredData ?? null
    if (!data && result.structuredDataJson) {
      try {
        data = JSON.parse(result.structuredDataJson)
      } catch {
        /* Raw provider JSON is not necessarily valid. */
      }
    }
    return {
      status: result.status,
      summary: result.finalReport,
      structuredData: data,
      schemaVersion: result.schemaVersion,
      extractionStatus: result.extractionStatus,
      importStatus: result.importStatus,
      issues: result.issues ?? [],
    }
  }
  async getResult(access: AgentAccess, signal?: AbortSignal): Promise<AgentResult> {
    return this.mapResult(
      await this.request(
        `/api/interviews/${encodeURIComponent(access.interviewId)}/result`,
        access,
        undefined,
        'GET',
        signal,
      ),
    )
  }
  async retryImport(access: AgentAccess) {
    return this.mapResult(
      await this.request(
        `/api/interviews/${encodeURIComponent(access.interviewId)}/result/retry-import`,
        access,
        {},
        'POST',
      ),
    )
  }
  recoverSession(access: AgentAccess, sessionId: string) {
    return this.request<void>(
      `/api/interview-sessions/${encodeURIComponent(sessionId)}/recover`,
      access,
      {},
      'POST',
    )
  }
  getPatientInterview(access: AgentAccess, signal?: AbortSignal) {
    return this.request<PatientInterview>('/api/v1/interview', access, undefined, 'GET', signal)
  }
  saveDraft(access: AgentAccess, draft: ReportDraft) {
    return this.request<PatientInterview>(
      '/api/v1/interview/draft',
      access,
      replaceDraftCommand(draft),
      'PUT',
    )
  }
  decideObservation(
    access: AgentAccess,
    id: string,
    decision: ObservationDecision,
    editedText: string | null = null,
  ) {
    return this.request<PatientInterview>(
      `/api/v1/interview/observations/${encodeURIComponent(id)}/decision`,
      access,
      { decision, editedText },
      'PUT',
    )
  }
  completeInterview(access: AgentAccess) {
    return this.request<void>('/api/v1/interview/complete', access, {}, 'POST')
  }
  approveReport(access: AgentAccess, confirmIncompleteReport: boolean) {
    return this.request<ReportVersion>(
      '/api/v1/interview/approve',
      access,
      { confirmIncompleteReport, acceptAllObservations: true, shareWithFacility: true },
      'POST',
    )
  }
  setConsent(access: AgentAccess, granted: boolean) {
    return this.request<void>('/api/v1/interview/consent', access, { granted }, 'PUT')
  }
  regeneratePdf(access: AgentAccess) {
    return this.request<void>('/api/v1/interview/report/regenerate-pdf', access, {}, 'POST')
  }
  answerSupplementation(
    access: AgentAccess,
    answers: { questionId: string; answer: string; mode: 'Text' | 'Voice' }[],
  ) {
    return this.request<void>(
      '/api/v1/interview/supplementation-round/answers',
      access,
      answers,
      'POST',
    )
  }
  async transcribe(access: AgentAccess, audio: Blob, signal?: AbortSignal) {
    const body = new FormData()
    const extension =
      audio.type === 'audio/mp4' ? 'mp4' : audio.type === 'audio/ogg' ? 'ogg' : 'webm'
    body.append('audio', audio, `dopowiedzenie.${extension}`)
    const response = await backendResponse(
      this.baseUrl,
      this.fetcher,
      '/api/v1/interview/voice/transcribe',
      this.token(access),
      body,
      'POST',
      signal,
      60000,
    )
    return ((await response.json()) as { text: string }).text
  }
  async downloadReport(access: AgentAccess, format: 'pdf' | 'json') {
    const response = await backendResponse(
      this.baseUrl,
      this.fetcher,
      `/api/v1/interview/report${format === 'pdf' ? '.pdf' : ''}`,
      this.token(access),
    )
    return response.blob()
  }
}

export const agentApi = new AgentApi()
