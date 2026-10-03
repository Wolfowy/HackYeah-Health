import type {
  Appointment,
  InterviewQuestion,
  PatientProfile,
  PatientService,
  ReportField,
} from '../models'

export const demoPatient: PatientProfile = {
  id: 'demo-patient',
  firstName: 'Aleksandra',
  lastName: 'Kowalska',
  email: 'aleksandra@example.com',
  phone: '+48 500 600 700',
}

function dateIn(days: number, hours: number, minutes: number) {
  const date = new Date()
  date.setDate(date.getDate() + days)
  date.setHours(hours, minutes, 0, 0)
  return date.toISOString()
}

export const appointments: Appointment[] = [
  {
    id: 'demo-appointment-1',
    externalAppointmentId: 'PW-2026-1042',
    facility: {
      id: 'demo-facility',
      name: 'Centrum Medyczne Harmonia',
      address: 'ul. Dobra 12, Warszawa',
    },
    doctor: { id: 'doctor-1', name: 'lek. Anna Nowak', specialty: 'Internista', initials: 'AN' },
    scheduledAt: dateIn(1, 10, 30),
    editDeadline: dateIn(1, 9, 30),
    room: 'Gabinet 204',
    status: 'not_started',
  },
  {
    id: 'demo-appointment-2',
    externalAppointmentId: 'PW-2026-1086',
    facility: {
      id: 'demo-facility',
      name: 'Centrum Medyczne Harmonia',
      address: 'ul. Dobra 12, Warszawa',
    },
    doctor: {
      id: 'doctor-2',
      name: 'lek. Michał Zieliński',
      specialty: 'Kardiolog',
      initials: 'MZ',
    },
    scheduledAt: dateIn(14, 14, 0),
    editDeadline: dateIn(14, 13, 0),
    room: 'Gabinet 108',
    status: 'not_started',
  },
]

export const reportFields: { key: ReportField; label: string; shortLabel: string }[] = [
  { key: 'reason', label: 'Powód wizyty', shortLabel: 'Powód wizyty' },
  { key: 'symptoms', label: 'Objawy i samopoczucie', shortLabel: 'Objawy' },
  { key: 'timeline', label: 'Przebieg dolegliwości', shortLabel: 'Chronologia' },
  { key: 'medications', label: 'Przyjmowane leki', shortLabel: 'Leki' },
  { key: 'allergies', label: 'Alergie i uczulenia', shortLabel: 'Alergie' },
  { key: 'conditions', label: 'Choroby przewlekłe', shortLabel: 'Zdrowie' },
  { key: 'questions', label: 'Pytania do lekarza', shortLabel: 'Twoje pytania' },
  { key: 'additionalNotes', label: 'Dodatkowe informacje', shortLabel: 'Dodatkowe informacje' },
]

export const interviewQuestions: InterviewQuestion[] = [
  {
    id: 'reason',
    stage: 0,
    field: 'reason',
    text: 'Co skłoniło Cię do umówienia wizyty?',
    hint: 'Opowiedz własnymi słowami. Nie musisz używać medycznych określeń.',
    examples: ['Od kilku dni boli mnie głowa.', 'Chcę omówić moje samopoczucie.'],
  },
  {
    id: 'symptoms',
    stage: 1,
    field: 'symptoms',
    text: 'Jakie objawy odczuwasz i jak wpływają na Twój dzień?',
    hint: 'Możesz opisać nasilenie, częstotliwość i to, co Ci przeszkadza.',
    examples: [
      'Ból pojawia się co kilka godzin, utrudnia mi skupienie.',
      'Objawy są łagodne i pojawiają się okresowo.',
    ],
  },
  {
    id: 'timeline',
    stage: 1,
    field: 'timeline',
    text: 'Od kiedy występują dolegliwości? Czy coś się zmieniło?',
    hint: 'Wystarczy przybliżony czas. Jeśli nie pamiętasz, możesz to zaznaczyć.',
    examples: [
      'Od około trzech dni. Wieczorem jest gorzej.',
      'Nie pamiętam dokładnie, od kilku tygodni.',
    ],
  },
  {
    id: 'medications',
    stage: 2,
    field: 'medications',
    text: 'Czy przyjmujesz jakieś leki? W jakiej dawce i z jakiego powodu?',
    hint: 'Uwzględnij leki przyjmowane na stałe, doraźnie i suplementy.',
    examples: ['Paracetamol 500 mg, doraźnie z powodu bólu głowy.', 'Nie przyjmuję żadnych leków.'],
  },
  {
    id: 'allergies',
    stage: 2,
    field: 'allergies',
    text: 'Czy masz alergie lub uczulenia, o których lekarz powinien wiedzieć?',
    hint: 'Jeśli nie wiesz, napisz to wprost — nie będziemy zgadywać.',
    examples: ['Nie mam znanych alergii.', 'Mam uczulenie na penicylinę.'],
  },
  {
    id: 'conditions',
    stage: 2,
    field: 'conditions',
    text: 'Czy leczysz się na choroby przewlekłe?',
    hint: 'Opowiedz też o istotnych informacjach dotyczących Twojego zdrowia.',
    examples: ['Nie leczę się na choroby przewlekłe.', 'Leczę się na nadciśnienie.'],
  },
  {
    id: 'questions',
    stage: 2,
    field: 'questions',
    text: 'O co chcesz zapytać lekarza podczas wizyty?',
    hint: 'To miejsce na wszystko, o czym nie chcesz zapomnieć.',
    examples: [
      'Chcę zapytać, jakie informacje warto dalej obserwować.',
      'Na razie nie mam dodatkowych pytań.',
    ],
  },
]

export const statusLabels = {
  not_started: 'Do przygotowania',
  in_progress: 'W trakcie',
  awaiting_approval: 'Do sprawdzenia',
  shared: 'Udostępniony',
  needs_supplement: 'Do uzupełnienia',
  expired: 'Wygasły',
  cancelled: 'Anulowany',
}

export const mockPatientService: PatientService = {
  async signInDemo(email) {
    return { ...demoPatient, email }
  },
  async getAppointments() {
    return structuredClone(appointments)
  },
  async getGuestAppointment(code) {
    return code.trim().toUpperCase() === 'DEMO2026' ? structuredClone(appointments[0]) : null
  },
}
