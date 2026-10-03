export function appointmentDay(iso: string) {
  const today = new Date()
  const date = new Date(iso)
  const tomorrow = new Date(today)
  tomorrow.setDate(today.getDate() + 1)
  const sameDay = (a: Date, b: Date) =>
    a.toLocaleDateString('pl-PL', { timeZone: 'Europe/Warsaw' }) ===
    b.toLocaleDateString('pl-PL', { timeZone: 'Europe/Warsaw' })
  if (sameDay(date, today)) return 'Dzisiaj'
  if (sameDay(date, tomorrow)) return 'Jutro'
  return date.toLocaleDateString('pl-PL', {
    day: 'numeric',
    month: 'long',
    timeZone: 'Europe/Warsaw',
  })
}

export function appointmentTime(iso: string) {
  return new Date(iso).toLocaleTimeString('pl-PL', {
    hour: '2-digit',
    minute: '2-digit',
    timeZone: 'Europe/Warsaw',
  })
}

export function fullDate(iso: string) {
  return new Date(iso).toLocaleDateString('pl-PL', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    timeZone: 'Europe/Warsaw',
  })
}

export function downloadJson(data: unknown, filename: string) {
  const url = URL.createObjectURL(
    new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' }),
  )
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  link.click()
  URL.revokeObjectURL(url)
}
