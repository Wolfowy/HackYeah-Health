/** Status names follow DocPrep.Domain.Visits.VisitStatus. */
export type VisitStatus =
  | 'NotStarted'
  | 'InProgress'
  | 'AwaitingApproval'
  | 'Shared'
  | 'RequiresSupplementation'
  | 'Expired'
  | 'Cancelled'
export type DeliveryStatus = 'Pending' | 'Delivered' | 'Failed' | 'NotSent'
export type DataMode = 'api' | 'demo'
export type ContactChannel = 'Sms' | 'Email'

export interface StaffUser {
  id: string
  facilityId: string
  displayName: string
  email: string
  role: 'Administrative' | 'Clinician' | 'System'
  clinicianId?: string | null
}

export interface Doctor {
  id: string
  name: string
  specialty: string
  initials: string
  defaultRoom?: string | null
  color: string
}

/** Mirrors ReportSnapshot. Only immutable, approved versions appear in the UI. */
export interface ReportSnapshot {
  versionId: string
  versionNumber: number
  schemaVersion: number
  visitId: string
  externalVisitId: string
  facilityId: string
  scheduledAt: string
  approvedAt: string
  consultationReason: string
  symptoms: {
    name: string
    startedOn: string | null
    frequency: string | null
    severity: number | null
    dailyImpact: string | null
    description: string | null
    source: string
    timeline: {
      occurredOn: string | null
      period: string | null
      description: string
    }[]
  }[]
  medications: {
    name: string
    dose: string | null
    schedule: string | null
    reason: string | null
    source: string
  }[]
  allergies: { substance: string; reaction: string | null; source: string }[]
  chronicConditions: {
    name: string
    description: string | null
    source: string
  }[]
  patientQuestions: string[]
  clarifications: { fieldPath: string; kind: string; message: string }[]
  observations: {
    observationId: string
    symptomName: string
    kind: string
    text: string
    editedByPatient: boolean
    source: string
  }[]
  supplementationAnswers: {
    question: string
    answer: string
    mode: string
    source: string
  }[]
  confirmedIncomplete: boolean
  additionalNotes: string | null
}

/** Shared presentation model; older integration responses may omit patient metadata. */
export interface Appointment {
  visitId: string
  externalVisitId: string
  scheduledAt: string
  endsAt?: string | null
  serviceExpiresAt: string
  timeZone: 'Europe/Warsaw'
  assignedClinicianId: string
  doctor: Doctor
  facility: { id: string; name: string; address: string }
  room: string
  visitType: 'InPerson' | 'Remote'
  status: VisitStatus
  deliveryStatus: DeliveryStatus
  hasOpenSupplementationRound: boolean
  patient: { name: string; phone: string; email: string }
  durationMinutes: number | null
  locationInstructions?: string | null
  deliveryMode?: string
  version?: number
  invitationUrl?: string
  reportAvailable?: boolean
  invitation: {
    token: string
    channel: ContactChannel
    lastSentAt: string | null
  }
  report: ReportSnapshot | null
  activity: { id: string; at: string; title: string; description: string }[]
}

export interface NewAppointment {
  patientName: string
  phone: string
  email: string
  doctorId: string
  scheduledAt: string
  durationMinutes: number
  room: string
  visitType: 'InPerson' | 'Remote'
  externalVisitId?: string
  pesel?: string
  channel?: ContactChannel
  serviceExpiresAt?: string
  doctorName?: string
}

export interface ReportVersion {
  versionId: string
  versionNumber: number
  approvedAt: string
  confirmedIncomplete: boolean
}

export interface UpdateAppointment {
  scheduledAt: string
  serviceExpiresAt: string
  room: string
  durationMinutes?: number
}

/** Boundary shared by the explicit demo and authenticated API adapters. */
export interface ReceptionService {
  signIn(email: string, password: string): Promise<StaffUser>
  getAppointments(): Promise<Appointment[]>
  createAppointment(input: NewAppointment): Promise<Appointment>
  signOut(): Promise<void>
  getDoctors(): Promise<Doctor[]>
  getFacility(): Promise<Appointment['facility']>
  getInvitation(visitId: string): Promise<string>
  regenerateInvitation(
    visitId: string,
    channel: ContactChannel,
    contact?: string,
  ): Promise<Appointment>
  sendInvitation(visitId: string, channel: ContactChannel, contact?: string): Promise<Appointment>
  getAppointment(visitId: string): Promise<Appointment>
  updateAppointment(visit: Appointment, input: UpdateAppointment): Promise<Appointment>
  cancelAppointment(visitId: string): Promise<Appointment>
  getReportVersions(visitId: string): Promise<ReportVersion[]>
  getReport(visitId: string, versionId: string): Promise<ReportSnapshot>
  getReportPdf(visitId: string, versionId: string): Promise<Blob>
  addQuestions(visitId: string, questions: string[]): Promise<void>
}
