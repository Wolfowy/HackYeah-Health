import { test, expect, type Page } from '@playwright/test'
import { reportFixture } from './report-fixture'

async function setup(
  page: Page,
  options: {
    incomplete?: boolean
    conflict?: boolean
    pdfFailure?: boolean
    extractionFailure?: boolean
    entries?: boolean
    consentFailure?: boolean
  } = {},
) {
  const state = {
    view: reportFixture(),
    mutations: [] as { path: string; body: unknown }[],
    sessions: 0,
    pdfFailed: !!options.pdfFailure,
    importFailed: !!options.extractionFailure,
  }
  if (options.incomplete) {
    state.view.draft.clarifications = [
      { fieldPath: 'allergies', kind: 'Unknown', message: 'Uzupełnij informacje o alergiach.' },
    ]
    state.view.observations = [
      {
        id: 'observation-1',
        symptomName: 'Ból głowy',
        kind: 'Recurring',
        text: 'Objaw może powracać.',
        decision: 'Pending',
        editedByPatient: false,
      },
    ]
    state.view.supplementationRound = {
      id: 'round-1',
      number: 1,
      status: 'Open',
      questions: [{ id: 'question-1', text: 'Czy występują nudności?', answer: null, mode: null }],
    }
  }
  if (options.entries) {
    state.view.draft.medications.push({
      name: 'Lek drugi',
      dose: '5 mg',
      doseState: 'Provided',
      schedule: 'Wieczorem',
      reason: 'Inny powód',
    })
    state.view.draft.allergies = [
      { substance: 'Penicylna', reaction: 'Wysypka' },
      { substance: 'Pyłki', reaction: 'Katar' },
    ]
    state.view.draft.allergiesState = 'Provided'
  }
  await page.route('**/api/**', async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    const method = request.method()
    if (!path.startsWith('/api/public'))
      expect(request.headers().authorization).toBe('Bearer test-token')
    if (method !== 'GET')
      state.mutations.push({
        path,
        body: request.headers()['content-type']?.includes('application/json')
          ? request.postDataJSON()
          : null,
      })
    if (path.endsWith('/authorize'))
      await route.fulfill({ json: { accessToken: 'test-token', interviewId: 'test-interview' } })
    else if (path.startsWith('/api/public'))
      await route.fulfill({
        json: {
          interview: {
            id: 'test-interview',
            visitDate: state.view.scheduledAt,
            status: 'completed',
            visit: {
              scheduledAt: state.view.scheduledAt,
              doctor: { name: 'lek. Anna Testowa' },
              facility: { name: 'Przychodnia Testowa' },
              visitType: 'InPerson',
            },
          },
        },
      })
    else if (path.endsWith('/retry-import')) {
      state.importFailed = false
      await route.fulfill({
        json: {
          status: 'completed',
          finalReport: 'Rozmowa testowa',
          structuredData: {},
          extractionStatus: 'ready',
          importStatus: 'ready',
          issues: [],
        },
      })
    } else if (path.endsWith('/result'))
      await route.fulfill({
        json: {
          status: 'completed',
          finalReport: '[sighs] Rozmowa testowa',
          structuredData: { schemaVersion: 1 },
          schemaVersion: 1,
          extractionStatus: state.importFailed ? 'failed' : 'ready',
          importStatus: state.importFailed ? 'failed' : 'ready',
          issues: [],
        },
      })
    else if (path === '/api/v1/interview') await route.fulfill({ json: state.view })
    else if (path.endsWith('/draft')) {
      if (options.conflict) {
        await route.fulfill({ status: 409, json: { code: 'draft.revision_conflict' } })
        return
      }
      const body = request.postDataJSON()
      expect(body.expectedRevision).toBe(state.view.draft.revision)
      state.view = {
        ...state.view,
        status: 'InProgress',
        draft: { ...body, revision: state.view.draft.revision + 1, clarifications: [] },
      }
      await route.fulfill({ json: state.view })
    } else if (path.endsWith('/supplementation-round/answers')) {
      state.view.supplementationRound!.questions[0].answer = request.postDataJSON()[0].answer
      await route.fulfill({ status: 204 })
    } else if (path.endsWith('/complete')) await route.fulfill({ status: 204 })
    else if (path.endsWith('/approve')) {
      expect(request.postDataJSON()).toEqual({
        confirmIncompleteReport: state.view.draft.clarifications.length > 0,
        acceptAllObservations: true,
        shareWithFacility: true,
      })
      state.view.observations.forEach((item) => {
        if (item.decision === 'Pending') item.decision = 'Accepted'
      })
      state.view.latestVersion = (state.view.latestVersion ?? 0) + 1
      if (state.pdfFailed)
        await route.fulfill({ status: 409, json: { code: 'report.generation_failed' } })
      else {
        state.view.consentActive = true
        await route.fulfill({
          json: {
            versionId: 'version-1',
            versionNumber: state.view.latestVersion,
            approvedAt: '2026-10-04T12:00:00Z',
            confirmedIncomplete: options.incomplete ?? false,
          },
        })
      }
    } else if (path.endsWith('/consent')) {
      if (options.consentFailure && request.postDataJSON().granted) {
        options.consentFailure = false
        await route.fulfill({
          status: 503,
          json: { message: 'Udostępnienie chwilowo niedostępne.' },
        })
        return
      }
      state.view.consentActive = request.postDataJSON().granted
      await route.fulfill({ status: 204 })
    } else if (path.endsWith('/regenerate-pdf')) {
      state.pdfFailed = false
      await route.fulfill({ status: 204 })
    } else if (path.endsWith('/report.pdf'))
      await route.fulfill({ contentType: 'application/pdf', body: '%PDF-1.4 test fixture' })
    else if (path.endsWith('/report'))
      await route.fulfill({
        json: {
          versionNumber: state.view.latestVersion,
          consultationReason: state.view.draft.consultationReason,
        },
      })
    else if (path.endsWith('/voice/transcribe')) {
      expect(request.headers()['content-type']).toContain('multipart/form-data; boundary=')
      expect(request.postDataBuffer()?.toString()).toContain('Content-Type: audio/webm\r\n')
      await route.fulfill({ json: { text: 'Nudności po południu.' } })
    } else if (path.endsWith('/sessions')) {
      state.sessions++
      await route.fulfill({ status: 409, json: { code: 'agent_interview.inactive' } })
    } else await route.fulfill({ status: 404, json: {} })
  })
  await page.goto('/i/report-test', { waitUntil: 'domcontentloaded' })
  await expect(page.getByRole('heading', { name: 'Powód wizyty', exact: true })).toBeVisible()
  return state
}

test('ponownie otwarty link zatwierdza cały raport i udostępnia jednym kliknięciem, pobiera tylko PDF', async ({
  page,
}) => {
  const state = await setup(page)
  await expect(page.getByText('Pacjent nie wie', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Dopowiedz na czacie', exact: true }).click()
  await page
    .getByRole('textbox', { name: 'Twoje dopowiedzenie' })
    .fill('Dodatkowo nudności. [500 mg]')
  await page.getByRole('button', { name: 'Zapisz dopowiedzenie' }).click()
  await expect(page.getByRole('status')).toContainText('Poprawki zapisane')
  expect(state.view.draft.symptoms[0].timeline).toEqual(reportFixture().draft.symptoms[0].timeline)
  expect(state.view.draft.medications[0].doseState).toBe('Unknown')
  expect(state.view.draft.chronicConditionsState).toBe('NotAsked')
  expect(state.view.draft.additionalNotes).toBe(
    'Wcześniejsze uwagi\n\nDodatkowo nudności. [500 mg]',
  )
  await page.getByRole('button', { name: 'Edytuj podsumowanie', exact: true }).click()
  await page.getByRole('textbox', { name: 'Powód przyjmowania', exact: true }).fill('Ból skroni')
  await page.getByRole('button', { name: 'Zapisz poprawki' }).click()
  await expect(page.getByText(/Powód: Ból skroni/)).toBeVisible()
  await page.getByRole('button', { name: 'Zatwierdź i udostępnij raport', exact: true }).click()
  await expect(page.getByRole('status')).toContainText(
    'Zatwierdzono i udostępniono lekarzowi wersję 1',
  )
  expect(state.mutations.filter((item) => item.path.endsWith('/consent'))).toHaveLength(0)
  expect(state.view.consentActive).toBe(true)
  await expect(page.getByRole('button', { name: /Udostępnij wersję/ })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Pobierz JSON' })).toHaveCount(0)
  const pdf = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Pobierz PDF' }).click()
  expect((await pdf).suggestedFilename()).toBe('Przed-wizyta-v1.pdf')
  await page.getByRole('button', { name: 'Cofnij zgodę na udostępnienie' }).click()
  await expect(page.getByRole('status')).toContainText('Zgoda cofnięta')
  expect(state.sessions).toBe(0)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  expect(await page.evaluate(() => [localStorage.length, sessionStorage.length])).toEqual([0, 0])
  await page.screenshot({
    path: `test-results/report-review-${test.info().project.name}.png`,
    fullPage: true,
    animations: 'disabled',
  })
})

test('braki i obserwacje akceptuje cały raport; odpowiedzi lekarzowi trzeba wcześniej zapisać', async ({
  page,
}) => {
  const state = await setup(page, { incomplete: true })
  const approve = page.getByRole('button', { name: 'Zatwierdź i udostępnij raport', exact: true })
  await expect(approve).toBeDisabled()
  await expect(page.getByRole('checkbox')).toHaveCount(0)
  await expect(page.getByRole('button', { name: /obserwację/ })).toHaveCount(0)
  await expect(page.getByText('Objaw może powracać.', { exact: true })).toBeVisible()
  await page.getByRole('textbox', { name: 'Czy występują nudności?' }).fill('Nie występują.')
  await expect(approve).toBeDisabled()
  await page.getByRole('button', { name: 'Zapisz odpowiedzi dla lekarza' }).click()
  await expect(approve).toBeEnabled()
  await approve.click()
  await expect(page.getByRole('status')).toContainText('Zatwierdzono i udostępniono lekarzowi')
  expect(state.mutations.find((item) => item.path.endsWith('/approve'))?.body).toEqual({
    confirmIncompleteReport: true,
    acceptAllObservations: true,
    shareWithFacility: true,
  })
  expect(state.view.observations[0].decision).toBe('Accepted')
  expect(state.view.consentActive).toBe(true)
  expect(state.mutations.some((item) => item.path.endsWith('/decision'))).toBe(false)
})

test('konflikt rewizji zachowuje poprawki i nie zatwierdza raportu', async ({ page }) => {
  const state = await setup(page, { conflict: true })
  await page.getByRole('button', { name: 'Edytuj podsumowanie', exact: true }).click()
  await page
    .getByRole('textbox', { name: 'Powód wizyty', exact: true })
    .fill('Moja ręczna poprawka')
  await page.getByRole('button', { name: 'Zapisz poprawki' }).click()
  await expect(page.getByRole('alert')).toContainText('Raport zmienił się w innej sesji')
  await expect(page.getByRole('textbox', { name: 'Powód wizyty', exact: true })).toHaveValue(
    'Moja ręczna poprawka',
  )
  expect(state.mutations.some((item) => item.path.endsWith('/approve'))).toBe(false)
})

test('błąd PDF ponawia generowanie bez drugiego zatwierdzenia; nieudany import można ponowić', async ({
  page,
}) => {
  const state = await setup(page, { pdfFailure: true, extractionFailure: true })
  await expect(page.getByText(/Nie udało się w pełni przenieść/)).toBeVisible()
  await page.getByRole('button', { name: 'Ponów import rozmowy' }).click()
  await expect(page.getByText(/Nie udało się w pełni przenieść/)).toHaveCount(0)
  await page.getByRole('button', { name: 'Zatwierdź i udostępnij raport', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('nie udało się przygotować PDF')
  await expect(page.getByRole('button', { name: 'Pobierz PDF' })).toBeDisabled()
  expect(state.view.consentActive).toBe(false)
  await page.getByRole('button', { name: 'Ponów przygotowanie i udostępnienie raportu' }).click()
  await expect(page.getByRole('status')).toContainText('Raport PDF jest gotowy')
  await expect(page.getByRole('button', { name: 'Pobierz PDF' })).toBeEnabled()
  expect(state.view.consentActive).toBe(true)
  expect(state.mutations.filter((item) => item.path.endsWith('/consent'))).toEqual([
    { path: '/api/v1/interview/consent', body: { granted: true } },
  ])
  expect(state.view.latestVersion).toBe(1)
  expect(state.mutations.filter((item) => item.path.endsWith('/approve'))).toHaveLength(1)
})

test('dopowiedzenie głosowe używa transkrypcji i wymaga sprawdzenia tekstu przed zapisem', async ({
  page,
}) => {
  await page.addInitScript(() => {
    Object.defineProperty(navigator.mediaDevices, 'getUserMedia', {
      value: async () => ({ getTracks: () => [{ stop() {} }] }),
    })
    class Recorder {
      static isTypeSupported() {
        return true
      }
      mimeType = 'audio/webm;codecs=opus'
      state = 'inactive'
      ondataavailable?: (event: { data: Blob }) => void
      onstop?: () => void
      start() {
        this.state = 'recording'
      }
      stop() {
        this.state = 'inactive'
        this.ondataavailable?.({ data: new Blob(['audio-test']) })
        this.onstop?.()
      }
    }
    Object.defineProperty(window, 'MediaRecorder', { value: Recorder })
  })
  const state = await setup(page)
  await page.getByRole('button', { name: 'Dopowiedz głosowo', exact: true }).click()
  await page.getByRole('button', { name: 'Nagraj dopowiedzenie' }).click()
  await page.getByRole('button', { name: 'Zatrzymaj nagranie' }).click()
  await expect(page.getByRole('textbox', { name: 'Twoje dopowiedzenie' })).toHaveValue(
    'Nudności po południu.',
  )
  expect(state.mutations.some((item) => item.path.endsWith('/draft'))).toBe(false)
  await page
    .getByRole('textbox', { name: 'Twoje dopowiedzenie' })
    .fill('Nudności wieczorem — poprawiona transkrypcja.')
  await page.getByRole('button', { name: 'Zapisz dopowiedzenie' }).click()
  await expect(page.getByRole('status')).toContainText('Poprawki zapisane')
  expect(state.view.draft.additionalNotes).toContain(
    'Nudności wieczorem — poprawiona transkrypcja.',
  )
  expect(state.sessions).toBe(0)
})

test('edycja jednego leku i alergenu zachowuje pozostałe wpisy oraz rewizję całego raportu', async ({
  page,
}) => {
  const state = await setup(page, { entries: true })
  const before = structuredClone(state.view.draft)
  await page.getByRole('button', { name: 'Edytuj lek Paracetamol', exact: true }).click()
  await expect(page.getByRole('textbox', { name: 'Lek 1', exact: true })).toBeFocused()
  await expect(page.getByRole('textbox', { name: 'Lek 2', exact: true })).toHaveCount(0)
  await expect(page.getByRole('textbox', { name: 'Powód wizyty', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Zatwierdź i udostępnij raport' })).toHaveCount(0)
  await page.getByRole('textbox', { name: 'Lek 1', exact: true }).fill('')
  await page.getByRole('button', { name: 'Zapisz poprawki' }).click()
  expect(state.mutations.filter((item) => item.path.endsWith('/draft'))).toHaveLength(0)
  await page.getByRole('textbox', { name: 'Lek 1', exact: true }).fill('Ibuprofen')
  await page.getByRole('textbox', { name: 'Dawka', exact: true }).fill('200 mg')
  await page.getByRole('textbox', { name: 'Sposób przyjmowania' }).fill('Po posiłku')
  await page.getByRole('button', { name: 'Zapisz poprawki' }).click()
  await expect(
    page.getByRole('button', { name: 'Edytuj lek Ibuprofen', exact: true }),
  ).toBeVisible()
  expect(state.view.draft.medications[0]).toMatchObject({
    name: 'Ibuprofen',
    dose: '200 mg',
    doseState: 'Provided',
    schedule: 'Po posiłku',
  })
  expect(state.view.draft.medications[1]).toEqual(before.medications[1])
  expect(state.view.draft.symptoms).toEqual(before.symptoms)
  expect(state.view.draft.allergies).toEqual(before.allergies)
  await page.getByRole('button', { name: 'Edytuj alergen Penicylna', exact: true }).click()
  await expect(page.getByRole('textbox', { name: 'Alergen 2', exact: true })).toHaveCount(0)
  await page.getByRole('textbox', { name: 'Alergen 1', exact: true }).fill('Penicylina')
  await page.getByRole('textbox', { name: 'Reakcja', exact: true }).fill('Pokrzywka')
  await page.getByRole('button', { name: 'Zapisz poprawki' }).click()
  await expect(
    page.getByRole('button', { name: 'Edytuj alergen Penicylina', exact: true }),
  ).toBeVisible()
  expect(state.view.draft.allergies).toEqual([
    { substance: 'Penicylina', reaction: 'Pokrzywka' },
    before.allergies[1],
  ])
  expect(state.view.draft.questions).toEqual(before.questions)
  expect(
    state.mutations
      .filter((item) => item.path.endsWith('/draft'))
      .map((item) => (item.body as { expectedRevision: number }).expectedRevision),
  ).toEqual([4, 5])
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.screenshot({
    path: `test-results/report-inline-edit-${test.info().project.name}.png`,
    fullPage: true,
    animations: 'disabled',
  })
})

test('ponowienie po błędzie udostępnienia kończy zapisany PDF bez tworzenia kolejnej wersji', async ({
  page,
}) => {
  const state = await setup(page, { pdfFailure: true, consentFailure: true })
  await page.getByRole('button', { name: 'Zatwierdź i udostępnij raport' }).click()
  const retry = page.getByRole('button', { name: 'Ponów przygotowanie i udostępnienie raportu' })
  await retry.click()
  await expect(page.getByRole('alert')).toBeVisible()
  await expect(retry).toBeEnabled()
  expect(state.view.consentActive).toBe(false)
  await retry.click()
  await expect(page.getByRole('status')).toContainText(
    'Raport PDF jest gotowy i udostępniony lekarzowi',
  )
  expect(state.view.consentActive).toBe(true)
  expect(state.view.latestVersion).toBe(1)
  expect(state.mutations.filter((item) => item.path.endsWith('/approve'))).toHaveLength(1)
})
