import { test, expect, type Page } from '@playwright/test'
import { reportFixture } from './report-fixture'

async function setup(page: Page) {
  const state = {
    starts: 0,
    binds: [] as string[],
    endings: [] as { id: string; continuesInterview: boolean }[],
    imported: false,
    draftReads: 0,
  }
  await page.route('**/@elevenlabs_client.js*', (route) =>
    route.fulfill({
      contentType: 'application/javascript',
      body: `export class Conversation {
      static async startSession(options) {
        const index = window.sdkStarts = (window.sdkStarts ?? 0) + 1;
        const id = 'conv_' + index;
        options.onConnect({conversationId:id});
        await new Promise(resolve => { window.releaseSdk = resolve });
        return {
          getId: () => id, getInputVolume: () => 0, getOutputVolume: () => 0,
          setVolume() { if (options.textOnly) throw new Error('setVolume is not supported in text conversations'); },
          setMicMuted() {}, sendContextualUpdate() {}, sendUserMessage() {},
          endSession: async () => { (window.closedSdk ??= []).push(id); options.onDisconnect({reason:'user'}); }
        };
      }
    }`,
    }),
  )
  await page.route('**/api/**', async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    if (path.endsWith('/authorize'))
      await route.fulfill({ json: { accessToken: 'scoped-token', interviewId: 'interview-1' } })
    else if (path.startsWith('/api/public'))
      await route.fulfill({
        json: {
          interview: { id: 'interview-1', status: 'pending', visitDate: '2026-12-10T10:00:00Z' },
        },
      })
    else {
      expect(request.headers().authorization).toBe('Bearer scoped-token')
      if (path.endsWith('/sessions')) {
        state.starts++
        await route.fulfill({
          json: {
            sessionId: 'session_' + state.starts,
            mode: 'text',
            provider: 'elevenlabs',
            signedUrl: 'wss://test-only',
          },
        })
      } else if (path.endsWith('/provider-conversation')) {
        state.binds.push(request.postDataJSON().conversationId)
        await route.fulfill({ status: 204 })
      } else if (path.endsWith('/end')) {
        state.endings.push({ id: path.split('/')[3], ...request.postDataJSON() })
        await route.fulfill({ status: 204 })
      } else if (path.endsWith('/result'))
        await route.fulfill({
          json: {
            status: 'completed',
            finalReport: 'Opis z serwera',
            extractionStatus: 'ready',
            importStatus: state.imported ? 'ready' : 'pending',
          },
        })
      else if (path === '/api/v1/interview') {
        state.draftReads++
        await route.fulfill({ json: reportFixture() })
      } else await route.fulfill({ status: 404, json: {} })
    }
  })
  await page.goto('/i/lifecycle-test', { waitUntil: 'domcontentloaded' })
  await page.getByRole('button', { name: 'Czat', exact: true }).click()
  return state
}

const release = (page: Page) =>
  page.evaluate(() => (window as unknown as { releaseSdk: () => void }).releaseSdk())

test('ID dostawcy zapisuje się przed końcem startu SDK; sam tekst podsumowania nie odblokowuje raportu przed importem', async ({
  page,
}) => {
  const state = await setup(page)
  await page.getByRole('button', { name: 'Rozpocznij czat', exact: true }).click()
  await expect.poll(() => state.binds).toEqual(['conv_1'])
  await release(page)
  await expect(page.getByRole('textbox', { name: 'Twoja odpowiedź' })).toBeVisible()
  await page.getByRole('button', { name: 'Zakończ rozmowę', exact: true }).click()
  await expect(page.getByText('Przygotowuję Twoje podsumowanie…')).toBeVisible()
  await expect(
    page.getByRole('button', { name: 'Zatwierdź i udostępnij raport', exact: true }),
  ).toBeDisabled()
  expect(state.draftReads).toBe(0)
  expect(state.endings).toEqual([{ id: 'session_1', continuesInterview: false }])
  state.imported = true
  await page.getByRole('button', { name: 'Sprawdź podsumowanie' }).click()
  await expect(page.getByRole('heading', { name: 'Powód wizyty', exact: true })).toBeVisible()
  expect(state.binds).toEqual(['conv_1'])
  expect(state.starts).toBe(1)
  expect(
    await page.evaluate(() => (window as unknown as { closedSdk: string[] }).closedSdk),
  ).toEqual(['conv_1'])
})

test('anulowany start nie kończy całego wywiadu, a opóźniony SDK zamyka się i nie nadpisuje nowej sesji', async ({
  page,
}) => {
  const state = await setup(page)
  await page.getByRole('button', { name: 'Rozpocznij czat', exact: true }).click()
  await expect.poll(() => state.binds).toEqual(['conv_1'])
  await page.getByRole('button', { name: 'Anuluj łączenie', exact: true }).click()
  await release(page)
  await expect.poll(() => state.endings).toEqual([{ id: 'session_1', continuesInterview: true }])
  await expect(page.getByRole('button', { name: 'Rozpocznij czat', exact: true })).toBeEnabled()
  await page.getByRole('button', { name: 'Rozpocznij czat', exact: true }).click()
  await expect.poll(() => state.binds).toEqual(['conv_1', 'conv_2'])
  await release(page)
  await expect(page.getByRole('textbox', { name: 'Twoja odpowiedź' })).toBeVisible()
  await page.getByRole('button', { name: 'Zakończ rozmowę', exact: true }).click()
  await expect
    .poll(() => state.endings)
    .toEqual([
      { id: 'session_1', continuesInterview: true },
      { id: 'session_2', continuesInterview: false },
    ])
  expect(
    await page.evaluate(() => (window as unknown as { closedSdk: string[] }).closedSdk),
  ).toEqual(['conv_1', 'conv_2'])
})
