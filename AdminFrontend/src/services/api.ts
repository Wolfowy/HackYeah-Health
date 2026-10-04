import type {
  Appointment,
  ReceptionService,
  ReportSnapshot,
  ReportVersion,
  StaffUser,
  VisitStatus,
  DeliveryStatus,
  Doctor,
} from '../models'
import { initials, inWarsaw } from '../lib/date'
import { StaffHttp } from './http'

export interface AdminVisitDto {
  visitId: string
  externalVisitId: string
  scheduledAt: string
  patientName?: string | null
  durationMinutes?: number | null
  endsAt?: string | null
  status: VisitStatus
  deliveryStatus: DeliveryStatus
  hasOpenSupplementationRound: boolean
  visit: null | {
    locationInstructions?: string | null
    serviceExpiresAt: string
    doctor: { id: string | null; name: string | null; specialty: string | null }
    facility: { id: string; name: string | null; address: string | null }
    room: string | null
    visitType: string
  }
}

export interface ReceptionVisitDto {
  visitId: string
  externalVisitId: string
  scheduledAt: string
  serviceExpiresAt: string
  durationMinutes: number
  endsAt?: string | null
  doctor: { id: string; name: string; specialty: string; defaultRoom: string | null } | null
  facility: { id: string; name: string | null; address: string | null }
  room: string | null
  visitType: string
  status: VisitStatus
  patient: { name: string; phone: string | null; email: string | null } | null
  deliveryStatus: DeliveryStatus
  deliveryMode: string
  lastSentAt: string | null
  hasOpenSupplementationRound: boolean
  version: number
  contactChannel: 'Sms' | 'Email'
}

export function mapReceptionVisit(dto: ReceptionVisitDto): Appointment {
  const visit = mapVisit(
    { ...dto, visit: { ...dto, doctor: dto.doctor || { id: null, name: null, specialty: null } } },
    dto.facility.id,
  )
  return {
    ...visit,
    durationMinutes: dto.durationMinutes,
    version: dto.version,
    deliveryMode: dto.deliveryMode,
    patient: dto.patient
      ? { name: dto.patient.name, phone: dto.patient.phone || '', email: dto.patient.email || '' }
      : visit.patient,
    doctor: { ...visit.doctor, defaultRoom: dto.doctor?.defaultRoom },
    invitation: { token: '', channel: dto.contactChannel, lastSentAt: dto.lastSentAt },
  }
}

export function mapVisit(dto: AdminVisitDto, facilityId: string): Appointment {
  const detail = dto.visit
  const name = detail?.doctor.name || 'Lekarz niepodany'
  return {
    visitId: dto.visitId,
    externalVisitId: dto.externalVisitId,
    scheduledAt: dto.scheduledAt,
    endsAt: dto.endsAt,
    serviceExpiresAt: detail?.serviceExpiresAt || dto.scheduledAt,
    timeZone: 'Europe/Warsaw',
    assignedClinicianId: detail?.doctor.id || '',
    doctor: {
      id: detail?.doctor.id || '',
      name,
      specialty: detail?.doctor.specialty || '',
      initials: initials(name),
      color: '#7461dc',
    },
    facility: {
      id: detail?.facility.id || facilityId,
      name: detail?.facility.name || 'Placówka',
      address: detail?.facility.address || '',
    },
    room: detail?.room || '',
    visitType: detail?.visitType === 'Remote' ? 'Remote' : 'InPerson',
    status: dto.status,
    deliveryStatus: dto.deliveryStatus,
    hasOpenSupplementationRound: dto.hasOpenSupplementationRound,
    patient: { name: dto.patientName || `Wizyta ${dto.externalVisitId}`, phone: '', email: '' },
    durationMinutes: dto.durationMinutes ?? null,
    locationInstructions: detail?.locationInstructions || null,
    invitation: { token: '', channel: 'Sms', lastSentAt: null },
    report: null,
    reportAvailable: dto.status === 'Shared' || dto.status === 'RequiresSupplementation',
    activity: [],
  }
}

export function createApiService(http = new StaffHttp()): ReceptionService {
  let user: StaffUser | null = null
  const invitations = new Map<string, Appointment['invitation']>()
  const links = new Map<string, string>()
  const root = '/integration/visits'
  const adminRoot = '/admin/appointments'
  function admin() {
    return user?.role === 'Administrative' || user?.role === 'System'
  }
  function receptionVisit(dto: ReceptionVisitDto) {
    const visit = mapReceptionVisit(dto)
    if (visit.status !== 'Cancelled') visit.invitationUrl = links.get(visit.visitId)
    return visit
  }
  function present(dto: AdminVisitDto) {
    const visit = mapVisit(dto, user?.facilityId || '')
    const invitation = invitations.get(visit.visitId)
    if (invitation && visit.status !== 'Cancelled') visit.invitation = invitation
    return visit
  }
  async function getAppointment(id: string) {
    if (admin()) return receptionVisit(await http.request<ReceptionVisitDto>(`${adminRoot}/${id}`))
    return present(await http.request<AdminVisitDto>(`${root}/${id}/status`))
  }
  return {
    async signIn(email, password) {
      user = await http.login(email, password)
      links.clear()
      invitations.clear()
      return user
    },
    async signOut() {
      try {
        await http.logout()
      } finally {
        user = null
        invitations.clear()
        links.clear()
      }
    },
    async getAppointments() {
      const all: Appointment[] = []
      for (let page = 1; ; page++) {
        const data = admin()
          ? await http.request<{ items: ReceptionVisitDto[]; total: number }>(
              `${adminRoot}?page=${page}&pageSize=100`,
            )
          : await http.request<{ items: AdminVisitDto[]; total: number }>(
              `${root}?page=${page}&pageSize=100`,
            )
        all.push(
          ...data.items.map((dto) =>
            admin() ? receptionVisit(dto as ReceptionVisitDto) : present(dto as AdminVisitDto),
          ),
        )
        if (all.length >= data.total || !data.items.length) return all
      }
    },
    async getDoctors() {
      const doctors =
        await http.request<
          { id: string; name: string; specialty: string; defaultRoom: string | null }[]
        >('/admin/doctors')
      return doctors.map((d): Doctor => ({ ...d, initials: initials(d.name), color: '#7461dc' }))
    },
    async getFacility() {
      const f = await http.request<Appointment['facility']>('/admin/facility')
      return { ...f, name: f.name || 'Placówka', address: f.address || '' }
    },
    async getInvitation(id) {
      const result = await http.request<{ url: string }>(`${adminRoot}/${id}/invitation`)
      links.set(id, result.url)
      return result.url
    },
    async regenerateInvitation(id, channel, contact) {
      if (admin() && !contact) {
        const result = await http.request<{ url: string }>(
          `${adminRoot}/${id}/invitation/regenerate`,
          { method: 'POST', body: JSON.stringify({ channel }) },
        )
        links.set(id, result.url)
        return getAppointment(id)
      }
      const result = await http.request<{ interviewInvitationToken: string }>(
        `${root}/${id}/invitations`,
        { method: 'POST', body: JSON.stringify({ contact }) },
      )
      invitations.set(id, { token: result.interviewInvitationToken, channel, lastSentAt: null })
      if (admin())
        links.set(
          id,
          `${(import.meta.env.VITE_PATIENT_FRONTEND_URL || 'http://127.0.0.1:5173').replace(/\/$/, '')}/i/${encodeURIComponent(result.interviewInvitationToken)}`,
        )
      return getAppointment(id)
    },
    getAppointment,
    async createAppointment(input) {
      if (admin()) {
        const result = await http.request<{
          appointment: ReceptionVisitDto
          invitation: { url: string }
        }>(adminRoot, {
          method: 'POST',
          body: JSON.stringify({
            patientName: input.patientName,
            phone: input.phone || null,
            email: input.email || null,
            doctorId: input.doctorId,
            scheduledAt: input.scheduledAt,
            durationMinutes: input.durationMinutes,
            room: input.room,
            visitType: input.visitType,
            timeZone: 'Europe/Warsaw',
            pesel: input.pesel || null,
            externalVisitId: input.externalVisitId || null,
            sendInvitation: false,
          }),
        })
        links.set(result.appointment.visitId, result.invitation.url)
        return receptionVisit(result.appointment)
      }
      const result = await http.request<{
        visitId: string
        interviewInvitationToken: string
        deliveryStatus: DeliveryStatus
      }>(root, {
        method: 'POST',
        body: JSON.stringify({
          externalVisitId: input.externalVisitId,
          pesel: input.pesel,
          scheduledAt: input.scheduledAt,
          serviceExpiresAt:
            input.serviceExpiresAt || inWarsaw(input.scheduledAt).endOf('day').toISOString(),
          contact: input.channel === 'Email' ? input.email : input.phone,
          channel: input.channel || 'Sms',
          assignedClinicianId: input.doctorId || null,
          doctorName: input.doctorName || null,
          timeZone: 'Europe/Warsaw',
          room: input.room,
          visitType: input.visitType,
        }),
      })
      invitations.set(result.visitId, {
        token: result.interviewInvitationToken,
        channel: input.channel || 'Sms',
        lastSentAt: new Date().toISOString(),
      })
      return getAppointment(result.visitId)
    },
    async sendInvitation(id, channel, contact) {
      if (admin())
        return receptionVisit(
          await http.request<ReceptionVisitDto>(`${adminRoot}/${id}/invitation/send`, {
            method: 'POST',
            body: JSON.stringify({ channel }),
          }),
        )
      const result = await http.request<{ interviewInvitationToken: string }>(
        `${root}/${id}/invitations`,
        { method: 'POST', body: JSON.stringify({ contact }) },
      )
      invitations.set(id, {
        token: result.interviewInvitationToken,
        channel,
        lastSentAt: new Date().toISOString(),
      })
      return getAppointment(id)
    },
    async updateAppointment(visit, input) {
      if (admin()) {
        const context = await http.request<AdminVisitDto>(`${root}/${visit.visitId}/status`)
        const updated = await http.request<ReceptionVisitDto>(`${adminRoot}/${visit.visitId}`, {
          method: 'PUT',
          body: JSON.stringify({
            ...input,
            durationMinutes: input.durationMinutes || visit.durationMinutes || 30,
            doctorId: visit.assignedClinicianId,
            visitType: visit.visitType,
            timeZone: 'Europe/Warsaw',
            expectedVersion: visit.version,
            locationInstructions: context.visit?.locationInstructions || null,
          }),
        })
        return receptionVisit(updated)
      }
      await http.request(`${root}/${visit.visitId}`, {
        method: 'PUT',
        body: JSON.stringify({
          ...input,
          timeZone: 'Europe/Warsaw',
          assignedClinicianId: visit.assignedClinicianId || null,
          doctorName: visit.doctor.name === 'Lekarz niepodany' ? null : visit.doctor.name,
          doctorSpecialty: visit.doctor.specialty || null,
          facilityName: visit.facility.name,
          facilityAddress: visit.facility.address || null,
          visitType: visit.visitType,
          locationInstructions: visit.locationInstructions || null,
        }),
      })
      return getAppointment(visit.visitId)
    },
    async cancelAppointment(id) {
      await http.request(`${admin() ? adminRoot : root}/${id}/cancel`, { method: 'POST' })
      invitations.delete(id)
      links.delete(id)
      return getAppointment(id)
    },
    getReportVersions: (id) => http.request<ReportVersion[]>(`${root}/${id}/report-versions`),
    async getReport(id, versionId) {
      const data = await http.request<{ report: ReportSnapshot }>(
        `${root}/${id}/report-versions/${versionId}`,
      )
      return data.report
    },
    getReportPdf: (id, versionId) =>
      http.request<Blob>(`${root}/${id}/report-versions/${versionId}/pdf`, {}, true, true, true),
    async addQuestions(id, questions) {
      await http.request(`${root}/${id}/supplementation-round/questions`, {
        method: 'POST',
        body: JSON.stringify({ questions }),
      })
    },
  }
}
