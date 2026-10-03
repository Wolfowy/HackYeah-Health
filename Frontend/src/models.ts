/** Frontend domain models. ISO 8601 for dates; no PESEL in the browser. */
export type InterviewStatus =
  | 'not_started'
  | 'in_progress'
  | 'awaiting_approval'
  | 'shared'
  | 'needs_supplement'
  | 'expired'
  | 'cancelled'
export type ConversationMode = 'voice' | 'text'
export type VoiceState = 'idle' | 'speaking' | 'listening' | 'paused'
export type Page = 'interview' | 'appointments' | 'appointment' | 'summary' | 'profile'

export interface PatientProfile {
  id: string
  firstName: string
  lastName: string
  email: string
  phone: string
}

export type Session =
  { mode: 'guest'; appointmentId: string } | { mode: 'authenticated'; patient: PatientProfile }

export interface Appointment {
  id: string
  externalAppointmentId: string
  facility: { id: string; name: string; address: string }
  doctor: { id: string; name: string; specialty: string; initials: string }
  scheduledAt: string
  editDeadline: string
  room: string
  status: InterviewStatus
}

export type ReportField =
  | 'reason'
  | 'symptoms'
  | 'timeline'
  | 'medications'
  | 'allergies'
  | 'conditions'
  | 'questions'
  | 'additionalNotes'
export type InformationSource = 'patient' | 'patient_edited' | 'ai_observation' | 'clinician'
export type FieldState = 'provided' | 'missing' | 'unknown' | 'conflicting'

/** Free text is preserved verbatim by the mock. An API adapter can return structured entities. */
export interface ReportSection {
  text: string
  state: FieldState
  source: InformationSource
}

export interface Symptom {
  id: string
  name: string
  startedOn: string | null
  frequency: string | null
  severity: number | null
  dailyImpact: string | null
  description: string | null
}

export interface Medication {
  id: string
  name: string
  dose: string | null
  schedule: string | null
  reason: string | null
}

export interface Observation {
  id: string
  kind: 'new' | 'recurring' | 'changed' | 'insufficient_data'
  description: string
  decision: 'pending' | 'accepted' | 'rejected' | 'edited'
  source: InformationSource
  // Historical evidence remains server-side; the patient receives only the proposal.
}

export interface InterviewReport {
  sections: Record<ReportField, ReportSection>
  observations: Observation[]
}

export interface InterviewQuestion {
  id: string
  stage: 0 | 1 | 2
  field: ReportField
  text: string
  hint: string
  examples: string[]
}

export interface Message {
  id: string
  role: 'assistant' | 'patient'
  text: string
  createdAt: string
  questionId: string | null
  mode: ConversationMode
}

export interface InterviewAnswer {
  questionId: string
  question: string
  text: string
  mode: ConversationMode
  answeredAt: string
}

export interface SummaryVersion {
  id: string
  version: number
  approvedAt: string
  report: InterviewReport
  acknowledgedMissingFields: ReportField[]
}

export interface SharingConsent {
  appointmentId: string
  facilityId: string
  granted: boolean
  grantedAt: string | null
  revokedAt: string | null
}

export interface SupplementRound {
  id: string
  status: 'open' | 'answered' | 'closed'
  questions: { id: string; text: string; answer: string | null }[]
}

export interface Interview {
  appointmentId: string
  status: InterviewStatus
  questionIndex: number
  messages: Message[]
  answers: InterviewAnswer[]
  draft: InterviewReport
  versions: SummaryVersion[]
  consent: SharingConsent
  supplementRounds: SupplementRound[]
  updatedAt: string | null
}

/** Replace this boundary with authenticated API calls without changing the components. */
export interface PatientService {
  signInDemo(email: string): Promise<PatientProfile>
  getAppointments(): Promise<Appointment[]>
  getGuestAppointment(code: string): Promise<Appointment | null>
}
