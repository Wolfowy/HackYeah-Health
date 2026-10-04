import assert from 'node:assert/strict'
import { test } from 'node:test'
import { appendNotes, replaceDraftCommand, emptyFieldText } from './patient-report'
import { reportFixture } from '../../tests/report-fixture'

test('adding notes preserves clinical data and uses the original revision', () => {
  const draft = reportFixture().draft
  const command = replaceDraftCommand(appendNotes(draft, '  Dodatkowe informacje.  '))
  assert.equal(command.expectedRevision, 4)
  assert.equal(command.additionalNotes, 'Wcześniejsze uwagi\n\nDodatkowe informacje.')
  assert.deepEqual(command.symptoms, draft.symptoms)
  assert.deepEqual(command.medications, draft.medications)
  assert.equal(command.medications[0].doseState, 'Unknown')
  assert.equal(command.chronicConditionsState, 'NotAsked')
  assert.ok(!('revision' in command) && !('clarifications' in command))
  assert.equal(draft.additionalNotes, 'Wcześniejsze uwagi')
})

test('unknown and unanswered empty lists never appear as confirmed absence', () => {
  assert.equal(emptyFieldText('Provided'), 'Nie zgłoszono')
  assert.equal(emptyFieldText('Unknown'), 'Pacjent nie wie')
  assert.equal(emptyFieldText('NotAsked'), 'Nie zebrano informacji')
  assert.equal(emptyFieldText('Contradictory'), 'Informacje wymagają wyjaśnienia')
})
