import type { Appointment, ReceptionService, DataMode } from '../models'
import { createMockAppointments, DEMO_EMAIL, DEMO_PASSWORD, doctors, facility } from '../data/mock'
import { canSendInvitation, hasConflict } from '../lib/appointments'
import { atTime, inWarsaw } from '../lib/date'
import { createApiService } from './api'

let appointments = createMockAppointments()
if (!appointments.some((v) => inWarsaw(v.scheduledAt).isSame(inWarsaw(), 'day'))) {
  const today = appointments.slice(0, 4).map((v, i) => ({
    ...structuredClone(v),
    visitId: `today-${i}`,
    externalVisitId: `DZIS-${i + 1}`,
    scheduledAt: atTime(inWarsaw(), ['08:30', '09:30', '10:30', '11:30'][i]).toISOString(),
    serviceExpiresAt: inWarsaw().endOf('day').toISOString(),
    assignedClinicianId: doctors[0].id,
    doctor: doctors[0],
  }))
  today.forEach((v) => {
    if (v.report) {
      v.report = {
        ...v.report,
        visitId: v.visitId,
        externalVisitId: v.externalVisitId,
        scheduledAt: v.scheduledAt,
        versionId: `report-${v.visitId}`,
      }
    }
    v.invitation.token = `demo-invitation-${v.visitId}`
  })
  appointments.push(...today)
}
const copy = <T>(value: T): T => structuredClone(value)

const mockService: ReceptionService = {
  async signIn(email, password) {
    if (
      ![DEMO_EMAIL, 'doctor@docprep.local'].includes(email.trim().toLowerCase()) ||
      password !== DEMO_PASSWORD
    )
      throw new Error('Użyj danych konta demonstracyjnego podanych poniżej.')
    return {
      id: email.startsWith('doctor') ? 'demo-doctor' : 'demo-receptionist',
      facilityId: facility.id,
      displayName: email.startsWith('doctor') ? 'dr Anna Kowalska' : 'Anna Nowak',
      email,
      role: email.startsWith('doctor') ? 'Clinician' : 'Administrative',
      clinicianId: email.startsWith('doctor') ? doctors[0].id : null,
    }
  },
  async getDoctors() {
    return copy(doctors)
  },
  async getFacility() {
    return copy(facility)
  },
  async getInvitation(id) {
    const visit = await this.getAppointment(id)
    return `https://przedwizyta.example/i/${visit.invitation.token}`
  },
  async regenerateInvitation(id, channel) {
    return this.sendInvitation(id, channel)
  },
  async signOut() {},
  async getAppointment(id) {
    const visit = appointments.find((v) => v.visitId === id)
    if (!visit) throw new Error('Wizyta nie jest dostępna.')
    return copy(visit)
  },
  async updateAppointment(visit, input) {
    if (
      Date.parse(input.scheduledAt) <= Date.now() ||
      Date.parse(input.serviceExpiresAt) <= Date.parse(input.scheduledAt)
    )
      throw new Error('Podaj przyszły termin i późniejszy koniec dostępu.')
    const updated = { ...visit, ...input }
    appointments = appointments.map((v) => (v.visitId === visit.visitId ? updated : v))
    return copy(updated)
  },
  async cancelAppointment(id) {
    const visit = await this.getAppointment(id)
    const updated: Appointment = {
      ...visit,
      status: 'Cancelled',
      report: null,
      reportAvailable: false,
    }
    appointments = appointments.map((v) => (v.visitId === id ? updated : v))
    return copy(updated)
  },
  async getReportVersions(id) {
    const visit = await this.getAppointment(id)
    return visit.report
      ? [
          {
            versionId: visit.report.versionId,
            versionNumber: visit.report.versionNumber,
            approvedAt: visit.report.approvedAt,
            confirmedIncomplete: visit.report.confirmedIncomplete,
          },
        ]
      : []
  },
  async getReport(id, versionId) {
    const visit = await this.getAppointment(id)
    if (!visit.report || visit.report.versionId !== versionId)
      throw new Error('Raport nie jest dostępny.')
    return copy(visit.report)
  },
  async getReportPdf() {
    throw new Error('PDF jest dostępny po połączeniu z placówką.')
  },
  async addQuestions(id, questions) {
    const visit = await this.getAppointment(id)
    const updated: Appointment = {
      ...visit,
      status: 'RequiresSupplementation',
      hasOpenSupplementationRound: true,
      activity: [
        ...visit.activity,
        {
          id: crypto.randomUUID(),
          at: new Date().toISOString(),
          title: 'Pytania uzupełniające',
          description: questions.join(' • '),
        },
      ],
    }
    appointments = appointments.map((v) => (v.visitId === id ? updated : v))
  },
  async getAppointments() {
    return copy(appointments)
  },
  async createAppointment(input) {
    const doctor = doctors.find((item) => item.id === input.doctorId)
    if (!doctor) throw new Error('Wybierz lekarza.')
    if (Date.parse(input.scheduledAt) <= Date.now())
      throw new Error('Termin wizyty musi być w przyszłości.')
    if (hasConflict(appointments, input))
      throw new Error('Lekarz lub gabinet ma już wizytę w tym terminie. Wybierz inną godzinę.')
    const id = crypto.randomUUID()
    const visit: Appointment = {
      visitId: id,
      externalVisitId: `WIZ-${String(appointments.length + 1).padStart(4, '0')}`,
      scheduledAt: input.scheduledAt,
      serviceExpiresAt: inWarsaw(input.scheduledAt).endOf('day').toISOString(),
      timeZone: 'Europe/Warsaw',
      assignedClinicianId: doctor.id,
      doctor,
      facility,
      room: input.room.trim(),
      visitType: input.visitType,
      status: 'NotStarted',
      deliveryStatus: 'Pending',
      hasOpenSupplementationRound: false,
      patient: {
        name: input.patientName.trim(),
        phone: input.phone.trim(),
        email: input.email.trim(),
      },
      durationMinutes: input.durationMinutes,
      invitation: {
        token: `demo-${id}`,
        channel: input.phone.trim() ? 'Sms' : 'Email',
        lastSentAt: null,
      },
      report: null,
      activity: [
        {
          id: crypto.randomUUID(),
          at: new Date().toISOString(),
          title: 'Utworzono wizytę',
          description: 'Wizyta dodana przez recepcję w wersji demo.',
        },
      ],
    }
    appointments = [...appointments, visit]
    return copy(visit)
  },
  async sendInvitation(visitId, channel) {
    const visit = appointments.find((item) => item.visitId === visitId)
    if (!visit || !canSendInvitation(visit))
      throw new Error('Zaproszenie do tej wizyty nie jest już aktywne.')
    if (!(channel === 'Sms' ? visit.patient.phone : visit.patient.email))
      throw new Error('Brak danych kontaktowych dla wybranego kanału.')
    const at = new Date().toISOString()
    const updated: Appointment = {
      ...visit,
      deliveryStatus: 'Delivered',
      invitation: { ...visit.invitation, channel, lastSentAt: at },
      activity: [
        ...visit.activity,
        {
          id: crypto.randomUUID(),
          at,
          title: 'Symulacja wysyłki zaproszenia',
          description: `Kanał: ${channel === 'Sms' ? 'SMS' : 'e-mail'}. Żadna wiadomość nie została wysłana.`,
        },
      ],
    }
    appointments = appointments.map((item) => (item.visitId === visitId ? updated : item))
    return copy(updated)
  },
}

const apiService = createApiService()
export let dataMode: DataMode = 'api'
export function selectDataMode(mode: DataMode) {
  dataMode = mode
}
export function service(): ReceptionService {
  return dataMode === 'demo' ? mockService : apiService
}
export const receptionService = new Proxy({} as ReceptionService, {
  get(_, key: keyof ReceptionService) {
    return service()[key].bind(service())
  },
})
/** Invitation secrets returned by API are kept only in memory. */
export function invitationUrl(visit: Appointment) {
  if (visit.invitationUrl) return visit.invitationUrl
  if (!visit.invitation.token) return ''
  const origin =
    dataMode === 'demo'
      ? 'https://przedwizyta.example'
      : import.meta.env.VITE_PATIENT_FRONTEND_URL || 'http://127.0.0.1:5173'
  return `${origin.replace(/\/$/, '')}/i/${encodeURIComponent(visit.invitation.token)}`
}
