export type StandaloneRoute =
  | { kind: 'invitation'; token: string }
  | { kind: 'patient'; visitId: string }
  | { kind: 'expired_session' }
export function standaloneRoute(pathname: string, hash = ''): StandaloneRoute | null {
  const path = hash.startsWith('#/i/') ? hash.slice(1) : pathname
  const invitation = /^\/i\/([^/]+)\/?$/.exec(path)
  if (invitation) {
    try {
      return { kind: 'invitation', token: decodeURIComponent(invitation[1]) }
    } catch {
      return { kind: 'expired_session' }
    }
  }
  const patient = /^\/visits\/([^/]+)\/interview\/?$/.exec(path)
  if (patient) return { kind: 'patient', visitId: patient[1] }
  return path === '/rozmowa' ? { kind: 'expired_session' } : null
}
