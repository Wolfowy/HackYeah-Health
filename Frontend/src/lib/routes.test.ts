import assert from 'node:assert/strict'
import { test } from 'node:test'
import { standaloneRoute } from './routes'

test('routes recognize guest invitations and patient visits without capturing workspace routes', () => {
  assert.deepEqual(standaloneRoute('/i/token_123'), { kind: 'invitation', token: 'token_123' })
  assert.deepEqual(standaloneRoute('/', '#/i/token_123'), {
    kind: 'invitation',
    token: 'token_123',
  })
  assert.deepEqual(standaloneRoute('/visits/visit-1/interview'), {
    kind: 'patient',
    visitId: 'visit-1',
  })
  assert.equal(standaloneRoute('/', '#/appointments'), null)
  assert.equal(standaloneRoute('/i/token/extra'), null)
  assert.deepEqual(standaloneRoute('/i/%E0%A4%A'), { kind: 'expired_session' })
  assert.deepEqual(standaloneRoute('/rozmowa'), { kind: 'expired_session' })
})
