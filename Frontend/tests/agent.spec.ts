import { test, expect } from '@playwright/test'

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
          options.onMessage({role:'agent', message:'Co jest powodem wizyty?', event_id:1});
          if (!options.textOnly) options.onMessage({role:'user',message:'Ból głowy od trzech dni.',event_id:2});
          options.onModeChange({mode:'listening'});
        }, 50);
        return {
          getId: () => id, getInputVolume: () => .4, getOutputVolume: () => .2,
          setVolume: ({volume}) => window.agentCalls.push({kind:'volume',volume}),
          setMicMuted: muted => window.agentCalls.push({kind:'mute',muted}),
          sendContextualUpdate: text => window.agentCalls.push({kind:'context',text}),
          sendUserMessage: text => { window.agentCalls.push({kind:'message',text}); options.onMessage({role:'agent',message:'Dziękuję. Czy przyjmujesz leki?',event_id:3}); },
          endSession: async () => {window.agentCalls.push({kind:'end'}); options.onDisconnect({reason:'user'});}
        };
      }
    }`,
    }),
  )
  let session = 0
  const endings: unknown[] = []
  await page.route('**/api/**', async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    if (path.endsWith('/authorize')) await route.fulfill({ json: { accessToken: 'scoped-token' } })
    else if (path.endsWith('/sessions')) {
      const { mode } = request.postDataJSON()
      session++
      await route.fulfill({
        json: {
          sessionId: `session_${session}`,
          provider: 'elevenlabs',
          mode,
          userId: `technical_${session}`,
          dynamicVariables: { language: 'pl' },
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
          summary: 'Pacjent opisuje ból głowy od trzech dni.',
          structuredData: {},
        },
      })
    else
      await route.fulfill({
        json: {
          interview: {
            displayName: 'Wywiad',
            visitDate: '2026-12-10T10:00:00Z',
            status: 'pending',
          },
        },
      })
  })
  await page.goto('/i/test-sdk-invitation')
  await page.getByRole('button', { name: 'Rozpocznij rozmowę', exact: true }).click()
  await expect(page.locator('.orb-scene')).toHaveAttribute('data-state', 'listening')
  await page.getByRole('button', { name: 'Wycisz mikrofon', exact: true }).click()
  await expect(page.locator('.orb-scene')).toHaveAttribute('data-state', 'paused')
  await page.getByRole('button', { name: 'Czat', exact: true }).click()
  await expect(
    page.getByRole('log').getByText('Ból głowy od trzech dni.', { exact: true }),
  ).toBeVisible()
  await page
    .getByRole('textbox', { name: 'Twoja odpowiedź', exact: true })
    .fill('Paracetamol doraźnie.')
  await page.getByRole('button', { name: 'Wyślij odpowiedź' }).click()
  await expect(
    page.getByRole('log').getByText('Dziękuję. Czy przyjmujesz leki?', { exact: true }),
  ).toBeVisible()
  await page.getByRole('button', { name: 'Zakończ rozmowę', exact: true }).click()
  await expect(
    page.getByText('Pacjent opisuje ból głowy od trzech dni.', { exact: true }),
  ).toBeVisible()
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
  expect(calls.filter((call) => call.kind === 'end')).toHaveLength(2)
  expect(endings).toContainEqual({ continuesInterview: true })
})
