import assert from 'node:assert/strict'
import { test } from 'node:test'
import { AgentApi, AgentApiError, type AgentAccess } from './agent-api'

test('anonymous authorization validates invitation first and stops at invalid links', async () => {
  const requests: string[] = []
  const api = new AgentApi('', (async (url) => {
    requests.push(String(url))
    return new Response('{}', { status: 410 })
  }) as typeof fetch)
  await assert.rejects(
    api.authorizeInvitation('invalid/token'),
    (error: unknown) => error instanceof AgentApiError && error.status === 410,
  )
  assert.deepEqual(requests, ['/api/public/interviews/invalid%2Ftoken'])
})

test('voice and text credentials use scoped bearer auth and send only the chosen mode', async () => {
  const requests: { url: string; options?: RequestInit }[] = []
  const api = new AgentApi('http://api.example/', (async (url, options) => {
    requests.push({ url: String(url), options })
    const mode = JSON.parse(options?.body as string).mode
    return Response.json({
      mode,
      sessionId: 'session',
      provider: 'elevenlabs',
      ...(mode === 'voice' ? { conversationToken: 'temporary' } : { signedUrl: 'wss://temporary' }),
    })
  }) as typeof fetch)
  const access: AgentAccess = {
    kind: 'anonymous',
    accessToken: 'only-this-interview',
    interviewId: 'interview-1',
  }
  assert.equal((await api.startSession(access, 'voice')).mode, 'voice')
  assert.equal((await api.startSession(access, 'text')).mode, 'text')
  for (const { url, options } of requests) {
    assert.equal(url, 'http://api.example/api/interviews/interview-1/sessions')
    assert.equal(options?.method, 'POST')
    assert.equal(new Headers(options?.headers).get('Authorization'), 'Bearer only-this-interview')
    assert.equal(new Headers(options?.headers).get('xi-api-key'), null)
    assert.deepEqual(Object.keys(JSON.parse(options?.body as string)), ['mode'])
  }
})

test('patient access uses its own interview and scoped session; binding and continuation are explicit', async () => {
  const requests: { url: string; options?: RequestInit }[] = []
  const api = new AgentApi(
    '',
    (async (url, options) => {
      requests.push({ url: String(url), options })
      return String(url).endsWith('/result')
        ? Response.json({
            status: 'completed',
            finalReport: 'Raport z backendu',
            structuredDataJson: '{"reason":"ból"}',
          })
        : new Response(null, { status: 204 })
    }) as typeof fetch,
    'patient-session-token',
  )
  const access: AgentAccess = { kind: 'patient', interviewId: 'patient-interview' }
  await api.bindConversation(access, 'session-1', 'conv-1')
  await api.endSession(access, 'session-1', true)
  const result = await api.getResult(access)
  assert.equal(result.summary, 'Raport z backendu')
  assert.deepEqual(result.structuredData, { reason: 'ból' })
  assert.deepEqual(JSON.parse(requests[0].options?.body as string), { conversationId: 'conv-1' })
  assert.deepEqual(JSON.parse(requests[1].options?.body as string), { continuesInterview: true })
  assert.equal(requests[2].url, '/api/interviews/patient-interview/result')
  assert.equal(new Headers(requests[0].options?.headers).get('X-Patient-Id'), null)
  assert.equal(
    new Headers(requests[0].options?.headers).get('Authorization'),
    'Bearer patient-session-token',
  )
})
