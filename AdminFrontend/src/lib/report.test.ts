import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createMockAppointments } from '../data/mock'
import { conversationSummary } from './report'

test('conversation overview only reflects the immutable approved snapshot', () => {
  const report = createMockAppointments().find((v) => v.report)!.report!
  const text = conversationSummary(report)
  assert.ok(text.includes(report.consultationReason))
  assert.ok(text.includes(report.symptoms[0].name))
  assert.ok(!text.includes('diagnoza'))
  assert.equal(
    conversationSummary({
      ...report,
      consultationReason: '',
      symptoms: [],
      medications: [],
      patientQuestions: [],
      additionalNotes: null,
    }),
    'Brak informacji do podsumowania w zatwierdzonym raporcie.',
  )
})
