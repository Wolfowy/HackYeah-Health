import { test, expect } from '@playwright/test'
import { reportFixture } from './report-fixture'

test('SDK łączy głos i czat, zachowuje historię, zwalnia mikrofon i pobiera wynik z backendu', async ({
  page,
}) => {
  await page.addInitScript(() => {
    Object.defineProperty(navigator.mediaDevices, 'getUserMedia', {
      value: async () => ({ getTracks: () => [{ stop() {} }] }),
    })
  })
  // Replace only the browser SDK transport. The application hook, API adapter and UI run unchanged.
  await page.route('**/@elevenlabs_client.js*', (route) =>
    route.fulfill({
      contentType: 'application/javascript',
      body: `export class Conversation {
      static async startSession(options) {
        (window.agentCalls ??= []).push({kind:'start', ...options});
        const id = 'conv_' + window.agentCalls.filter(c => c.kind === 'start').length;
        setTimeout(() => {
          options.onMessage({role:'agent', message:'[sighs] Co jest powodem wizyty?', event_id:1});
          if (!options.textOnly) options.onMessage({role:'user',message:'Ból głowy od trzech dni. [śmiech]',event_id:2});
          options.onModeChange({mode:'listening'});
        }, 50);
        return {
          getId: () => id, getInputVolume: () => .4, getOutputVolume: () => .2,
          setVolume: ({volume}) => { if (options.textOnly) throw new Error('setVolume is not supported in text conversations'); window.agentCalls.push({kind:'volume',volume}); },
          setMicMuted: muted => window.agentCalls.push({kind:'mute',muted}),
          sendContextualUpdate: text => window.agentCalls.push({kind:'context',text}),
          sendUserMessage: text => { window.agentCalls.push({kind:'message',text}); options.onMessage({role:'agent',message:'[whispers] Dziękuję. [wzdycha] Czy przyjmujesz leki?',event_id:3}); },
          endSession: async () => {window.agentCalls.push({kind:'end'}); options.onDisconnect({reason:'user'});}
        };
      }
    }`,
    }),
  )
  let report = reportFixture()
  let session = 0
  const endings: unknown[] = []
  await page.route('**/api/**', async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    if (path === '/api/v1/interview') await route.fulfill({ json: report })
    else if (path.endsWith('/draft')) {
      const body = request.postDataJSON()
      report = {
        ...report,
        draft: { ...body, revision: report.draft.revision + 1, clarifications: [] },
      }
      await route.fulfill({ json: report })
    } else if (path.endsWith('/complete')) await route.fulfill({ status: 204 })
    else if (path.endsWith('/approve')) {
      report.latestVersion = 1
      report.consentActive = true
      await route.fulfill({
        json: {
          versionId: 'version',
          versionNumber: 1,
          approvedAt: '2026-10-04T10:00:00Z',
          confirmedIncomplete: false,
        },
      })
    } else if (path.endsWith('/authorize'))
      await route.fulfill({ json: { accessToken: 'scoped-token', interviewId: 'interview-sdk' } })
    else if (path.endsWith('/sessions')) {
      const { mode } = request.postDataJSON()
      session++
      await route.fulfill({
        json: {
          sessionId: `session_${session}`,
          provider: 'elevenlabs',
          mode,
          ...(mode === 'voice'
            ? { conversationToken: 'temporary-voice' }
            : { signedUrl: 'wss://temporary-text' }),
        },
      })
    } else if (path.endsWith('/provider-conversation')) await route.fulfill({ status: 204 })
    else if (path.endsWith('/end')) {
      endings.push(request.postDataJSON())
      await route.fulfill({ status: 204 })
    } else if (path.endsWith('/result'))
      await route.fulfill({
        json: {
          status: 'completed',
          finalReport:
            session >= 3
              ? 'Pacjent opisuje ból głowy od trzech dni. Dodatkowo zgłasza nudności.'
              : '[sighs] Pacjent opisuje ból głowy od trzech dni.',
          structuredDataJson: '{}',
        },
      })
    else
      await route.fulfill({
        json: {
          interview: {
            id: 'interview-sdk',
            displayName: 'Wywiad',
            visitDate: '2026-12-10T10:00:00Z',
            status: 'pending',
            visit: {
              id: 'visit-sdk',
              scheduledAt: '2026-12-10T10:00:00Z',
              timeZone: 'Europe/Warsaw',
              doctor: { name: 'lek. Anna Testowa', specialty: 'Neurologia' },
              facility: { name: 'Przychodnia Testowa', address: 'ul. Testowa 12, Warszawa' },
              room: '204',
              visitType: 'Konsultacja w placówce',
              locationInstructions: 'Drugie piętro, wejście od ulicy.',
            },
          },
        },
      })
  })
  await page.goto('/i/test-sdk-invitation', { waitUntil: 'domcontentloaded' })
  const card = page.getByRole('region', { name: 'Informacje o wizycie', exact: true })
  await expect(card.getByText('lek. Anna Testowa', { exact: true })).toBeVisible()
  await expect(card.getByText('ul. Testowa 12, Warszawa', { exact: true })).toBeVisible()
  await expect(card.locator('time')).toHaveText('10 grudnia 2026 godz. 11:00')
  await card.locator('summary').click()
  await expect(card.getByText('Konsultacja w placówce')).toBeVisible()
  await expect(card.getByText('Drugie piętro, wejście od ulicy.', { exact: true })).toBeVisible()
  await card.locator('summary').click()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.screenshot({
    path: `test-results/visit-card-${test.info().project.name}.png`,
    fullPage: true,
  })
  await page.getByRole('button', { name: 'Rozpocznij rozmowę', exact: true }).click()
  await expect(page.locator('.orb-scene')).toHaveAttribute('data-state', 'listening')
  await expect(page.locator('.current-question')).toHaveText('Co jest powodem wizyty?')
  await page.getByRole('button', { name: 'Wycisz mikrofon', exact: true }).click()
  await expect(page.locator('.orb-scene')).toHaveAttribute('data-state', 'paused')
  await page.getByRole('button', { name: 'Czat', exact: true }).click()
  await expect(
    page.getByRole('log').getByText('Ból głowy od trzech dni. [śmiech]', { exact: true }),
  ).toBeVisible()
  await page
    .getByRole('textbox', { name: 'Twoja odpowiedź', exact: true })
    .fill('Paracetamol [500 mg] doraźnie. [sighs]')
  await page.getByRole('button', { name: 'Wyślij odpowiedź' }).click()
  await expect(
    page.getByRole('log').getByText('Dziękuję. Czy przyjmujesz leki?', { exact: true }),
  ).toBeVisible()
  await expect(
    page.getByRole('log').getByText('Paracetamol [500 mg] doraźnie. [sighs]', { exact: true }),
  ).toBeVisible()
  await expect(
    page.locator('.assistant .chat-bubble').filter({ hasText: /\[(whispers|wzdycha|sighs)\]/ }),
  ).toHaveCount(0)
  await page.getByRole('button', { name: 'Zakończ rozmowę', exact: true }).click()
  await page.locator('.report-summary-text summary').click()
  await expect(
    page.getByText('Pacjent opisuje ból głowy od trzech dni.', { exact: true }),
  ).toBeVisible()
  await page.getByRole('button', { name: 'Zatwierdź i udostępnij raport', exact: true }).click()
  await expect(page.getByRole('status')).toContainText(
    'Zatwierdzono i udostępniono lekarzowi wersję 1',
  )
  await expect(
    page.getByRole('button', { name: 'Raport zatwierdzony', exact: true }),
  ).toBeDisabled()
  await page.getByRole('button', { name: 'Dopowiedz głosowo', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Nagraj dopowiedzenie' })).toBeVisible()
  await page.getByRole('button', { name: 'Wróć do podsumowania', exact: true }).click()
  await page.getByRole('button', { name: 'Dopowiedz na czacie', exact: true }).click()
  await page
    .getByRole('textbox', { name: 'Twoje dopowiedzenie' })
    .fill('Dodatkowo zgłaszam nudności.')
  await page.getByRole('button', { name: 'Zapisz dopowiedzenie' }).click()
  await expect(
    page.getByText('Wcześniejsze uwagi\n\nDodatkowo zgłaszam nudności.', { exact: true }),
  ).toBeVisible()
  await expect(
    page.getByRole('button', { name: 'Zatwierdź i udostępnij raport', exact: true }),
  ).toBeEnabled()
  expect(session).toBe(2)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.screenshot({
    path: `test-results/summary-actions-${test.info().project.name}.png`,
    fullPage: true,
    animations: 'disabled',
  })
  const calls = await page.evaluate(
    () => (window as unknown as { agentCalls: Record<string, unknown>[] }).agentCalls,
  )
  const starts = calls.filter((call) => call.kind === 'start')
  expect(starts[0]).toMatchObject({
    connectionType: 'webrtc',
    textOnly: false,
    conversationToken: 'temporary-voice',
  })
  expect(starts[1]).toMatchObject({
    connectionType: 'websocket',
    textOnly: true,
    signedUrl: 'wss://temporary-text',
  })
  expect(calls.find((call) => call.kind === 'context')?.text).toContain('Ból głowy od trzech dni.')
  expect(calls.find((call) => call.kind === 'context')?.text).toContain('[sighs]')
  expect(starts).toHaveLength(2)
  expect(calls.filter((call) => call.kind === 'end')).toHaveLength(2)
  expect(endings).toContainEqual({ continuesInterview: true })
})
