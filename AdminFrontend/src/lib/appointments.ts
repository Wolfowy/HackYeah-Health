import { atTime, inWarsaw, weekStart, dayjs } from './date'
import type { Appointment, NewAppointment, VisitStatus } from '../models'

export const statusConfig: Record<
  VisitStatus,
  { label: string; color: string; className: string }
> = {
  NotStarted: {
    label: 'Nie rozpoczęto',
    color: 'default',
    className: 'pending',
  },
  InProgress: {
    label: 'Wywiad w trakcie',
    color: 'blue',
    className: 'progress',
  },
  AwaitingApproval: {
    label: 'Do zatwierdzenia',
    color: 'gold',
    className: 'approval',
  },
  Shared: { label: 'Raport gotowy', color: 'green', className: 'shared' },
  RequiresSupplementation: {
    label: 'Do uzupełnienia',
    color: 'purple',
    className: 'supplement',
  },
  Expired: { label: 'Dostęp wygasł', color: 'default', className: 'expired' },
  Cancelled: { label: 'Anulowana', color: 'default', className: 'cancelled' },
}

export function canSendInvitation(visit: Appointment, now = Date.now()) {
  return (
    visit.status !== 'Cancelled' &&
    visit.status !== 'Expired' &&
    Date.parse(visit.serviceExpiresAt) > now
  )
}

export function filterAppointments(
  visits: Appointment[],
  filters: {
    search: string
    doctorId?: string
    status?: VisitStatus
    date: dayjs.Dayjs
    period: 'week' | 'month' | 'all'
    reportsOnly?: boolean
  },
) {
  const query = filters.search.toLocaleLowerCase('pl').trim()
  const start = filters.period === 'month' ? filters.date.startOf('month') : weekStart(filters.date)
  const end = atTime(
    start.add(filters.period === 'month' ? 1 : 7, filters.period === 'month' ? 'month' : 'day'),
    '00:00',
  )
  return visits
    .filter((visit) => {
      const date = inWarsaw(visit.scheduledAt)
      return (
        (filters.period === 'all' ||
          (date.valueOf() >= start.valueOf() && date.valueOf() < end.valueOf())) &&
        (!filters.doctorId || visit.assignedClinicianId === filters.doctorId) &&
        (!filters.status || visit.status === filters.status) &&
        (!filters.reportsOnly || (visit.reportAvailable ?? visit.report !== null)) &&
        (!query ||
          `${visit.patient.name} ${visit.patient.phone} ${visit.patient.email} ${visit.externalVisitId} ${visit.doctor.name}`
            .toLocaleLowerCase('pl')
            .includes(query))
      )
    })
    .sort((a, b) => Date.parse(a.scheduledAt) - Date.parse(b.scheduledAt))
}

export function hasConflict(visits: Appointment[], input: NewAppointment) {
  const start = Date.parse(input.scheduledAt)
  const end = start + input.durationMinutes * 60_000
  return visits.some(
    (visit) =>
      visit.status !== 'Cancelled' &&
      (visit.assignedClinicianId === input.doctorId ||
        (input.visitType === 'InPerson' &&
          visit.visitType === 'InPerson' &&
          visit.room.trim() === input.room.trim())) &&
      start < Date.parse(visit.scheduledAt) + (visit.durationMinutes ?? 30) * 60_000 &&
      end > Date.parse(visit.scheduledAt),
  )
}

/** Group transitively overlapping appointments before allocating calendar columns. */
export function calendarPlacements(visits: Appointment[]) {
  const result: { visit: Appointment; column: number; columnCount: number }[] = []
  let group: Appointment[] = []
  let groupEnd = 0
  function flush() {
    const laneEnds: number[] = []
    const placed = group.map((visit) => {
      const start = Date.parse(visit.scheduledAt)
      let column = laneEnds.findIndex((end) => end <= start)
      if (column < 0) column = laneEnds.length
      laneEnds[column] = start + (visit.durationMinutes ?? 30) * 60_000
      return { visit, column }
    })
    result.push(...placed.map((item) => ({ ...item, columnCount: laneEnds.length })))
    group = []
  }
  for (const visit of [...visits].sort(
    (a, b) =>
      Date.parse(a.scheduledAt) - Date.parse(b.scheduledAt) || a.visitId.localeCompare(b.visitId),
  )) {
    const start = Date.parse(visit.scheduledAt)
    if (group.length && start >= groupEnd) flush()
    if (!group.length) groupEnd = 0
    group.push(visit)
    groupEnd = Math.max(groupEnd, start + (visit.durationMinutes ?? 30) * 60_000)
  }
  flush()
  return result
}
