import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { appointments, interviewQuestions } from '../data/mock'
import {
  approveReport,
  createInterview,
  editReportField,
  isCurrentVersionApproved,
  missingFields,
  reportForFacility,
  setSharingConsent,
  submitAnswer,
} from './interview'

describe('wywiad i kontrola udostępniania', () => {
  it('zachowuje własne słowa pacjenta i odpowiedzi z obu trybów', () => {
    const first = submitAnswer(
      createInterview(appointments[0]),
      '  Inny powód niż w przykładzie.  ',
      'voice',
    )
    const second = submitAnswer(first, 'Własny opis objawów.', 'text')
    assert.equal(second.draft.sections.reason.text, 'Inny powód niż w przykładzie.')
    assert.equal(second.draft.sections.symptoms.text, 'Własny opis objawów.')
    assert.deepEqual(
      second.answers.map((answer) => answer.mode),
      ['voice', 'text'],
    )
    assert.equal(second.answers.length, 2)
    assert.equal(first.answers.length, 1)
  })

  it('nie dopisuje danych do pominiętych pól i nie zapisuje pustych odpowiedzi', () => {
    const initial = createInterview(appointments[0])
    assert.equal(submitAnswer(initial, '   ', 'text'), initial)
    const answered = submitAnswer(initial, 'Nie wiem.', 'text')
    assert.equal(answered.draft.sections.reason.state, 'unknown')
    assert.equal(answered.draft.sections.medications.state, 'missing')
    assert.equal(answered.draft.sections.medications.text, '')
  })

  it('po pełnym wywiadzie przechodzi do przeglądu i nie dodaje kolejnych odpowiedzi', () => {
    const completed = interviewQuestions.reduce(
      (interview, question) => submitAnswer(interview, question.examples[0], 'text'),
      createInterview(appointments[0]),
    )
    assert.equal(completed.status, 'awaiting_approval')
    assert.equal(missingFields(completed.draft).length, 0)
    assert.equal(submitAnswer(completed, 'Kolejna odpowiedź', 'text'), completed)
  })

  it('niepełny raport wymaga osobnego potwierdzenia, a zatwierdzenie nie udziela zgody', () => {
    const draft = submitAnswer(createInterview(appointments[0]), 'Mój powód.', 'text')
    assert.throws(() => approveReport(draft, false), /niepełnego/)
    const approved = approveReport(draft, true)
    assert.equal(approved.versions.length, 1)
    assert.equal(approved.consent.granted, false)
    assert.equal(reportForFacility(approved), null)
    assert.equal(approved.versions[0].acknowledgedMissingFields.length, 6)
  })

  it('udostępnienie wymaga zatwierdzonej treści, a cofnięcie zgody blokuje dostęp', () => {
    const draft = submitAnswer(createInterview(appointments[0]), 'Mój powód.', 'text')
    assert.throws(() => setSharingConsent(draft, true), /zatwierdź/)
    const shared = setSharingConsent(approveReport(draft, true), true)
    assert.equal(reportForFacility(shared)?.version, 1)
    assert.equal(shared.consent.facilityId, appointments[0].facility.id)
    const revoked = setSharingConsent(shared, false)
    assert.equal(reportForFacility(revoked), null)
    assert.equal(revoked.versions.length, 1)
    assert.ok(revoked.consent.revokedAt)
  })

  it('edycja nie zmienia wersji widocznej placówce; ponowne zatwierdzenie tworzy nową', () => {
    const shared = setSharingConsent(
      approveReport(
        submitAnswer(createInterview(appointments[0]), 'Pierwsza treść.', 'text'),
        true,
      ),
      true,
    )
    const edited = editReportField(shared, 'reason', 'Poprawiona treść.')
    assert.equal(isCurrentVersionApproved(edited), false)
    assert.equal(reportForFacility(edited)?.report.sections.reason.text, 'Pierwsza treść.')
    assert.equal(shared.draft.sections.reason.text, 'Pierwsza treść.')
    const approved = approveReport(edited, true)
    assert.equal(approved.status, 'shared')
    assert.equal(reportForFacility(approved)?.report.sections.reason.text, 'Poprawiona treść.')
    assert.equal(approved.versions[0].report.sections.reason.text, 'Pierwsza treść.')
    assert.equal(approved.versions[1].version, 2)
    assert.equal(approveReport(approved, true), approved)
  })

  it('usuwanie informacji przywraca oznaczenie braku', () => {
    const draft = submitAnswer(createInterview(appointments[0]), 'Mój powód.', 'text')
    const cleared = editReportField(draft, 'reason', '')
    assert.equal(cleared.draft.sections.reason.state, 'missing')
    assert.ok(missingFields(cleared.draft).includes('reason'))
  })

  it('osobne wizyty mają niezależne dane i zgody', () => {
    const first = submitAnswer(createInterview(appointments[0]), 'Pierwsza wizyta.', 'text')
    const second = createInterview(appointments[1])
    assert.equal(second.draft.sections.reason.text, '')
    assert.notEqual(first.consent.appointmentId, second.consent.appointmentId)
  })
})
