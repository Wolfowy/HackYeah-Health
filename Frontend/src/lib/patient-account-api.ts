import type { VisitDetails } from '../models'
import type { AgentAccess } from './agent-api'
import { AgentApiError, backendResponse } from './backend-http'

export interface AccountUser {
  id: string
  email: string
  displayName: string
  avatarUrl: string | null
}
interface AccountTokens {
  accessToken: string
  refreshToken: string
  accessTokenExpiresAt: string
  user: AccountUser
}
export interface AccountVisit extends VisitDetails {
  id: string
  status: string
  interviewId: string
  interviewStatus: 'pending' | 'in_progress' | 'processing' | 'completed'
}

/** Separate account JWT and per-visit sessions. Credentials exist only for this tab's lifetime. */
export class PatientAccountApi {
  private auth: AccountTokens | null = null
  private refreshing: Promise<void> | null = null
  private generation = 0
  constructor(
    private baseUrl = import.meta.env?.VITE_API_BASE_URL ?? '',
    private fetcher: typeof fetch = globalThis.fetch.bind(globalThis),
  ) {}
  get user() {
    return this.auth?.user ?? null
  }
  private async publicRequest<T>(path: string, body: unknown, token = '') {
    const response = await backendResponse(this.baseUrl, this.fetcher, path, token, body, 'POST')
    return response.json() as Promise<T>
  }
  private accept(tokens: AccountTokens) {
    this.generation++
    this.auth = tokens
    return tokens.user
  }
  async login(email: string, password: string) {
    return this.accept(
      await this.publicRequest<AccountTokens>('/api/v1/patient-account/auth/login', {
        email,
        password,
      }),
    )
  }
  async register(access: AgentAccess, email: string, password: string, displayName: string) {
    const token = access.kind === 'anonymous' ? access.accessToken : access.sessionToken
    if (!token) throw new AgentApiError(401)
    return this.accept(
      await this.publicRequest<AccountTokens>(
        '/api/v1/patient-account/auth/register',
        { email, password, displayName },
        token,
      ),
    )
  }
  private async refresh() {
    if (!this.auth) throw new AgentApiError(401, 'patient_account.invalid_token')
    if (!this.refreshing) {
      const current = this.generation
      const refreshToken = this.auth.refreshToken
      this.refreshing = this.publicRequest<AccountTokens>('/api/v1/patient-account/auth/refresh', {
        refreshToken,
      })
        .then((tokens) => {
          if (current !== this.generation)
            throw new AgentApiError(401, 'patient_account.invalid_token')
          this.auth = tokens
        })
        .catch((error) => {
          if (current === this.generation) this.auth = null
          throw error
        })
        .finally(() => {
          this.refreshing = null
        })
    }
    await this.refreshing
  }
  private async request<T>(path: string, body?: unknown, method = 'GET') {
    if (!this.auth) throw new AgentApiError(401, 'patient_account.invalid_token')
    if (Date.parse(this.auth.accessTokenExpiresAt) <= Date.now() + 30000) await this.refresh()
    const current = this.generation
    let response: Response
    try {
      response = await backendResponse(
        this.baseUrl,
        this.fetcher,
        `/api/v1/patient-account${path}`,
        this.auth?.accessToken,
        body,
        method,
      )
    } catch (cause) {
      if (!(cause instanceof AgentApiError) || cause.status !== 401 || current !== this.generation)
        throw cause
      await this.refresh()
      response = await backendResponse(
        this.baseUrl,
        this.fetcher,
        `/api/v1/patient-account${path}`,
        this.auth?.accessToken,
        body,
        method,
      )
    }
    if (current !== this.generation) throw new AgentApiError(401, 'patient_account.invalid_token')
    return response.status === 204 ? (undefined as T) : (response.json() as Promise<T>)
  }
  me() {
    return this.request<AccountUser>('/me')
  }
  async updateProfile(displayName: string, avatarUrl: string | null) {
    const user = await this.request<AccountUser>('/me', { displayName, avatarUrl }, 'PUT')
    if (this.auth) this.auth = { ...this.auth, user }
    return user
  }
  visits() {
    return this.request<{ visit: AccountVisit }[]>('/visits')
  }
  createVisitSession(id: string) {
    return this.request<{ sessionToken: string; visitId: string; expiresAt: string }>(
      `/visits/${encodeURIComponent(id)}/session`,
      {},
      'POST',
    )
  }
  async logout() {
    try {
      if (this.auth) {
        if (Date.parse(this.auth.accessTokenExpiresAt) <= Date.now() + 30000) await this.refresh()
        await backendResponse(
          this.baseUrl,
          this.fetcher,
          '/api/v1/patient-account/logout',
          this.auth?.accessToken,
          { refreshToken: this.auth?.refreshToken },
          'POST',
        )
      }
    } finally {
      this.generation++
      this.auth = null
    }
  }
}
export const patientAccountApi = new PatientAccountApi()
