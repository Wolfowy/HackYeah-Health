import dayjs from 'dayjs'
import utc from 'dayjs/plugin/utc.js'
import timezone from 'dayjs/plugin/timezone.js'
import 'dayjs/locale/pl.js'

dayjs.extend(utc)
dayjs.extend(timezone)
dayjs.locale('pl')

export const TIME_ZONE = 'Europe/Warsaw'
export const inWarsaw = (value?: string | Date) => dayjs(value).tz(TIME_ZONE)
export const atTime = (date: dayjs.Dayjs, time: string) =>
  dayjs.tz(`${date.format('YYYY-MM-DD')} ${time}`, TIME_ZONE)
export const weekStart = (date: dayjs.Dayjs) =>
  atTime(date.subtract((date.day() + 6) % 7, 'day'), '00:00')
export function initialDate() {
  const today = inWarsaw().startOf('day')
  return atTime(
    today.day() === 6 ? today.add(2, 'day') : today.day() === 0 ? today.add(1, 'day') : today,
    '00:00',
  )
}
export const formatTime = (value: string) => inWarsaw(value).format('HH:mm')
export const formatDateTime = (value: string) => inWarsaw(value).format('D MMM YYYY, HH:mm')
export const initials = (name: string) =>
  name
    .split(' ')
    .filter(Boolean)
    .map((part) => part[0])
    .slice(0, 2)
    .join('')
export { dayjs }
