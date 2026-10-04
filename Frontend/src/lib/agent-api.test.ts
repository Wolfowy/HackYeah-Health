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

test('typed result preserves failed extraction and malformed legacy JSON is safe', async () => {
  const api = new AgentApi('', (async () =>
    Response.json({
      status: 'completed',
      finalReport: 'Raport',
      structuredDataJson: '{broken',
      structuredData: null,
      schemaVersion: 1,
      extractionStatus: 'failed',
      importStatus: 'failed',
      issues: [{ fieldPath: 'symptoms', kind: 'missing', message: 'Brak danych' }],
    })) as typeof fetch)
  const result = await api.getResult({ kind: 'anonymous', accessToken: 'token', interviewId: 'id' })
  assert.equal(result.structuredData, null)
  assert.equal(result.importStatus, 'failed')
  assert.equal(result.issues?.[0].fieldPath, 'symptoms')
  const typed = new AgentApi('', (async () =>
    Response.json({
      status: 'completed',
      finalReport: null,
      structuredData: { schemaVersion: 1 },
      structuredDataJson: '{broken',
      extractionStatus: 'ready',
    })) as typeof fetch)
  assert.deepEqual(
    (await typed.getResult({ kind: 'anonymous', accessToken: 'token', interviewId: 'id' }))
      .structuredData,
    { schemaVersion: 1 },
  )
})

test('report approval accepts the whole report and grants sharing with the invitation JWT', async () => {
  const requests: { path: string; body: unknown; auth: string | null }[] = []
  const api = new AgentApi('', (async (url, options) => {
    requests.push({
      path: String(url),
      body: options?.body ? JSON.parse(String(options.body)) : null,
      auth: new Headers(options?.headers).get('Authorization'),
    })
    return String(url).endsWith('/approve')
      ? Response.json({ versionNumber: 1 })
      : new Response(null, { status: 204 })
  }) as typeof fetch)
  const access: AgentAccess = { kind: 'anonymous', interviewId: 'id', accessToken: 'review-jwt' }
  await api.approveReport(access, true)
  assert.deepEqual(
    requests.map((item) => item.path),
    ['/api/v1/interview/approve'],
  )
  assert.deepEqual(requests[0].body, {
    confirmIncompleteReport: true,
    acceptAllObservations: true,
    shareWithFacility: true,
  })
  assert.ok(requests.every((item) => item.auth === 'Bearer review-jwt'))
  await api.setConsent(access, false)
  assert.deepEqual(requests[1].body, { granted: false })
})

test('completed transport awaits extraction/import, while failed extraction opens an explicit review', async () => {
  const { resultCanBeReviewed } = await import('./agent-api')
  const result = { status: 'completed', summary: 'Opis', structuredData: null } as const
  assert.equal(
    resultCanBeReviewed({ ...result, extractionStatus: 'ready', importStatus: 'pending' }),
    false,
  )
  assert.equal(
    resultCanBeReviewed({ ...result, extractionStatus: 'pending', importStatus: 'ready' }),
    false,
  )
  assert.equal(
    resultCanBeReviewed({ ...result, extractionStatus: 'partial', importStatus: 'ready' }),
    true,
  )
  assert.equal(
    resultCanBeReviewed({ ...result, extractionStatus: 'failed', importStatus: 'failed' }),
    true,
  )
  assert.equal(resultCanBeReviewed(result), true)
  assert.equal(resultCanBeReviewed({ ...result, status: 'processing' }), false)
})
