export function formatVisitSchedule(iso: string, timeZone = 'Europe/Warsaw') {
  const date = new Date(iso)
  if (!iso || Number.isNaN(date.getTime())) return null
  let zone = timeZone
  try {
    new Intl.DateTimeFormat('pl-PL', { timeZone: zone })
  } catch {
    zone = 'Europe/Warsaw'
  }
  return {
    date: date.toLocaleDateString('pl-PL', {
      day: 'numeric',
      month: 'long',
      year: 'numeric',
      timeZone: zone,
    }),
    time: date.toLocaleTimeString('pl-PL', {
      hour: '2-digit',
      minute: '2-digit',
      timeZone: zone,
    }),
    timeZone: zone,
  }
}
