import assert from 'node:assert/strict'
import { test } from 'node:test'
import { AgentApi } from './agent-api'
import { AgentSession } from './agent-session'
const access = {
  kind: 'anonymous',
  accessToken: 'scoped-token',
  interviewId: 'interview-1',
} as const

test('SDK connection binds once and ending waits for correlation; duplicate cleanup keeps the original intent', async () => {
  const requests: { path: string; body: unknown }[] = []
  let releaseBinding!: () => void
  const bindingGate = new Promise<void>((resolve) => {
    releaseBinding = resolve
  })
  const api = new AgentApi('', (async (url, init) => {
    requests.push({ path: String(url), body: JSON.parse(String(init?.body)) })
    if (String(url).endsWith('/provider-conversation')) await bindingGate
    return new Response(null, { status: 204 })
  }) as typeof fetch)
  const session = new AgentSession(api, access, 'session-1')
  const binding = session.bind('conv-1')
  assert.equal(session.bind('conv-1'), binding)
  const ending = session.end(true)
  assert.equal(session.end(false), ending)
  assert.equal(requests.length, 1)
  releaseBinding()
  await ending
  assert.deepEqual(
    requests.map((item) => item.body),
    [{ conversationId: 'conv-1' }, { continuesInterview: true }],
  )
  await session.end(false)
  assert.equal(requests.length, 2)
  await assert.rejects(session.bind('different-conversation'))
  assert.equal(requests.length, 2)
})

test('cancelling before connection ends the unused transport as a continuation', async () => {
  const bodies: unknown[] = []
  const api = new AgentApi('', (async (_url, init) => {
    bodies.push(JSON.parse(String(init?.body)))
    return new Response(null, { status: 204 })
  }) as typeof fetch)
  const session = new AgentSession(api, access, 'unused-session')
  await session.end(true)
  await session.end()
  assert.deepEqual(bodies, [{ continuesInterview: true }])
})
