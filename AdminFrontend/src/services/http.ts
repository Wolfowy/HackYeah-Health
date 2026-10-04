import type { StaffUser } from '../models'

interface Tokens {
  accessToken: string
  refreshToken: string
  accessTokenExpiresAt: string
  user: StaffUser
}

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message)
  }
}

/** Staff tokens live only in memory; refresh is rotated once for concurrent requests. */
export class StaffHttp {
  private tokens: Tokens | null = null
  private refreshing: Promise<void> | null = null
  private session = 0
  constructor(
    private fetcher: typeof fetch = fetch,
    private base = '/api/v1',
  ) {}

  async login(email: string, password: string) {
    const session = ++this.session
    this.tokens = null
    const tokens = await this.request<Tokens>(
      '/auth/login',
      { method: 'POST', body: JSON.stringify({ email, password }) },
      false,
    )
    if (session !== this.session) throw new ApiError(0, 'Sesja została zmieniona.')
    this.tokens = tokens
    return tokens.user
  }

  async logout() {
    try {
      if (this.tokens)
        await this.request('/auth/logout', {
          method: 'POST',
          body: JSON.stringify({ refreshToken: this.tokens.refreshToken }),
        })
    } finally {
      this.session++
      this.tokens = null
    }
  }

  private async refresh() {
    if (!this.refreshing) {
      const session = this.session
      this.refreshing = (async () => {
        const token = this.tokens?.refreshToken
        if (!token) throw new ApiError(401, 'Sesja wygasła. Zaloguj się ponownie.')
        try {
          const tokens = await this.request<Tokens>(
            '/auth/refresh',
            { method: 'POST', body: JSON.stringify({ refreshToken: token }) },
            false,
          )
          if (session !== this.session) throw new ApiError(0, 'Sesja została zmieniona.')
          this.tokens = tokens
        } catch {
          if (session !== this.session) throw new ApiError(0, 'Sesja została zmieniona.')
          this.tokens = null
          throw new ApiError(401, 'Sesja wygasła. Zaloguj się ponownie.')
        }
      })().finally(() => {
        this.refreshing = null
      })
    }
    await this.refreshing
  }

  async request<T = void>(
    path: string,
    init: RequestInit = {},
    authenticated = true,
    retry = true,
    blob = false,
  ): Promise<T> {
    if (authenticated && !this.tokens) throw new ApiError(401, 'Zaloguj się ponownie.')
    const session = this.session
    const headers = new Headers(init.headers)
    if (init.body) headers.set('Content-Type', 'application/json')
    if (authenticated && this.tokens)
      headers.set('Authorization', `Bearer ${this.tokens.accessToken}`)
    let response: Response
    try {
      const fetchRequest = this.fetcher
      response = await fetchRequest(this.base + path, {
        ...init,
        headers,
        cache: 'no-store',
        signal: AbortSignal.timeout(15000),
      })
    } catch {
      throw new ApiError(
        0,
        'Nie można połączyć się z serwerem. Sprawdź połączenie i spróbuj ponownie.',
      )
    }
    if (authenticated && session !== this.session) throw new ApiError(0, 'Sesja została zmieniona.')
    if (response.status === 401 && authenticated && retry) {
      await this.refresh()
      return this.request<T>(path, init, authenticated, false, blob)
    }
    if (!response.ok) {
      const codes: Record<string, string> = {
        'calendar.clinician_conflict': 'Lekarz ma już wizytę w tym terminie. Wybierz inną godzinę.',
        'calendar.room_conflict':
          'Gabinet jest zajęty w tym terminie. Wybierz inny gabinet lub godzinę.',
        'visit.version_conflict':
          'Wizyta została zmieniona przez innego pracownika. Odśwież status przed ponowną edycją.',
        'clinician.unavailable': 'Lekarz jest nieaktywny lub niedostępny. Wybierz innego lekarza.',
      }
      let code = ''
      try {
        code = (await response.json()).code || ''
      } catch {
        /* A proxy may return an empty error body. */
      }
      const messages: Record<number, string> = {
        400: 'Sprawdź wymagane dane i termin wizyty.',
        401: authenticated
          ? 'Sesja wygasła. Zaloguj się ponownie.'
          : 'Nieprawidłowy e-mail lub hasło.',
        403: 'Brak dostępu. Raport wymaga zgody pacjenta i przypisania do lekarza.',
        404: 'Wizyta lub zatwierdzony raport nie są dostępne.',
        409: 'Operacja nie jest możliwa w obecnym stanie wizyty. Odśwież dane.',
        429: 'Zbyt wiele prób. Spróbuj ponownie za kilka minut.',
      }
      // Do not render arbitrary server diagnostics or patient data in an error notification.
      throw new ApiError(
        response.status,
        codes[code] ||
          messages[response.status] ||
          'Serwer nie mógł wykonać operacji. Spróbuj ponownie.',
      )
    }
    if (response.status === 204 || response.status === 202) return undefined as T
    return (blob ? await response.blob() : await response.json()) as T
  }
}
