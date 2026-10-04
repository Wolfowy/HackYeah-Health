import { test, expect } from '@playwright/test'
import { reportFixture } from './report-fixture'

test('konto pacjenta loguje prawdziwe dane, otwiera własną wizytę ograniczoną sesją, zapisuje profil i wylogowuje', async ({
  page,
}) => {
  const report = reportFixture()
  const user = {
    id: 'patient-1',
    displayName: 'Pacjent Testowy',
    email: 'patient@example.invalid',
    avatarUrl: null,
  }
  const visit = {
    id: report.visitId,
    scheduledAt: report.scheduledAt,
    status: 'awaiting_approval',
    interviewId: 'interview-1',
    interviewStatus: 'completed',
    doctor: { name: 'lek. Anna Testowa' },
    facility: { name: 'Przychodnia Testowa', address: 'ul. Testowa 12' },
  }
  let logins = 0
  let sessions = 0
  let logout = false
  await page.route('**/api/**', async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    if (path.endsWith('/auth/login')) {
      logins++
      expect(request.postDataJSON()).toEqual({ email: user.email, password: 'PatientPassword123' })
      await route.fulfill({
        json: {
          accessToken: 'account-jwt',
          refreshToken: 'account-refresh',
          accessTokenExpiresAt: '2099-01-01T00:00:00Z',
          user,
        },
      })
    } else if (path.includes('/patient-account/')) {
      expect(request.headers().authorization).toBe('Bearer account-jwt')
      if (path.endsWith('/visits')) await route.fulfill({ json: [{ visit }] })
      else if (path.endsWith('/me')) {
        if (request.method() === 'PUT') Object.assign(user, request.postDataJSON())
        await route.fulfill({ json: user })
      } else if (path.endsWith('/session')) {
        sessions++
        await route.fulfill({
          json: {
            sessionToken: 'visit-opaque',
            visitId: report.visitId,
            expiresAt: report.serviceExpiresAt,
          },
        })
      } else if (path.endsWith('/logout')) {
        expect(request.postDataJSON()).toEqual({ refreshToken: 'account-refresh' })
        logout = true
        await route.fulfill({ status: 204 })
      } else await route.fulfill({ status: 404 })
    } else {
      expect(request.headers().authorization).toBe('Bearer visit-opaque')
      if (path.endsWith('/result'))
        await route.fulfill({
          json: {
            status: 'completed',
            finalReport: 'Opis testowy',
            extractionStatus: 'ready',
            importStatus: 'ready',
          },
        })
      else if (path === '/api/v1/interview') await route.fulfill({ json: report })
      else if (path === `/api/visits/${report.visitId}/interview`)
        await route.fulfill({
          json: { id: 'interview-1', status: 'completed', visitDate: report.scheduledAt, visit },
        })
      else await route.fulfill({ status: 404 })
    }
  })
  await page.goto('/', { waitUntil: 'domcontentloaded' })
  await page.getByRole('textbox', { name: 'Adres e-mail' }).fill(user.email)
  await page.getByLabel('Hasło', { exact: true }).fill('PatientPassword123')
  await page.getByRole('button', { name: 'Zaloguj się', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Moje wizyty' })).toBeVisible()
  await expect(page.getByText('lek. Anna Testowa', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Otwórz podsumowanie' }).click()
  await expect(page.getByRole('heading', { name: 'Powód wizyty', exact: true })).toBeVisible()
  await expect(page.getByText('Ból głowy od trzech dni.', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Moje konto', exact: true }).click()
  await page.getByRole('textbox', { name: 'Nazwa konta' }).fill('Poprawiona nazwa')
  await page.getByRole('button', { name: 'Zapisz profil' }).click()
  await expect(page.getByRole('status')).toHaveText('Profil zapisany.')
  expect(user.displayName).toBe('Poprawiona nazwa')
  expect(sessions).toBe(1)
  expect(logins).toBe(1)
  expect(await page.evaluate(() => [localStorage.length, sessionStorage.length])).toEqual([0, 0])
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.getByRole('button', { name: 'Wyloguj', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Zaloguj się' })).toBeVisible()
  expect(logout).toBe(true)
})

test('rejestracja konta jest dostępna z linka zweryfikowanej wizyty', async ({ page }) => {
  let registered = false
  const user = {
    id: 'new-patient',
    email: 'new@example.invalid',
    displayName: 'Nowy Pacjent',
    avatarUrl: null,
  }
  await page.route('**/api/**', async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    if (path.endsWith('/authorize'))
      await route.fulfill({ json: { accessToken: 'verified-visit', interviewId: 'interview-1' } })
    else if (path.startsWith('/api/public'))
      await route.fulfill({
        json: {
          interview: { id: 'interview-1', status: 'pending', visitDate: '2026-12-10T10:00:00Z' },
        },
      })
    else if (path.endsWith('/register')) {
      expect(request.headers().authorization).toBe('Bearer verified-visit')
      expect(request.postDataJSON()).toEqual({
        ...{ email: user.email, displayName: user.displayName },
        password: 'PatientPassword123',
      })
      registered = true
      await route.fulfill({
        json: {
          accessToken: 'new-account',
          refreshToken: 'refresh',
          accessTokenExpiresAt: '2099-01-01T00:00:00Z',
          user,
        },
      })
    } else if (path.endsWith('/me')) await route.fulfill({ json: user })
    else if (path.endsWith('/visits')) await route.fulfill({ json: [] })
    else await route.fulfill({ status: 404 })
  })
  await page.goto('/i/register-test', { waitUntil: 'domcontentloaded' })
  await page.getByRole('button', { name: 'Konto pacjenta', exact: true }).click()
  await page
    .getByRole('textbox', { name: 'Imię i nazwisko lub nazwa konta' })
    .fill(user.displayName)
  await page.getByRole('textbox', { name: 'Adres e-mail' }).fill(user.email)
  await page.getByLabel('Hasło', { exact: true }).fill('PatientPassword123')
  await page.getByRole('button', { name: 'Utwórz konto', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Moje wizyty' })).toBeVisible()
  expect(registered).toBe(true)
})
