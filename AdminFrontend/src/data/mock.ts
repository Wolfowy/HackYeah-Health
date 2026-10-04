import type { Appointment, Doctor, ReportSnapshot } from '../models'
import { atTime, initialDate, inWarsaw } from '../lib/date'

export const DEMO_EMAIL = 'admin@docprep.local'
export const DEMO_PASSWORD = 'DocPrepDemo!2026'
export const facility = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'Przychodnia Dobra',
  address: 'ul. Długa 24, Warszawa',
}
export const doctors: Doctor[] = [
  {
    id: 'doctor-kowalska',
    name: 'dr Anna Kowalska',
    specialty: 'Medycyna rodzinna',
    initials: 'AK',
    color: '#7764d8',
  },
  {
    id: 'doctor-wisniewski',
    name: 'dr Piotr Wiśniewski',
    specialty: 'Internista',
    initials: 'PW',
    color: '#3b938f',
  },
  {
    id: 'doctor-zielinska',
    name: 'dr Maria Zielińska',
    specialty: 'Kardiolog',
    initials: 'MZ',
    color: '#c88b48',
  },
]

function makeReport(visit: Appointment, index: number): ReportSnapshot {
  const cardio = visit.doctor.id === 'doctor-zielinska'
  return {
    versionId: `report-${visit.visitId}`,
    versionNumber: (index % 2) + 1,
    schemaVersion: 1,
    visitId: visit.visitId,
    externalVisitId: visit.externalVisitId,
    facilityId: facility.id,
    scheduledAt: visit.scheduledAt,
    approvedAt: atTime(inWarsaw().subtract(1, 'day'), '18:42').toISOString(),
    consultationReason: cardio
      ? 'Kontrola okresowa i omówienie domowych pomiarów ciśnienia.'
      : 'Konsultacja z powodu nawracających bólów głowy i zmęczenia w ostatnim tygodniu.',
    symptoms: [
      {
        name: cardio ? 'Kołatanie serca' : 'Ból głowy',
        startedOn: initialDate().subtract(7, 'day').format('YYYY-MM-DD'),
        frequency: 'Kilka razy w tygodniu',
        severity: 4,
        dailyImpact: 'Utrudnia skupienie podczas pracy.',
        description: 'Pacjent zgłasza częstsze występowanie dolegliwości wieczorem.',
        source: 'patient',
        timeline: [
          {
            occurredOn: initialDate().subtract(7, 'day').format('YYYY-MM-DD'),
            period: null,
            description: 'Pierwsze zauważone dolegliwości.',
          },
        ],
      },
    ],
    medications: [
      {
        name: 'Lek zgłoszony przez pacjenta',
        dose: null,
        schedule: 'Według dotychczasowego schematu',
        reason: 'Stałe leczenie',
        source: 'patient',
      },
    ],
    allergies: [
      {
        substance: 'Pyłki traw',
        reaction: 'Sezonowy katar',
        source: 'patient',
      },
    ],
    chronicConditions: [
      {
        name: 'Nadciśnienie tętnicze',
        description: 'Rozpoznane według relacji pacjenta w 2022 roku.',
        source: 'patient',
      },
    ],
    patientQuestions: [
      'Czy przynieść dodatkowe wyniki badań?',
      'Jakie informacje zapisywać przed kolejną wizytą?',
    ],
    clarifications: [
      {
        fieldPath: 'medications[0].dose',
        kind: 'missing',
        message: 'Pacjent nie podał nazwy ani dawki leku. Do wyjaśnienia podczas wizyty.',
      },
    ],
    observations: [
      {
        observationId: `observation-${index}`,
        symptomName: 'Zmęczenie',
        kind: 'New',
        text: 'Pacjent wskazuje, że zmęczenie pojawiło się w ostatnim tygodniu.',
        editedByPatient: false,
        source: 'ai_observation',
      },
    ],
    supplementationAnswers: visit.hasOpenSupplementationRound
      ? [
          {
            question: 'Od kiedy obserwuje Pan/Pani dolegliwości?',
            answer: 'Od około tygodnia.',
            mode: 'Text',
            source: 'patient',
          },
        ]
      : [],
    confirmedIncomplete: true,
    additionalNotes:
      'Pacjent przyniesie na wizytę listę przyjmowanych leków. Wszystkie informacje w tej wersji demonstracyjnej są fikcyjne.',
  }
}

export function createMockAppointments(): Appointment[] {
  const base = initialDate()
  const historyDate = inWarsaw().startOf('day')
  const seed: [
    number,
    string,
    number,
    string,
    Appointment['status'],
    Appointment['deliveryStatus'],
  ][] = [
    [0, '08:30', 0, 'Jan Malinowski', 'Shared', 'Delivered'],
    [0, '09:30', 1, 'Zofia Wójcik', 'InProgress', 'Delivered'],
    [0, '10:30', 2, 'Michał Lewandowski', 'NotStarted', 'Pending'],
    [0, '11:30', 0, 'Alicja Kamińska', 'AwaitingApproval', 'Delivered'],
    [0, '13:00', 1, 'Tomasz Dąbrowski', 'Shared', 'Delivered'],
    [0, '14:30', 2, 'Helena Szymańska', 'RequiresSupplementation', 'Delivered'],
    [0, '16:00', 0, 'Marek Woźniak', 'NotStarted', 'Failed'],
    [1, '09:00', 0, 'Natalia Kozłowska', 'Shared', 'Delivered'],
    [1, '11:00', 1, 'Adam Jankowski', 'NotStarted', 'Pending'],
    [1, '13:30', 2, 'Ewa Mazur', 'InProgress', 'Delivered'],
    [1, '15:00', 0, 'Paweł Krawczyk', 'AwaitingApproval', 'Delivered'],
    [2, '08:30', 1, 'Karolina Piotrowska', 'Shared', 'Delivered'],
    [2, '10:00', 2, 'Andrzej Grabowski', 'RequiresSupplementation', 'Delivered'],
    [2, '12:00', 0, 'Monika Nowak', 'NotStarted', 'Pending'],
    [2, '14:00', 1, 'Bartosz Król', 'Cancelled', 'Delivered'],
    [3, '09:30', 2, 'Irena Wieczorek', 'Shared', 'Delivered'],
    [3, '11:00', 0, 'Jakub Jabłoński', 'NotStarted', 'Pending'],
    [3, '14:30', 1, 'Dorota Dudek', 'InProgress', 'Delivered'],
    [4, '09:00', 0, 'Krzysztof Adamczyk', 'Shared', 'Delivered'],
    [4, '10:30', 2, 'Magdalena Sikora', 'AwaitingApproval', 'Delivered'],
    [4, '13:00', 1, 'Wojciech Walczak', 'NotStarted', 'Pending'],
    [7, '09:00', 0, 'Barbara Lis', 'NotStarted', 'Pending'],
    [-3, '10:00', 1, 'Robert Zając', 'Expired', 'Delivered'],
  ]
  return seed.map(([offset, time, doctorIndex, name, status, deliveryStatus], index) => {
    const date = base.add(offset, 'day')
    const scheduledAt = atTime(date, time).toISOString()
    const doctor = doctors[doctorIndex]
    const visit: Appointment = {
      visitId: `demo-visit-${index + 1}`,
      externalVisitId: `WIZ-${String(index + 1).padStart(4, '0')}`,
      scheduledAt,
      serviceExpiresAt: atTime(date, '23:59').toISOString(),
      timeZone: 'Europe/Warsaw',
      assignedClinicianId: doctor.id,
      doctor,
      facility,
      room: String(doctorIndex + 1).padStart(2, '0'),
      visitType: index === 9 ? 'Remote' : 'InPerson',
      status,
      deliveryStatus,
      hasOpenSupplementationRound: status === 'RequiresSupplementation',
      patient: {
        name,
        phone: `+48 500 100 ${String(index + 100).padStart(3, '0')}`,
        email: `pacjent${index + 1}@example.com`,
      },
      durationMinutes: 45,
      invitation: {
        token: `demo-invitation-${index + 1}`,
        channel: index % 3 === 0 ? 'Email' : 'Sms',
        lastSentAt:
          deliveryStatus === 'Delivered'
            ? atTime(historyDate.subtract(2, 'day'), '12:00').toISOString()
            : null,
      },
      report: null,
      activity: [
        {
          id: `created-${index}`,
          at: atTime(historyDate.subtract(3, 'day'), '09:15').toISOString(),
          title: 'Utworzono wizytę',
          description: 'Wizyta została dodana do kalendarza placówki.',
        },
      ],
    }
    if (deliveryStatus === 'Delivered')
      visit.activity.push({
        id: `invited-${index}`,
        at: visit.invitation.lastSentAt!,
        title: 'Dostarczono zaproszenie',
        description: `Kanał: ${visit.invitation.channel === 'Sms' ? 'SMS' : 'e-mail'}.`,
      })
    if (deliveryStatus === 'Failed')
      visit.activity.push({
        id: `failed-${index}`,
        at: atTime(historyDate.subtract(2, 'day'), '12:00').toISOString(),
        title: 'Nie dostarczono zaproszenia',
        description: 'Przykładowy błąd operatora. Można ponowić wysyłkę.',
      })
    if (status === 'Shared' || status === 'RequiresSupplementation') {
      visit.report = makeReport(visit, index)
      visit.activity.push({
        id: `report-${index}`,
        at: visit.report.approvedAt,
        title: 'Udostępniono raport',
        description: `Pacjent zatwierdził wersję ${visit.report.versionNumber} i udzielił zgody.`,
      })
    }
    return visit
  })
}
