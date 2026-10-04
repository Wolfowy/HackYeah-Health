import { test, expect, type Page } from '@playwright/test'

async function mockAudio(page: Page) {
  await page.addInitScript(() => {
    const events: string[] = []
    Object.assign(window, { toneEvents: events })
    class TestAudioContext {
      state = 'running'
      currentTime = 0
      destination = {}
      resume() {
        events.push('resume')
        return Promise.resolve()
      }
      close() {
        this.state = 'closed'
        events.push('close')
        return Promise.resolve()
      }
      createGain() {
        return {
          gain: { setValueAtTime() {}, linearRampToValueAtTime() {} },
          connect() {},
          disconnect() {},
        }
      }
      createOscillator() {
        return {
          frequency: { value: 0 },
          onended: null,
          connect() {},
          start() {
            events.push('start')
          },
          stop() {},
          disconnect() {
            events.push('disconnect')
          },
        }
      }
    }
    Object.assign(window, { AudioContext: TestAudioContext })
    Object.defineProperty(navigator.mediaDevices, 'getUserMedia', {
      value: async () => ({ getTracks: () => [{ stop() {} }] }),
    })
  })
}

const audioEvents = (page: Page) =>
  page.evaluate(() => (window as unknown as { toneEvents: string[] }).toneEvents)

async function mockApi(page: Page) {
  await page.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    if (path.endsWith('/authorize'))
      await route.fulfill({ json: { accessToken: 'test-token', interviewId: 'test-interview' } })
    else if (path.endsWith('/sessions')) {
      const { mode } = route.request().postDataJSON()
      await route.fulfill({
        json: {
          sessionId: 'test-session',
          provider: 'elevenlabs',
          mode,
          ...(mode === 'voice'
            ? { conversationToken: 'test-voice' }
            : { signedUrl: 'wss://test-text' }),
        },
      })
    } else if (path.endsWith('/end') || path.endsWith('/provider-conversation'))
      await route.fulfill({ status: 204 })
    else if (path.endsWith('/result'))
      await route.fulfill({
        json: { status: 'processing', finalReport: null, structuredDataJson: null },
      })
    else
      await route.fulfill({
        json: {
          interview: {
            id: 'test-interview',
            displayName: 'Wywiad',
            visitDate: '2026-12-10T10:00:00Z',
            status: 'pending',
          },
        },
      })
  })
}

test('sygnał łączenia respektuje wyciszenie, błąd, anulowanie i ponowne rozpoczęcie', async ({
  page,
}) => {
  await mockAudio(page)
  await mockApi(page)
  await page.clock.install()
  let release = () => {}
  let sessionRequests = 0
  await page.route('**/api/interviews/*/sessions', async (route) => {
    await new Promise<void>((resolve) => {
      release = resolve
      sessionRequests++
    })
    await route.fulfill({ status: 503, json: {} }).catch(() => {})
  })
  await page.goto('/i/tone-test', { waitUntil: 'domcontentloaded' })
  expect(await audioEvents(page)).toEqual([])
  await page.getByRole('button', { name: 'Rozpocznij rozmowę', exact: true }).click()
  await expect(page.getByText('Łączę z asystentem…', { exact: true })).toBeVisible()
  await expect
    .poll(async () => (await audioEvents(page)).filter((event) => event === 'start').length)
    .toBe(2)
  await page.getByRole('button', { name: 'Wyłącz dźwięk' }).click()
  await expect
    .poll(async () => (await audioEvents(page)).filter((event) => event === 'disconnect').length)
    .toBe(2)
  // Advance past a ringback cycle to verify the interval was cleared.
  await page.clock.runFor(2500)
  expect((await audioEvents(page)).filter((event) => event === 'start')).toHaveLength(2)
  await page.getByRole('button', { name: 'Włącz dźwięk' }).click()
  await expect
    .poll(async () => (await audioEvents(page)).filter((event) => event === 'start').length)
    .toBe(4)
  release()
  await expect(page.getByRole('alert')).toContainText('chwilowo niedostępna')
  await expect
    .poll(async () => (await audioEvents(page)).filter((event) => event === 'disconnect').length)
    .toBe(4)
  await page.getByRole('button', { name: 'Rozpocznij rozmowę', exact: true }).click()
  await expect
    .poll(async () => (await audioEvents(page)).filter((event) => event === 'start').length)
    .toBe(6)
  await expect.poll(() => sessionRequests).toBe(2)
  await page.getByRole('button', { name: 'Anuluj łączenie', exact: true }).click()
  release()
  await expect(page.getByText('Porozmawiajmy o Twoim zdrowiu.', { exact: true })).toBeVisible()
  await expect
    .poll(async () => (await audioEvents(page)).filter((event) => event === 'disconnect').length)
    .toBe(6)
  await page.clock.runFor(2500)
  expect((await audioEvents(page)).filter((event) => event === 'start')).toHaveLength(6)
})

test('pierwsza wypowiedź zatrzymuje sygnał, wyciszenie podczas łączenia trafia do SDK, czat i raport są ciche', async ({
  page,
}) => {
  await mockAudio(page)
  await mockApi(page)
  await page.route('**/@elevenlabs_client.js*', (route) =>
    route.fulfill({
      contentType: 'application/javascript',
      body: `export class Conversation {
      static async startSession(options) {
        window.testOptions = options;
        if (!options.textOnly) await new Promise(resolve => { window.releaseSdk = resolve });
        else options.onConnect({conversationId:'conv-test'});
        return {
          getId: () => 'conv-test', getInputVolume: () => 0, getOutputVolume: () => 0,
          setVolume: ({volume}) => { if (options.textOnly) throw new Error('setVolume is not supported in text conversations'); window.sdkVolume = volume }, setMicMuted() {},
          sendContextualUpdate() {}, sendUserMessage() {},
          endSession: async () => options.onDisconnect({reason:'user'})
        };
      }
    }`,
    }),
  )
  await page.goto('/i/tone-connected', { waitUntil: 'domcontentloaded' })
  await page.getByRole('button', { name: 'Rozpocznij rozmowę', exact: true }).click()
  await expect
    .poll(async () => (await audioEvents(page)).filter((event) => event === 'start').length)
    .toBe(2)
  await expect
    .poll(() =>
      page.evaluate(() => typeof (window as unknown as { releaseSdk: unknown }).releaseSdk),
    )
    .toBe('function')
  await page.evaluate(() => {
    const state = window as unknown as { testOptions: { onMessage: (event: unknown) => void } }
    state.testOptions.onMessage({ role: 'agent', message: '[sighs] Dzień dobry.', event_id: 1 })
  })
  await expect(page.locator('.current-question')).toHaveText('Dzień dobry.')
  await expect
    .poll(async () => (await audioEvents(page)).filter((event) => event === 'disconnect').length)
    .toBe(2)
  await page.getByRole('button', { name: 'Wyłącz dźwięk' }).click()
  await page.getByRole('button', { name: 'Włącz dźwięk' }).click()
  expect((await audioEvents(page)).filter((event) => event === 'start')).toHaveLength(2)
  await page.getByRole('button', { name: 'Wyłącz dźwięk' }).click()
  await page.evaluate(() => (window as unknown as { releaseSdk: () => void }).releaseSdk())
  await expect(page.getByRole('button', { name: 'Wycisz mikrofon', exact: true })).toBeVisible()
  expect(await page.evaluate(() => (window as unknown as { sdkVolume: number }).sdkVolume)).toBe(0)
  await page.getByRole('button', { name: 'Czat', exact: true }).click()
  await expect(page.getByRole('textbox', { name: 'Twoja odpowiedź' })).toBeVisible()
  await page.getByRole('button', { name: 'Zakończ rozmowę', exact: true }).click()
  await expect(page.getByText('Przygotowuję Twoje podsumowanie…')).toBeVisible()
  await expect(
    page.getByRole('button', { name: 'Zatwierdź i udostępnij raport', exact: true }),
  ).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Dopowiedz głosowo', exact: true })).toBeDisabled()
  await expect(
    page.getByRole('button', { name: 'Dopowiedz na czacie', exact: true }),
  ).toBeDisabled()
  expect((await audioEvents(page)).filter((event) => event === 'start')).toHaveLength(2)
})
