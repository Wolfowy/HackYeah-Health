import { test, expect } from '@playwright/test'

test('ponowne otwarcie linku wysyła zapisany kontekst do nowej sesji ElevenLabs', async ({
  page,
}) => {
  const summary = 'Pacjent zgłosił ból głowy od wtorku. Przyjmuje ibuprofen.'
  await page.addInitScript(() => {
    Object.defineProperty(navigator.mediaDevices, 'getUserMedia', {
      value: async () => ({ getTracks: () => [{ stop() {} }] }),
    })
  })
  await page.route('**/@elevenlabs_client.js*', (route) =>
    route.fulfill({
      contentType: 'application/javascript',
      body: `export class Conversation {
        static async startSession(options) {
          window.resumedCredential = options.conversationToken;
          window.resumedVariables = options.dynamicVariables;
          window.resumedContext = [];
          options.onConnect?.();
          options.onModeChange({mode:'listening'});
          return {
            getId: () => 'conv_resume', getInputVolume: () => 0, getOutputVolume: () => 0,
            setVolume() {}, setMicMuted() {}, sendUserMessage() {},
            sendContextualUpdate: text => window.resumedContext.push(text),
            endSession: async () => options.onDisconnect({reason:'user'})
          };
        }
      }`,
    }),
  )
  let sessionCount = 0
  await page.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    if (path.endsWith('/authorize'))
      await route.fulfill({
        json: { accessToken: 'scoped-resume', interviewId: 'interview-resume' },
      })
    else if (path.endsWith('/sessions')) {
      sessionCount += 1
      await route.fulfill({
        json: {
          sessionId: `resume_${sessionCount}`,
          mode: route.request().postDataJSON().mode,
          provider: 'elevenlabs',
          conversationToken: `fresh_token_${sessionCount}`,
          dynamicVariables: {
            language: 'pl',
            is_continuation: true,
            previous_conversation_summary: summary,
          },
        },
      })
    } else if (path.endsWith('/provider-conversation') || path.endsWith('/end'))
      await route.fulfill({ status: 204 })
    else
      await route.fulfill({
        json: {
          interview: {
            id: 'interview-resume',
            displayName: 'Wywiad',
            visitDate: '2026-12-10T10:00:00Z',
            status: 'in_progress',
          },
        },
      })
  })
  for (let attempt = 1; attempt <= 2; attempt += 1) {
    // Reopen the original invitation: authorization removes the secret from the address bar.
    await page.goto('/i/test-resume-invitation')
    await page.getByRole('button', { name: 'Rozpocznij rozmowę', exact: true }).click()
    await expect
      .poll(() =>
        page.evaluate(
          () =>
            (window as unknown as { resumedContext?: string[] }).resumedContext?.join('\n') ?? '',
        ),
      )
      .toContain(summary)
    const sdk = await page.evaluate(() => {
      const state = window as unknown as {
        resumedCredential: string
        resumedVariables: Record<string, unknown>
      }
      return { credential: state.resumedCredential, variables: state.resumedVariables }
    })
    expect(sdk.credential).toBe(`fresh_token_${attempt}`)
    expect(sdk.variables).toMatchObject({
      is_continuation: true,
      previous_conversation_summary: summary,
    })
  }
})
