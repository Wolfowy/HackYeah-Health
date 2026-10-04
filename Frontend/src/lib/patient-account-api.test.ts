import assert from 'node:assert/strict'
import { test } from 'node:test'
import { PatientAccountApi } from './patient-account-api'
const user = {
  id: 'user-1',
  email: 'test@example.com',
  displayName: 'Pacjent Testowy',
  avatarUrl: null,
}

test('concurrent calls rotate refresh once, visit sessions are separate and logout revokes the new token', async () => {
  const requests: { url: string; init?: RequestInit }[] = []
  const api = new PatientAccountApi('', (async (url, init) => {
    requests.push({ url: String(url), init })
    if (String(url).endsWith('/login'))
      return Response.json({
        accessToken: 'expired',
        refreshToken: 'old-refresh',
        accessTokenExpiresAt: '2000-01-01T00:00:00Z',
        user,
      })
    if (String(url).endsWith('/refresh'))
      return Response.json({
        accessToken: 'account-jwt',
        refreshToken: 'new-refresh',
        accessTokenExpiresAt: '2099-01-01T00:00:00Z',
        user,
      })
    if (String(url).endsWith('/me')) return Response.json(user)
    if (String(url).endsWith('/visits')) return Response.json([])
    if (String(url).endsWith('/session'))
      return Response.json({
        sessionToken: 'only-visit',
        visitId: 'visit-1',
        expiresAt: '2099-01-01',
      })
    return new Response(null, { status: 204 })
  }) as typeof fetch)
  await api.login('test@example.com', 'Password12345')
  await Promise.all([api.me(), api.visits()])
  assert.equal(requests.filter((item) => item.url.endsWith('/refresh')).length, 1)
  assert.deepEqual(
    JSON.parse(String(requests.find((item) => item.url.endsWith('/refresh'))?.init?.body)),
    { refreshToken: 'old-refresh' },
  )
  assert.equal((await api.createVisitSession('visit-1')).sessionToken, 'only-visit')
  for (const item of requests.filter((item) => !item.url.includes('/auth/')))
    assert.equal(new Headers(item.init?.headers).get('Authorization'), 'Bearer account-jwt')
  await api.logout()
  assert.deepEqual(JSON.parse(String(requests.at(-1)?.init?.body)), { refreshToken: 'new-refresh' })
  assert.equal(api.user, null)
  await assert.rejects(api.visits())
})

test('registration requires verified visit access and never sends a staff API key', async () => {
  let headers: Headers | undefined
  const api = new PatientAccountApi('', (async (_url, init) => {
    headers = new Headers(init?.headers)
    return Response.json({
      accessToken: 'account',
      refreshToken: 'refresh',
      accessTokenExpiresAt: '2099-01-01T00:00:00Z',
      user,
    })
  }) as typeof fetch)
  await api.register(
    { kind: 'anonymous', interviewId: 'visit-interview', accessToken: 'visit-jwt' },
    user.email,
    'Password12345',
    user.displayName,
  )
  assert.equal(headers?.get('Authorization'), 'Bearer visit-jwt')
  assert.equal(headers?.get('X-Api-Key'), null)
  await assert.rejects(
    api.register({ kind: 'patient', interviewId: 'id' }, 'x@example.com', 'Password12345', 'Name'),
  )
})
