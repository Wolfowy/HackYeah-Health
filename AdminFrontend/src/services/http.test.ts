import { test } from 'node:test'
import assert from 'node:assert/strict'
import { ApiError, StaffHttp } from './http'
import { createApiService, mapVisit, type AdminVisitDto } from './api'

const user = {
  id: 'staff',
  facilityId: 'facility',
  displayName: 'Lekarz',
  email: 'doctor@example.com',
  role: 'Clinician',
  clinicianId: 'clinician',
}
const tokens = (accessToken = 'access') => ({
  accessToken,
  refreshToken: 'refresh',
  accessTokenExpiresAt: '2099-01-01',
  user,
})
const json = (value: unknown, status = 200) =>
  new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
const dto: AdminVisitDto = {
  visitId: 'visit',
  externalVisitId: 'VIS-1',
  scheduledAt: '2026-10-04T10:00:00+02:00',
  status: 'NotStarted',
  deliveryStatus: 'NotSent',
  hasOpenSupplementationRound: false,
  visit: {
    serviceExpiresAt: '2026-10-04T23:59:00+02:00',
    doctor: { id: 'clinician', name: 'Lekarz', specialty: null },
    facility: { id: 'facility', name: null, address: null },
    room: null,
    visitType: 'InPerson',
  },
}

test('API mapping preserves visits without reports and does not invent patient metadata', () => {
  const visit = mapVisit(dto, 'facility')
  assert.equal(visit.patient.name, 'Wizyta VIS-1')
  assert.equal(visit.patient.phone, '')
  assert.equal(visit.durationMinutes, null)
  assert.equal(visit.reportAvailable, false)
  assert.equal(visit.assignedClinicianId, 'clinician')
})

test('concurrent unauthorized requests share one rotated refresh and retry with new bearer', async () => {
  let refreshCount = 0
  const client = new StaffHttp(async (url, init) => {
    if (String(url).endsWith('/auth/login')) return json(tokens())
    if (String(url).endsWith('/auth/refresh')) {
      refreshCount++
      await new Promise((resolve) => setTimeout(resolve, 10))
      return json(tokens('rotated'))
    }
    return new Headers(init?.headers).get('Authorization') === 'Bearer rotated'
      ? json({ ok: true })
      : json({}, 401)
  })
  await client.login('doctor@example.com', 'secret')
  const results = await Promise.all([client.request('/one'), client.request('/two')])
  assert.deepEqual(results, [{ ok: true }, { ok: true }])
  assert.equal(refreshCount, 1)
})

test('failed refresh requires login and never falls back to mock data', async () => {
  const client = new StaffHttp(async (url) =>
    String(url).endsWith('/auth/login') ? json(tokens()) : json({}, 401),
  )
  await client.login('doctor@example.com', 'secret')
  await assert.rejects(
    client.request('/integration/visits'),
    (err) => err instanceof ApiError && err.status === 401,
  )
  await assert.rejects(
    client.request('/integration/visits'),
    (err) => err instanceof ApiError && err.status === 401,
  )
})

test('adapter follows all pages, uses protected PDF and sends only questions for the authenticated clinician', async () => {
  const requests: { url: string; init?: RequestInit }[] = []
  const client = new StaffHttp(async (url, init) => {
    requests.push({ url: String(url), init })
    if (String(url).endsWith('/auth/login')) return json(tokens())
    if (String(url).includes('page=1')) return json({ items: [dto], total: 2 })
    if (String(url).includes('page=2'))
      return json({ items: [{ ...dto, visitId: 'second' }], total: 2 })
    if (String(url).endsWith('/pdf'))
      return new Response('%PDF-1.4', { headers: { 'Content-Type': 'application/pdf' } })
    return new Response(null, { status: 202 })
  })
  const service = createApiService(client)
  await service.signIn('doctor@example.com', 'secret')
  assert.equal((await service.getAppointments()).length, 2)
  assert.equal((await service.getReportPdf('visit', 'version')).type, 'application/pdf')
  await service.addQuestions('visit', ['Od kiedy występuje objaw?'])
  const last = requests.at(-1)!
  assert.deepEqual(JSON.parse(String(last.init?.body)), {
    questions: ['Od kiedy występuje objaw?'],
  })
  assert.equal(new Headers(last.init?.headers).get('Authorization'), 'Bearer access')
})

test('reception creation sends patient metadata and duration without triggering invitation delivery', async () => {
  let createBody: Record<string, unknown> = {}
  const appointment = {
    ...dto,
    serviceExpiresAt: dto.visit!.serviceExpiresAt,
    doctor: dto.visit!.doctor,
    facility: dto.visit!.facility,
    room: '01',
    visitType: 'InPerson',
    durationMinutes: 45,
    patient: { name: 'Jan Testowy', phone: '+48500100200', email: null },
    deliveryMode: 'demo',
    contactChannel: 'Sms',
    lastSentAt: null,
    version: 1,
  }
  const service = createApiService(
    new StaffHttp(async (url, init) => {
      if (String(url).endsWith('/auth/login'))
        return json({ ...tokens(), user: { ...user, role: 'Administrative' } })
      if (init?.method === 'POST') {
        createBody = JSON.parse(String(init.body))
        return json({ appointment, invitation: { url: 'http://127.0.0.1:5173/i/new-secret' } }, 201)
      }
      return json(appointment)
    }),
  )
  await service.signIn('admin@example.com', 'secret')
  const visit = await service.createAppointment({
    patientName: 'Jan Testowy',
    phone: '+48500100200',
    email: '',
    doctorId: 'clinician',
    scheduledAt: '2026-10-05T08:30:00+02:00',
    durationMinutes: 45,
    room: '01',
    visitType: 'InPerson',
  })
  assert.equal(createBody.pesel, null)
  assert.equal(createBody.phone, '+48500100200')
  assert.equal(createBody.patientName, 'Jan Testowy')
  assert.equal(createBody.durationMinutes, 45)
  assert.equal(createBody.sendInvitation, false)
  assert.equal(visit.invitationUrl, 'http://127.0.0.1:5173/i/new-secret')
  assert.equal(visit.durationMinutes, 45)
  assert.equal(visit.patient.name, 'Jan Testowy')
})

test('reception editing preserves instructions and supplies optimistic concurrency version', async () => {
  let updateBody: Record<string, unknown> = {}
  const appointment = {
    ...dto,
    serviceExpiresAt: dto.visit!.serviceExpiresAt,
    doctor: dto.visit!.doctor,
    facility: dto.visit!.facility,
    room: '01',
    visitType: 'InPerson',
    durationMinutes: 30,
    patient: null,
    deliveryMode: 'demo',
    contactChannel: 'Sms',
    lastSentAt: null,
    version: 7,
  }
  const service = createApiService(
    new StaffHttp(async (url, init) => {
      if (String(url).endsWith('/auth/login'))
        return json({ ...tokens(), user: { ...user, role: 'Administrative' } })
      if (String(url).endsWith('/status'))
        return json({ ...dto, visit: { ...dto.visit, locationInstructions: 'Wejście boczne' } })
      if (init?.method === 'PUT') {
        updateBody = JSON.parse(String(init.body))
        return json(appointment)
      }
      return json(appointment)
    }),
  )
  await service.signIn('admin@example.com', 'secret')
  const visit = await service.getAppointment('visit')
  await service.updateAppointment(visit, {
    scheduledAt: visit.scheduledAt,
    serviceExpiresAt: visit.serviceExpiresAt,
    room: '02',
    durationMinutes: 45,
  })
  assert.equal(updateBody.expectedVersion, 7)
  assert.equal(updateBody.locationInstructions, 'Wejście boczne')
  assert.equal(updateBody.durationMinutes, 45)
})
