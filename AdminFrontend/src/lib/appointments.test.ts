import assert from 'node:assert/strict'
import test from 'node:test'
import { createMockAppointments } from '../data/mock'
import {
  calendarPlacements,
  canSendInvitation,
  filterAppointments,
  hasConflict,
} from './appointments'
import { atTime, inWarsaw, weekStart } from './date'
import type { Appointment, NewAppointment } from '../models'

const fixture = createMockAppointments()[0]
function visit(time: string, id = time, durationMinutes = 30): Appointment {
  return {
    ...structuredClone(fixture),
    visitId: id,
    scheduledAt: `2026-10-05T${time}:00+02:00`,
    durationMinutes,
    serviceExpiresAt: '2026-10-05T23:59:00+02:00',
    status: 'Shared',
  }
}
function input(time: string, overrides: Partial<NewAppointment> = {}): NewAppointment {
  return {
    patientName: 'Test Demo',
    phone: '+48 500 100 200',
    email: '',
    doctorId: fixture.assignedClinicianId,
    scheduledAt: `2026-10-05T${time}:00+02:00`,
    durationMinutes: 30,
    room: fixture.room,
    visitType: 'InPerson',
    ...overrides,
  }
}

test('week filtering includes Sunday, excludes the next Monday and combines search, doctor and status', () => {
  const monday = visit('09:00')
  const sunday = {
    ...visit('09:00', 'sun'),
    scheduledAt: '2026-10-11T23:45:00+02:00',
  }
  const next = {
    ...visit('09:00', 'next'),
    scheduledAt: '2026-10-12T00:00:00+02:00',
  }
  const result = filterAppointments([monday, sunday, next], {
    search: fixture.patient.name.toUpperCase(),
    status: 'Shared',
    doctorId: fixture.doctor.id,
    date: inWarsaw('2026-10-07'),
    period: 'week',
  })
  assert.deepEqual(
    result.map((item) => item.visitId),
    [monday.visitId, 'sun'],
  )
  assert.equal(
    filterAppointments([monday], {
      search: 'brak pacjenta',
      date: inWarsaw('2026-10-07'),
      period: 'week',
    }).length,
    0,
  )
  assert.equal(weekStart(inWarsaw('2026-10-11')).format('YYYY-MM-DD'), '2026-10-05')
})

test('report list includes approved versions during supplementation and excludes absent reports', () => {
  const shared = visit('09:00')
  const supplement = {
    ...visit('10:00'),
    status: 'RequiresSupplementation' as const,
  }
  const noReport = {
    ...visit('11:00'),
    status: 'AwaitingApproval' as const,
    report: null,
  }
  assert.equal(
    filterAppointments([shared, supplement, noReport], {
      search: '',
      date: inWarsaw(),
      period: 'all',
      reportsOnly: true,
    }).length,
    2,
  )
})

test('invitations are disabled for cancelled, expired and time-expired visits', () => {
  const now = Date.parse('2026-10-05T12:00:00+02:00')
  assert.equal(canSendInvitation(visit('13:00'), now), true)
  assert.equal(canSendInvitation({ ...visit('13:00'), status: 'Cancelled' }, now), false)
  assert.equal(canSendInvitation({ ...visit('13:00'), status: 'Expired' }, now), false)
  assert.equal(canSendInvitation(visit('13:00'), Date.parse('2026-10-06T00:00:00+02:00')), false)
})

test('booking checks clinician and room overlap, but allows adjacent slots and cancelled slots', () => {
  const appointments = [visit('09:00')]
  assert.equal(hasConflict(appointments, input('09:15')), true)
  assert.equal(hasConflict(appointments, input('09:15', { doctorId: 'another' })), true)
  assert.equal(
    hasConflict(appointments, input('09:15', { doctorId: 'another', room: '02' })),
    false,
  )
  assert.equal(hasConflict(appointments, input('09:30')), false)
  assert.equal(hasConflict([{ ...visit('09:00'), status: 'Cancelled' }], input('09:15')), false)
  assert.equal(
    hasConflict(appointments, input('09:15', { doctorId: 'another', visitType: 'Remote' })),
    false,
  )
})

test('chained overlaps use consistent lanes without hiding appointments', () => {
  const appointments = [
    visit('09:00', 'a'),
    visit('09:15', 'b'),
    visit('09:30', 'c'),
    visit('11:00', 'd'),
  ]
  const layout = calendarPlacements(appointments)
  assert.deepEqual(
    layout.map(({ visit: item, column, columnCount }) => [item.visitId, column, columnCount]),
    [
      ['a', 0, 2],
      ['b', 1, 2],
      ['c', 0, 2],
      ['d', 0, 1],
    ],
  )
})

test('Warsaw appointment creation respects daylight saving transitions', () => {
  assert.equal(atTime(inWarsaw('2026-10-23'), '10:00').toISOString(), '2026-10-23T08:00:00.000Z')
  assert.equal(atTime(inWarsaw('2026-10-26'), '10:00').toISOString(), '2026-10-26T09:00:00.000Z')
})

test('week containing daylight saving transition includes the entire final Sunday', () => {
  const sunday = { ...visit('09:00', 'sunday'), scheduledAt: '2026-10-25T23:30:00+01:00' }
  const monday = { ...visit('09:00', 'monday'), scheduledAt: '2026-10-26T00:00:00+01:00' }
  assert.deepEqual(
    filterAppointments([sunday, monday], {
      search: '',
      date: inWarsaw('2026-10-20'),
      period: 'week',
    }).map((item) => item.visitId),
    ['sunday'],
  )
})
