import assert from 'node:assert/strict'
import { test } from 'node:test'
import { formatVisitSchedule } from './visit'

test('formats the appointment in its time zone, including day rollover and summer time', () => {
  assert.deepEqual(formatVisitSchedule('2026-07-10T23:30:00Z'), {
    date: '11 lipca 2026',
    time: '01:30',
    timeZone: 'Europe/Warsaw',
  })
  assert.equal(formatVisitSchedule('2026-12-10T10:00:00Z')?.time, '11:00')
  assert.equal(formatVisitSchedule('2026-12-10T10:00:00Z', 'America/New_York')?.time, '05:00')
})

test('handles the Warsaw daylight saving transition without browser-local time assumptions', () => {
  assert.equal(formatVisitSchedule('2026-03-29T00:30:00Z')?.time, '01:30')
  assert.equal(formatVisitSchedule('2026-03-29T01:30:00Z')?.time, '03:30')
  assert.equal(formatVisitSchedule('2026-10-25T00:30:00Z')?.time, '02:30')
  assert.equal(formatVisitSchedule('2026-10-25T01:30:00Z')?.time, '02:30')
})

test('missing dates do not display an invalid date and invalid zones fall back to Warsaw', () => {
  assert.equal(formatVisitSchedule(''), null)
  assert.equal(formatVisitSchedule('invalid'), null)
  assert.equal(formatVisitSchedule('2026-12-10T10:00:00Z', 'invalid')?.time, '11:00')
})
