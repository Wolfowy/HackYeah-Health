import { expect, test, type Page } from '@playwright/test'

test.beforeEach(async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-04T06:00:00+02:00') })
})

async function login(page: Page) {
  await page.goto('/')
  await page.getByRole('button', { name: 'Otwórz wersję demo' }).click()
  await expect(page.getByRole('heading', { name: 'Kalendarz wizyt.' })).toBeVisible()
}

test('login validates credentials, restores demo session and logs out', async ({ page }) => {
  await page.goto('/')
  await page.getByText('Demo', { exact: true }).click()
  await page.getByLabel('Hasło', { exact: true }).fill('niepoprawne')
  await page.getByRole('button', { name: 'Zaloguj się', exact: true }).click()
  await expect(page.getByText('Użyj danych konta demonstracyjnego podanych poniżej.')).toBeVisible()
  await page.getByRole('button', { name: 'Otwórz wersję demo' }).click()
  await expect(page.getByRole('heading', { name: 'Kalendarz wizyt.' })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Kalendarz wizyt.' })).toBeVisible()
  await page.getByRole('button', { name: /Anna Nowak/ }).click()
  await page.getByRole('menuitem', { name: 'Wyloguj się' }).click()
  await expect(page.getByRole('heading', { name: 'Witaj w panelu przychodni' })).toBeVisible()
  await expect(page.locator('.ant-drawer')).toHaveCount(0)
})

test('calendar opens visit, shows approved report and the missing report state', async ({
  page,
}) => {
  await login(page)
  await page.getByRole('button', { name: '08:30 Jan Malinowski, Raport gotowy' }).click()
  await expect(page.getByRole('heading', { name: 'Jan Malinowski' })).toBeVisible()
  await page.getByRole('tab', { name: /Raport/ }).click()
  await expect(page.getByRole('heading', { name: 'Powód konsultacji' })).toBeVisible()
  await expect(page.getByText('Przykładowy raport pacjenta')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Przyjmowane leki' })).toBeVisible()
  await page.getByRole('button', { name: 'Zamknij szczegóły', exact: true }).click()
  await expect(page.locator('.ant-drawer')).toHaveCount(0)
  await page.getByRole('button', { name: '10:30 Michał Lewandowski, Nie rozpoczęto' }).click()
  await page.getByRole('tab', { name: /Raport/ }).click()
  await expect(page.getByRole('heading', { name: 'Raport jeszcze nie jest gotowy' })).toBeVisible()
})

test('copy and invitation simulation update delivery and activity without API calls', async ({
  page,
  context,
}) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  const apiRequests: string[] = []
  page.on('request', (request) => {
    if (request.url().includes('/api/')) apiRequests.push(request.url())
  })
  await login(page)
  await page.getByRole('button', { name: '10:30 Michał Lewandowski, Nie rozpoczęto' }).click()
  await page.getByRole('button', { name: 'Kopiuj link zaproszenia' }).click()
  await expect
    .poll(() => page.evaluate(() => navigator.clipboard.readText()))
    .toContain('https://przedwizyta.example/i/demo-invitation-3')
  await page.getByRole('button', { name: 'Wyślij zaproszenie', exact: true }).click()
  await expect(page.getByText('Wersja demo — żadna wiadomość nie zostanie wysłana.')).toBeVisible()
  await page.getByRole('button', { name: 'Symuluj wysyłkę' }).click()
  await expect(page.getByRole('button', { name: 'Ponów zaproszenie' })).toBeVisible()
  await page.getByRole('tab', { name: 'Aktywność' }).click()
  await expect(page.getByRole('heading', { name: 'Symulacja wysyłki zaproszenia' })).toBeVisible()
  expect(apiRequests).toEqual([])
})

test('search, month navigation and reports list work', async ({ page }) => {
  await login(page)
  await page.getByRole('textbox', { name: 'Szukaj pacjenta lub wizyty' }).fill('Jan Malinowski')
  await expect(page.locator('.calendar-event')).toHaveCount(1)
  await page.getByRole('button', { name: 'Wyczyść', exact: true }).click()
  await page.getByText('Miesiąc', { exact: true }).click()
  await expect(page.locator('.month-calendar')).toBeVisible()
  await page.getByRole('button', { name: 'Następny okres' }).click()
  await expect(page.locator('.month-event')).toHaveCount(0)
  await page.getByRole('button', { name: 'Poprzedni okres' }).click()
  await expect(page.locator('.month-event').first()).toBeVisible()
  await page.getByRole('menuitem', { name: /Raporty/ }).click()
  await expect(page.getByRole('heading', { name: 'Raporty pacjentów.' })).toBeVisible()
  await page
    .getByRole('row')
    .filter({ hasText: 'WIZ-0001' })
    .getByRole('button', { name: 'Otwórz wizytę Jan Malinowski' })
    .click()
  await expect(page.getByRole('heading', { name: 'Powód konsultacji' })).toBeVisible()
})

test('new visit rejects a collision and accepts a free slot', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: 'Nowa wizyta' }).click()
  await page.getByLabel('Imię i nazwisko pacjenta').fill('Pacjent Testowy')
  await page.getByLabel('Telefon (do SMS)').fill('+48 500 222 333')
  await page.getByLabel('Godzina', { exact: true }).fill('08:30')
  await page.getByLabel('Godzina', { exact: true }).press('Tab')
  await page.getByRole('button', { name: 'Dodaj wizytę', exact: true }).click()
  await expect(
    page.getByText('Lekarz lub gabinet ma już wizytę w tym terminie. Wybierz inną godzinę.'),
  ).toBeVisible()
  await page.getByLabel('Godzina', { exact: true }).fill('17:00')
  await page.getByLabel('Godzina', { exact: true }).press('Tab')
  await page.getByRole('button', { name: 'Dodaj wizytę', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Pacjent Testowy' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Wyślij zaproszenie', exact: true })).toBeVisible()
})

test('cancelled visit disables invitation and layout stays within viewport', async ({ page }) => {
  await login(page)
  const width = await page.evaluate(() => ({
    viewport: innerWidth,
    page: document.documentElement.scrollWidth,
  }))
  expect(width.page).toBeLessThanOrEqual(width.viewport + 1)
  await page.screenshot({
    path: `test-results/calendar-${test.info().project.name}.png`,
    fullPage: true,
  })
  await page.getByRole('button', { name: '14:00 Bartosz Król, Anulowana' }).click()
  await expect(page.getByText('Wizyta anulowana — zaproszenie jest nieaktywne.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Kopiuj link zaproszenia' })).toBeDisabled()
})
