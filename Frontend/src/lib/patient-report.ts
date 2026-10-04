/** DTOs for /api/v1/interview, separate from the demonstration model. */
export type ReportFieldState = 'Provided' | 'Unknown' | 'NotAsked' | 'Contradictory'
export interface TimelineEntry {
  occurredOn: string | null
  period: string | null
  description: string
}
export interface ReportSymptom {
  name: string
  startedOn: string | null
  startedOnState: ReportFieldState
  frequency: string | null
  severity: number | null
  dailyImpact: string | null
  description: string | null
  timeline: TimelineEntry[]
}
export interface ReportMedication {
  name: string
  dose: string | null
  doseState: ReportFieldState
  schedule: string | null
  reason: string | null
}
export interface ReportIssue {
  fieldPath: string
  kind: string
  message: string
}
export interface ReportDraft {
  consultationReason: string | null
  revision: number
  symptoms: ReportSymptom[]
  medications: ReportMedication[]
  allergies: { substance: string; reaction: string | null }[]
  chronicConditions: { name: string; description: string | null }[]
  questions: string[]
  additionalNotes: string | null
  medicationsState: ReportFieldState
  allergiesState: ReportFieldState
  chronicConditionsState: ReportFieldState
  clarifications: ReportIssue[]
}
export type ObservationDecision = 'Pending' | 'Accepted' | 'Rejected' | 'EditedAndAccepted'
export interface PatientInterview {
  visitId: string
  scheduledAt: string
  serviceExpiresAt: string
  status: string
  draft: ReportDraft
  latestVersion: number | null
  consentActive: boolean
  observations: {
    id: string
    symptomName: string
    kind: string
    text: string
    decision: ObservationDecision
    editedByPatient: boolean
  }[]
  supplementationRound: {
    id: string
    number: number
    status: string
    questions: { id: string; text: string; answer: string | null; mode: 'Text' | 'Voice' | null }[]
  } | null
}
export interface ReportVersion {
  versionId: string
  versionNumber: number
  approvedAt: string
  confirmedIncomplete: boolean
}

/** Send a whole draft and its revision, preserving untouched clinical data and unknown states. */
export function replaceDraftCommand(draft: ReportDraft) {
  const { revision, clarifications: _clarifications, ...fields } = draft
  return { ...fields, expectedRevision: revision }
}

export function emptyFieldText(state: ReportFieldState) {
  return (
    {
      Provided: 'Nie zgłoszono',
      Unknown: 'Pacjent nie wie',
      NotAsked: 'Nie zebrano informacji',
      Contradictory: 'Informacje wymagają wyjaśnienia',
    }[state] ?? 'Nie zebrano informacji'
  )
}

export function appendNotes(draft: ReportDraft, text: string): ReportDraft {
  return {
    ...draft,
    additionalNotes: [draft.additionalNotes, text.trim()].filter(Boolean).join('\n\n'),
  }
}
