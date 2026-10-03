import { test, expect, type Page } from '@playwright/test'

async function navigate(page: Page, name: string) {
  if (await page.getByRole('button', { name: 'Otwórz menu' }).isVisible())
    await page.getByRole('button', { name: 'Otwórz menu' }).click()
  await page
    .getByRole('navigation', { name: 'Nawigacja główna' })
    .getByRole('link', { name, exact: true })
    .click()
}

test('gość przechodzi wywiad, poprawia raport, zatwierdza i osobno udostępnia', async ({
  page,
}) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Porozmawiajmy o Twoim zdrowiu' })).toBeVisible()
  await page.getByRole('button', { name: 'Czat', exact: true }).click()
  const replies = [
    'Od kilku dni boli mnie głowa.',
    'Ból pojawia się co kilka godzin, utrudnia mi skupienie.',
    'Od około trzech dni. Wieczorem jest gorzej.',
    'Paracetamol 500 mg, doraźnie z powodu bólu głowy.',
    'Nie mam znanych alergii.',
    'Nie leczę się na choroby przewlekłe.',
    'Chcę zapytać, jakie informacje warto dalej obserwować.',
  ]
  for (const reply of replies) await page.getByRole('button', { name: reply, exact: true }).click()
  await page.getByRole('button', { name: 'Sprawdź podsumowanie' }).click()
  await page.getByRole('button', { name: 'Edytuj: Powód wizyty', exact: true }).click()
  await page.getByLabel('Treść informacji').fill('Ból głowy od czterech dni.')
  await page.getByRole('button', { name: 'Zapisz zmiany' }).click()
  await expect(page.getByText('Ból głowy od czterech dni.', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Zatwierdź treść raportu' }).click()
  await expect(page.getByRole('heading', { name: 'Treść zatwierdzona przez Ciebie' })).toBeVisible()
  await page.getByRole('button', { name: 'Udostępnij placówce' }).click()
  await expect(page.getByRole('button', { name: 'Potwierdź udostępnienie' })).toBeDisabled()
  await page.getByRole('checkbox', { name: 'Zgadzam się na udostępnienie' }).check()
  await page.getByRole('button', { name: 'Potwierdź udostępnienie' }).click()
  await expect(page.getByText('Udostępniony w demo', { exact: true })).toBeVisible()
  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Pobierz JSON' }).click()
  expect((await download).suggestedFilename()).toBe('przed-wizyta-raport-v1.json')
  await page.getByRole('button', { name: 'Cofnij zgodę na udostępnienie' }).click()
  await expect(page.getByRole('button', { name: 'Udostępnij placówce' })).toBeVisible()
})

test('demo głosu reaguje na start i pauzę, a przełączenie do tekstu zachowuje odpowiedź', async ({
  page,
}) => {
  await page.goto('/')
  await page.getByRole('button', { name: 'Rozpocznij rozmowę demonstracyjną' }).click()
  await expect(page.locator('.orb-scene')).toHaveAttribute('data-state', 'speaking')
  await expect(page.locator('.orb-scene')).toHaveAttribute('data-state', 'listening')
  await page.getByRole('button', { name: 'Pauza', exact: true }).click()
  await expect(page.locator('.orb-scene')).toHaveAttribute('data-state', 'paused')
  await page.getByRole('button', { name: 'Wznów rozmowę', exact: true }).first().click()
  await page.getByRole('button', { name: 'Od kilku dni boli mnie głowa.', exact: true }).click()
  await page.getByRole('button', { name: 'Zatwierdź odpowiedź' }).click()
  await page.getByRole('button', { name: 'Wolę pisać' }).click()
  await expect(
    page.getByRole('log').getByText('Od kilku dni boli mnie głowa.', { exact: true }),
  ).toBeVisible()
  await page
    .getByRole('textbox', { name: 'Twoja odpowiedź', exact: true })
    .fill('Własna odpowiedź pacjenta.')
  await page.getByRole('button', { name: 'Wyślij odpowiedź' }).click()
  await expect(
    page.getByRole('log').getByText('Własna odpowiedź pacjenta.', { exact: true }),
  ).toBeVisible()
})

test('konto demo udostępnia listę wizyt, izoluje raporty i czyści dane po wylogowaniu', async ({
  page,
}) => {
  await page.goto('/')
  await page.getByRole('button', { name: 'Czat', exact: true }).click()
  await page.getByRole('button', { name: 'Od kilku dni boli mnie głowa.', exact: true }).click()
  await page.getByRole('button', { name: 'Zaloguj się', exact: true }).click()
  await page.getByRole('button', { name: 'Wejdź na konto demonstracyjne' }).click()
  await page.getByRole('button', { name: 'Menu konta' }).click()
  await page.getByRole('button', { name: 'Moje konto', exact: true }).click()
  await page.getByLabel('Imię', { exact: true }).fill('Anna')
  await page.getByRole('button', { name: 'Zapisz zmiany' }).click()
  await expect(page.getByRole('heading', { name: 'Anna Kowalska' })).toBeVisible()
  await navigate(page, 'Moje wizyty')
  await page
    .getByRole('article')
    .filter({ has: page.getByRole('heading', { name: 'Kardiolog' }) })
    .getByRole('button', { name: 'Przygotuj się' })
    .click()
  await page.getByRole('button', { name: 'Moje podsumowanie' }).click()
  await expect(page.getByText('Od kilku dni boli mnie głowa.', { exact: true })).toHaveCount(0)
  await page.getByRole('button', { name: 'Edytuj: Dodatkowe informacje', exact: true }).click()
  await page.getByLabel('Treść informacji').fill('Dodatkowy opis dla kardiologa.')
  await page.getByRole('button', { name: 'Zapisz zmiany' }).click()
  await expect(page.getByText('Dodatkowy opis dla kardiologa.', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Menu konta' }).click()
  await page.getByRole('button', { name: 'Wyloguj się', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Zaloguj się', exact: true })).toBeVisible()
  await expect(page.getByText('Od kilku dni boli mnie głowa.', { exact: true })).toHaveCount(0)
})

test('niepełny raport wymaga potwierdzenia, a niewłaściwy kod nie otwiera wizyty', async ({
  page,
}) => {
  await page.goto('/')
  await page.getByRole('button', { name: 'Mam kod innej wizyty' }).click()
  await page.getByLabel('Kod wizyty', { exact: true }).fill('WRONG')
  await page.getByRole('button', { name: 'Otwórz wywiad', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('Nieprawidłowy kod')
  await page.getByLabel('Kod wizyty', { exact: true }).fill('DEMO2026')
  await page.getByRole('button', { name: 'Otwórz wywiad', exact: true }).click()
  await page.getByRole('button', { name: 'Czat', exact: true }).click()
  await page.getByRole('button', { name: 'Od kilku dni boli mnie głowa.', exact: true }).click()
  await navigate(page, 'Podsumowanie')
  await expect(page.getByRole('button', { name: 'Zatwierdź treść raportu' })).toBeDisabled()
  await page.getByRole('checkbox', { name: 'Rozumiem, że raport jest niepełny' }).check()
  await page.getByRole('button', { name: 'Zatwierdź treść raportu' }).click()
  await expect(page.getByRole('button', { name: 'Udostępnij placówce' })).toBeVisible()
})

test('interfejs mieści się w szerokości ekranu', async ({ page }) => {
  await page.goto('/')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.screenshot({
    path: `test-results/interview-${test.info().project.name}.png`,
    fullPage: true,
  })
  await page.getByRole('button', { name: 'Czat', exact: true }).click()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
})
