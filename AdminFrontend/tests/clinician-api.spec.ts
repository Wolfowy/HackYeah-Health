import { test, expect, type Page } from '@playwright/test'
import { createMockAppointments } from '../src/data/mock'

const visitId = '22222222-2222-2222-2222-222222222222'
const reportId = '33333333-3333-3333-3333-333333333333'
const report = {
  ...createMockAppointments().find((v) => v.report)!.report!,
  visitId,
  versionId: reportId,
  versionNumber: 2,
  approvedAt: '2026-10-03T18:30:00+02:00',
}
const detail = {
  id: visitId,
  externalVisitId: 'WIZ-API-1',
  scheduledAt: '2026-10-04T08:30:00+02:00',
  timeZone: 'Europe/Warsaw',
  serviceExpiresAt: '2026-10-04T23:59:00+02:00',
  doctor: { id: 'doctor-demo', name: 'dr Anna Kowalska', specialty: 'Internista' },
  facility: {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'Przychodnia API',
    address: 'Warszawa',
  },
  room: '01',
  visitType: 'InPerson',
  locationInstructions: 'Wejście od ulicy',
  status: 'Shared',
  interviewId: 'interview',
  interviewStatus: 'Completed',
}
const apiVisit = {
  visitId,
  externalVisitId: detail.externalVisitId,
  scheduledAt: detail.scheduledAt,
  status: 'Shared',
  deliveryStatus: 'Delivered',
  hasOpenSupplementationRound: false,
  visit: detail,
}

const receptionVisit = {
  ...apiVisit,
  serviceExpiresAt: detail.serviceExpiresAt,
  durationMinutes: 30,
  doctor: { ...detail.doctor, defaultRoom: '01', isActive: true },
  facility: detail.facility,
  room: '01',
  visitType: 'InPerson',
  patient: { name: 'Jan Testowy', phone: '+48500100200', email: 'jan@example.invalid' },
  deliveryMode: 'demo',
  lastSentAt: null,
  version: 3,
  contactChannel: 'Sms',
}

test.beforeEach(async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-04T06:00:00+02:00') })
})

async function api(
  page: Page,
  role: 'Administrative' | 'Clinician',
  options: { forbidden?: boolean; refreshFail?: boolean } = {},
) {
  const calls: { path: string; method: string; body: unknown }[] = []
  let current = structuredClone(apiVisit)
  let expired = false
  let adminVisit = structuredClone(receptionVisit)
  let invitation = 'http://127.0.0.1:5173/i/existing-token'
  await page.route('**/api/v1/**', async (route) => {
    const req = route.request()
    const path = new URL(req.url()).pathname
    const method = req.method()
    const body = req.postData() ? req.postDataJSON() : null
    calls.push({ path, method, body })
    const send = (value: unknown, status = 200) =>
      route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) })
    if (path.endsWith('/auth/login') || path.endsWith('/auth/refresh')) {
      if (path.endsWith('/refresh') && options.refreshFail) return send({}, 401)
      return send({
        accessToken: 'staff-token',
        refreshToken: 'refresh-secret',
        accessTokenExpiresAt: '2026-10-04T09:00:00Z',
        user: {
          id: 'staff',
          facilityId: detail.facility.id,
          email: role === 'Clinician' ? 'doctor@docprep.local' : 'admin@docprep.local',
          displayName: role === 'Clinician' ? 'dr Anna Kowalska' : 'Anna Nowak',
          role,
          clinicianId: role === 'Clinician' ? 'doctor-demo' : null,
        },
      })
    }
    if (expired) return send({}, 401)
    if (path.endsWith('/auth/logout')) return route.fulfill({ status: 204 })
    if (path.endsWith('/admin/facility')) return send(detail.facility)
    if (path.endsWith('/admin/doctors')) return send([receptionVisit.doctor])
    if (path.includes('/admin/appointments')) {
      if (path.endsWith('/invitation/regenerate')) {
        invitation = 'http://127.0.0.1:5173/i/fresh-invitation-token'
        return send({ url: invitation })
      }
      if (path.endsWith('/invitation/send')) {
        adminVisit.deliveryStatus = 'Delivered'
        return send(adminVisit)
      }
      if (path.endsWith('/invitation')) return send({ url: invitation })
      if (path.endsWith('/cancel')) {
        adminVisit.status = 'Cancelled'
        return route.fulfill({ status: 204 })
      }
      if (method === 'POST') {
        adminVisit = {
          ...adminVisit,
          visitId: 'createdvisit',
          externalVisitId: 'WIZ-API-NEW',
          patient: { ...adminVisit.patient, name: body.patientName },
          scheduledAt: body.scheduledAt,
          status: 'NotStarted',
          durationMinutes: body.durationMinutes,
        }
        return send({ appointment: adminVisit, invitation: { url: invitation } }, 201)
      }
      if (method === 'PUT') {
        adminVisit = {
          ...adminVisit,
          scheduledAt: body.scheduledAt,
          durationMinutes: body.durationMinutes,
          serviceExpiresAt: body.serviceExpiresAt,
          room: body.room,
          version: adminVisit.version + 1,
        }
        return send(adminVisit)
      }
      if (path.endsWith('/appointments'))
        return send({ items: [adminVisit], total: 1, page: 1, pageSize: 100 })
      return send(adminVisit)
    }
    if (path.endsWith('/report-versions'))
      return send(
        options.forbidden
          ? {}
          : [
              {
                versionId: reportId,
                versionNumber: 2,
                approvedAt: report.approvedAt,
                confirmedIncomplete: false,
              },
              {
                versionId: 'older',
                versionNumber: 1,
                approvedAt: report.approvedAt,
                confirmedIncomplete: false,
              },
            ],
        options.forbidden ? 403 : 200,
      )
    if (path.endsWith('/pdf'))
      return route.fulfill({ status: 200, contentType: 'application/pdf', body: '%PDF-1.4\n%%EOF' })
    if (path.includes('/report-versions/'))
      return send({
        report: path.endsWith('/older')
          ? { ...report, versionId: 'older', versionNumber: 1 }
          : report,
        availableEvidence: [],
      })
    if (path.endsWith('/questions')) {
      current.status = 'RequiresSupplementation'
      current.hasOpenSupplementationRound = true
      return route.fulfill({ status: 202 })
    }
    if (path.endsWith('/cancel')) {
      current.status = 'Cancelled'
      return route.fulfill({ status: 204 })
    }
    if (path.endsWith('/invitations'))
      return send({
        visitId,
        interviewInvitationToken: 'fresh-invitation-token',
        deliveryStatus: 'Pending',
      })
    if (path.endsWith('/status')) return send(current)
    if (method === 'PUT') {
      current = {
        ...current,
        scheduledAt: body.scheduledAt,
        visit: {
          ...current.visit,
          scheduledAt: body.scheduledAt,
          serviceExpiresAt: body.serviceExpiresAt,
          room: body.room,
        },
      }
      return send(current)
    }
    if (method === 'POST') {
      current = {
        ...current,
        status: 'NotStarted',
        externalVisitId: body.externalVisitId,
        scheduledAt: body.scheduledAt,
        visit: {
          ...detail,
          scheduledAt: body.scheduledAt,
          serviceExpiresAt: body.serviceExpiresAt,
        },
      }
      return send(
        { visitId, interviewInvitationToken: 'created-token', deliveryStatus: 'Pending' },
        201,
      )
    }
    return send({
      items: [
        current,
        {
          ...apiVisit,
          visitId: 'second',
          externalVisitId: 'WIZ-API-2',
          scheduledAt: '2026-10-04T09:30:00+02:00',
          status: 'NotStarted',
        },
        {
          ...apiVisit,
          visitId: 'other-doctor',
          externalVisitId: 'INNY-LEKARZ',
          scheduledAt: '2026-10-04T09:00:00+02:00',
          visit: { ...detail, doctor: { ...detail.doctor, id: 'other' } },
        },
      ],
      page: 1,
      pageSize: 100,
      total: 3,
    })
  })
  await page.goto('/')
  await page
    .getByLabel('Adres e-mail')
    .fill(role === 'Clinician' ? 'doctor@docprep.local' : 'admin@docprep.local')
  await page.getByLabel('Hasło', { exact: true }).fill('DocPrepDemo!2026')
  await page.getByRole('button', { name: 'Zaloguj się', exact: true }).click()
  await expect(
    page.getByRole('heading', {
      name: role === 'Clinician' ? 'Pacjenci na dziś.' : 'Kalendarz wizyt.',
    }),
  ).toBeVisible()
  return {
    calls,
    expire: () => {
      expired = true
    },
  }
}

test('doctor daily queue includes visits without reports, isolates clinician, and opens summary, versions, PDF and questions', async ({
  page,
}) => {
  const { calls } = await api(page, 'Clinician')
  await expect(page.getByTestId('doctor-visit')).toHaveCount(2)
  await expect(page.getByTestId('doctor-visit').nth(0)).toContainText('08:30')
  await expect(page.getByTestId('doctor-visit').nth(1)).toContainText('09:30')
  await expect(page.getByText('INNY-LEKARZ')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Nowa wizyta', exact: true })).toHaveCount(0)
  await page.getByRole('button', { name: 'Szczegóły Wizyta WIZ-API-2', exact: true }).click()
  await page.getByRole('tab', { name: /Raport/ }).click()
  await expect(page.getByRole('heading', { name: 'Raport jeszcze nie jest gotowy' })).toBeVisible()
  await page.getByRole('button', { name: 'Zamknij szczegóły', exact: true }).click()
  await page.getByRole('button', { name: 'Otwórz raport Wizyta WIZ-API-1', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Podsumowanie rozmowy' })).toBeVisible()
  await expect(page.locator('.conversation-summary')).toContainText(report.consultationReason)
  await expect(page.getByText('Przykładowy raport pacjenta')).toHaveCount(0)
  await page.getByRole('combobox', { name: 'Wersja raportu' }).click()
  await page.getByTitle(/Wersja 1/).click()
  await expect(page.locator('.report-banner')).toContainText('Wersja 1')
  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Pobierz PDF' }).click()
  expect((await download).suggestedFilename()).toContain('-v1.pdf')
  await page
    .getByRole('textbox', { name: 'Pytania uzupełniające' })
    .fill('Od kiedy występuje objaw?\nJak często się pojawia?')
  await page.getByRole('button', { name: 'Wyślij pytania', exact: true }).click()
  await page.getByRole('button', { name: 'Wyślij do pacjenta', exact: true }).click()
  await expect(page.getByText('Wysłano pytania do uzupełnienia przez pacjenta.')).toBeVisible()
  expect(calls.find((c) => c.path.endsWith('/questions'))?.body).toEqual({
    questions: ['Od kiedy występuje objaw?', 'Jak często się pojawia?'],
  })
  await page.getByRole('button', { name: 'Zamknij szczegóły', exact: true }).click()
  await expect(page.getByTestId('doctor-visit').first()).toContainText('Do uzupełnienia')
  expect(await page.evaluate(() => JSON.stringify(localStorage))).not.toContain('staff-token')
  expect(await page.evaluate(() => JSON.stringify(sessionStorage))).not.toContain('refresh-secret')
})

test('administrative account sees status and sends regenerated invitation without requesting clinical content', async ({
  page,
}) => {
  const { calls } = await api(page, 'Administrative')
  await page.getByRole('menuitem', { name: /Wizyty/ }).click()
  await page.getByRole('button', { name: 'Otwórz wizytę Jan Testowy', exact: true }).click()
  await page.getByRole('tab', { name: /Raport/ }).click()
  await expect(page.getByText('Raport jest dostępny dla przypisanego lekarza')).toBeVisible()
  expect(calls.some((c) => c.path.includes('report-versions'))).toBe(false)
  await page.getByRole('tab', { name: 'Szczegóły', exact: true }).click()
  await expect(page.getByLabel('Link dla pacjenta')).toHaveValue(
    'http://127.0.0.1:5173/i/existing-token',
  )
  await expect(
    page.getByText('Operator działa w trybie demo — wiadomości nie są wysyłane.'),
  ).toBeVisible()
  await page.getByRole('button', { name: 'Ponów zaproszenie', exact: true }).click()
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Wyślij zaproszenie', exact: true })
    .click()
  await expect(page.getByLabel('Link dla pacjenta')).toHaveValue(
    'http://127.0.0.1:5173/i/existing-token',
  )
  expect(calls.find((c) => c.path.endsWith('/invitation/send'))?.body).toEqual({ channel: 'Sms' })
  await page.getByRole('button', { name: 'Utwórz nowy link', exact: true }).click()
  await page
    .getByRole('dialog', { name: 'Utwórz nowy link do wywiadu' })
    .getByRole('button', { name: 'Utwórz nowy link', exact: true })
    .click()
  await expect(page.getByLabel('Link dla pacjenta')).toHaveValue(
    'http://127.0.0.1:5173/i/fresh-invitation-token',
  )
  expect(calls.find((c) => c.path.endsWith('/invitation/regenerate'))?.body).toEqual({
    channel: 'Sms',
  })
  await page.getByRole('button', { name: 'Anuluj wizytę', exact: true }).click()
  await page.getByRole('button', { name: 'Potwierdź anulowanie', exact: true }).click()
  await expect(page.getByText('Wizyta anulowana — zaproszenie jest nieaktywne.')).toBeVisible()
})

test('forbidden report clears clinical content; expired staff session returns to login', async ({
  page,
}) => {
  const { expire } = await api(page, 'Clinician', { forbidden: true, refreshFail: true })
  await page.getByRole('button', { name: 'Otwórz raport Wizyta WIZ-API-1', exact: true }).click()
  await expect(
    page.getByText('Brak dostępu. Raport wymaga zgody pacjenta i przypisania do lekarza.'),
  ).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Podsumowanie rozmowy' })).toHaveCount(0)
  await page.getByRole('button', { name: 'Zamknij szczegóły', exact: true }).click()
  expire()
  await page.getByRole('button', { name: 'Odśwież wizyty', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Witaj w panelu przychodni' })).toBeVisible()
  await expect(page.getByTestId('doctor-visit')).toHaveCount(0)
})

test('doctor demo works on today, with a summary and no API requests', async ({ page }) => {
  const requests: string[] = []
  page.on('request', (r) => {
    if (r.url().includes('/api/')) requests.push(r.url())
  })
  await page.goto('/')
  await page.getByRole('button', { name: 'Otwórz demo lekarza', exact: true }).click()
  await expect(page.getByTestId('doctor-visit')).toHaveCount(4)
  await page.getByRole('button', { name: 'Otwórz raport Jan Malinowski', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Podsumowanie rozmowy' })).toBeVisible()
  expect(requests).toEqual([])
  await page.getByRole('button', { name: 'Zamknij szczegóły', exact: true }).click()
  await page.screenshot({
    path: `test-results/doctor-${test.info().project.name}.png`,
    fullPage: true,
  })
  const width = await page.evaluate(() => ({
    viewport: innerWidth,
    page: document.documentElement.scrollWidth,
  }))
  expect(width.page).toBeLessThanOrEqual(width.viewport + 1)
})

test('reception creates and edits a reservation using patient metadata, duration and optimistic version', async ({
  page,
}) => {
  const { calls } = await api(page, 'Administrative')
  await page.getByRole('button', { name: /Nowa wizyta/ }).click()
  await page.getByLabel('Imię i nazwisko pacjenta').fill('Pacjent API')
  await page.getByLabel('Telefon (do SMS)').fill('+48500100200')
  await page.getByLabel('Lekarz', { exact: true }).click()
  await page.getByTitle('dr Anna Kowalska · Internista', { exact: true }).click()
  await page.getByLabel('Godzina', { exact: true }).fill('17:00')
  await page.getByLabel('Godzina', { exact: true }).press('Tab')
  await page.getByRole('button', { name: 'Dodaj wizytę', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Pacjent API' })).toBeVisible()
  const create = calls.find((c) => c.path === '/api/v1/admin/appointments' && c.method === 'POST')!
    .body as Record<string, unknown>
  expect(create.patientName).toBe('Pacjent API')
  expect(create.durationMinutes).toBe(30)
  expect(create.sendInvitation).toBe(false)
  expect(create.pesel).toBeNull()
  await page.getByRole('button', { name: 'Zmień termin', exact: true }).click()
  await page.getByLabel('Czas wizyty (min)', { exact: true }).fill('45')
  await page.getByLabel('Gabinet', { exact: true }).fill('02')
  await page.getByRole('button', { name: 'Zapisz zmiany', exact: true }).click()
  await expect(page.getByText('Gabinet 02 · Przychodnia API')).toBeVisible()
  const update = calls.find((c) => c.method === 'PUT')!.body as Record<string, unknown>
  expect(update.expectedVersion).toBe(3)
  expect(update.durationMinutes).toBe(45)
})
