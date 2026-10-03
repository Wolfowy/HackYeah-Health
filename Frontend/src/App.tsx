import { useEffect, useRef, useState } from 'react'
import {
  ArrowRight,
  CalendarDays,
  Check,
  ChevronDown,
  ChevronRight,
  FileText,
  Heart,
  LockKeyhole,
  LogIn,
  LogOut,
  Menu,
  MessageCircle,
  ShieldCheck,
  UserRound,
  X,
} from 'lucide-react'
import { appointments, mockPatientService } from './data/mock'
import { createInterview } from './lib/interview'
import { submitAnswer } from './lib/interview'
import type { ConversationMode, Interview, Page, PatientProfile, Session } from './models'
import { Conversation } from './components/Conversation'
import { VisitContext } from './components/VisitContext'
import { Summary } from './components/Summary'
import { Appointments, AppointmentDetails } from './components/Appointments'
import { Login } from './components/Login'
import { Profile } from './components/Profile'
import { Modal } from './components/Modal'

const paths: Record<Page, string> = {
  interview: 'wywiad',
  appointments: 'wizyty',
  appointment: 'moja-wizyta',
  summary: 'podsumowanie',
  profile: 'konto',
}
function currentRoute() {
  return window.location.hash.replace(/^#\/?/, '') || 'wywiad'
}
function initialInterviews() {
  return Object.fromEntries(
    appointments.map((appointment) => [appointment.id, createInterview(appointment)]),
  )
}

export default function App() {
  const [session, setSession] = useState<Session>({
    mode: 'guest',
    appointmentId: appointments[0].id,
  })
  const [route, setRoute] = useState(currentRoute)
  const [selectedId, setSelectedId] = useState(appointments[0].id)
  const [interviews, setInterviews] = useState<Record<string, Interview>>(initialInterviews)
  const [menuOpen, setMenuOpen] = useState(false)
  const [accountOpen, setAccountOpen] = useState(false)
  const [toast, setToast] = useState('')
  const [info, setInfo] = useState(false)
  const [codeModal, setCodeModal] = useState(false)
  const [code, setCode] = useState('')
  const [codeError, setCodeError] = useState('')
  const [resetModal, setResetModal] = useState(false)
  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const authenticated = session.mode === 'authenticated'
  const appointment = appointments.find((item) => item.id === selectedId)!
  const interview = interviews[selectedId]
  const routePage =
    (Object.keys(paths) as Page[]).find((key) => paths[key] === route) ?? 'interview'
  const page =
    !authenticated && (routePage === 'appointments' || routePage === 'profile')
      ? 'interview'
      : routePage

  useEffect(() => {
    const update = () => {
      setRoute(currentRoute())
      setMenuOpen(false)
      setAccountOpen(false)
    }
    window.addEventListener('hashchange', update)
    return () => window.removeEventListener('hashchange', update)
  }, [])
  useEffect(
    () => () => {
      if (toastTimer.current) clearTimeout(toastTimer.current)
    },
    [],
  )
  useEffect(() => {
    document.title = `${route === 'logowanie' ? 'Logowanie' : { interview: 'Twój wywiad', appointments: 'Moje wizyty', appointment: 'Moja wizyta', summary: 'Podsumowanie', profile: 'Moje konto' }[page]} — Przed wizytą`
  }, [page, route])
  useEffect(() => {
    if (!accountOpen) return
    const close = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setAccountOpen(false)
    }
    window.addEventListener('keydown', close)
    return () => window.removeEventListener('keydown', close)
  }, [accountOpen])

  function navigate(next: Page | 'login') {
    window.location.hash = `/${next === 'login' ? 'logowanie' : paths[next]}`
    setMenuOpen(false)
    setAccountOpen(false)
  }
  function notify(message: string) {
    if (toastTimer.current) clearTimeout(toastTimer.current)
    setToast(message)
    toastTimer.current = setTimeout(() => setToast(''), 4500)
  }
  function updateInterview(next: Interview) {
    setInterviews((previous) => ({ ...previous, [next.appointmentId]: next }))
  }
  function answer(text: string, mode: ConversationMode) {
    setInterviews((previous) => ({
      ...previous,
      [selectedId]: submitAnswer(previous[selectedId], text, mode),
    }))
  }
  async function signIn(email: string) {
    const patient = await mockPatientService.signInDemo(email)
    setSession({ mode: 'authenticated', patient })
    navigate('interview')
    notify('Otwarto konto demonstracyjne. Twoja rozmowa została zachowana.')
  }
  function logout() {
    setSession({ mode: 'guest', appointmentId: appointments[0].id })
    setSelectedId(appointments[0].id)
    setInterviews(initialInterviews())
    navigate('interview')
    notify('Wylogowano. Dane sesji demo zostały wyczyszczone.')
  }
  function savePatient(patient: PatientProfile) {
    setSession({ mode: 'authenticated', patient })
    notify('Dane konta demo zostały zaktualizowane.')
  }

  if (route === 'logowanie') return <Login onLogin={signIn} onBack={() => navigate('interview')} />
  const titles = {
    interview: {
      title: 'Porozmawiajmy o Twoim zdrowiu',
      subtitle: 'Kilka spokojnych minut teraz. Lepiej przygotowana wizyta jutro.',
    },
    appointments: {
      title: 'Twoje wizyty, spokojnie zaplanowane',
      subtitle: 'Przygotuj swoją historię przed spotkaniem z lekarzem.',
    },
    appointment: {
      title: 'Wszystko o Twojej wizycie',
      subtitle: 'Termin, miejsce i przygotowanie — w jednym miejscu.',
    },
    summary: {
      title: 'Twoje podsumowanie przed wizytą',
      subtitle: 'Zebrane z Twoich odpowiedzi. Gotowe do sprawdzenia.',
    },
    profile: {
      title: 'Twoje konto',
      subtitle: 'Twoje dane i przestrzeń na przygotowanie do wizyty.',
    },
  }

  const navItems = [
    { page: 'interview' as Page, label: 'Wywiad', icon: MessageCircle },
    {
      page: (authenticated ? 'appointments' : 'appointment') as Page,
      label: authenticated ? 'Moje wizyty' : 'Moja wizyta',
      icon: CalendarDays,
    },
    { page: 'summary' as Page, label: 'Podsumowanie', icon: FileText },
  ]
  return (
    <div className="app-shell">
      <a
        className="skip-link"
        href="#main-content"
        onClick={(event) => {
          event.preventDefault()
          document.getElementById('main-content')?.focus()
        }}
      >
        Przejdź do treści
      </a>
      {menuOpen && (
        <button
          className="sidebar-backdrop"
          aria-label="Zamknij nawigację"
          onClick={() => setMenuOpen(false)}
        />
      )}
      <aside className={`sidebar ${menuOpen ? 'open' : ''}`} aria-label="Nawigacja główna">
        <a className="brand" href="#/wywiad">
          <span className="brand-ring" />
          <span>
            Przed wizytą<span className="brand-dot">.</span>
          </span>
        </a>
        <button
          className="icon-button mobile-close"
          onClick={() => setMenuOpen(false)}
          aria-label="Zamknij menu"
        >
          <X size={20} />
        </button>
        <div className="sidebar-label">TWOJA PRZESTRZEŃ</div>
        <nav aria-label="Nawigacja główna">
          {navItems.map(({ page: itemPage, label, icon: Icon }) => (
            <a
              href={`#/${paths[itemPage]}`}
              className={`nav-item ${page === itemPage || (itemPage === 'appointments' && page === 'appointment') ? 'active' : ''}`}
              key={itemPage}
              aria-current={page === itemPage ? 'page' : undefined}
              onClick={() => setMenuOpen(false)}
            >
              <Icon size={20} strokeWidth={1.7} />
              <span>{label}</span>
              {itemPage === 'interview' && <span className="nav-dot" />}
            </a>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <div className="sidebar-quote">
            <span className="quote-icon">
              <Heart size={17} />
            </span>
            <p>
              Dobra wizyta zaczyna się
              <br />
              od dobrej rozmowy.
            </p>
            <span>Jesteśmy tu dla Ciebie.</span>
          </div>
          <button className="sidebar-help" onClick={() => setInfo(true)}>
            <ShieldCheck size={18} />O aplikacji <ChevronRight size={14} />
          </button>
          <div className="sidebar-copyright">PRZED WIZYTĄ © 2026</div>
        </div>
      </aside>
      <div className="main-shell">
        <header className="topbar">
          <div className="topbar-left">
            <button
              className="icon-button menu-button"
              onClick={() => setMenuOpen(true)}
              aria-label="Otwórz menu"
            >
              <Menu size={21} />
            </button>
            <span className="breadcrumb">
              Twoja przestrzeń <ChevronRight size={13} />
              <strong>
                {page === 'interview'
                  ? 'Wywiad'
                  : page === 'summary'
                    ? 'Podsumowanie'
                    : page === 'profile'
                      ? 'Konto'
                      : 'Wizyty'}
              </strong>
            </span>
            <a className="mobile-brand" href="#/wywiad">
              <span className="brand-ring" />
              Przed wizytą<span className="brand-dot">.</span>
            </a>
          </div>
          <div className="topbar-right">
            <span className="demo-badge">DEMO</span>
            {authenticated ? (
              <div className="account-container">
                <button
                  className="account-button"
                  onClick={() => setAccountOpen(!accountOpen)}
                  aria-expanded={accountOpen}
                  aria-label="Menu konta"
                >
                  <span className="patient-avatar">
                    {session.patient.firstName[0]}
                    {session.patient.lastName[0]}
                  </span>
                  <span className="account-name">
                    {session.patient.firstName}
                    <small>Konto pacjenta</small>
                  </span>
                  <ChevronDown size={14} />
                </button>
                {accountOpen && (
                  <>
                    <button
                      className="account-dismiss"
                      aria-label="Zamknij menu konta"
                      onClick={() => setAccountOpen(false)}
                    />
                    <div className="account-dropdown">
                      <strong>
                        {session.patient.firstName} {session.patient.lastName}
                      </strong>
                      <span>{session.patient.email}</span>
                      <button onClick={() => navigate('profile')}>
                        <UserRound size={16} />
                        Moje konto
                      </button>
                      <button onClick={logout}>
                        <LogOut size={16} />
                        Wyloguj się
                      </button>
                    </div>
                  </>
                )}
              </div>
            ) : (
              <>
                <span className="guest-label">
                  <LockKeyhole size={13} />
                  Tryb gościa
                </span>
                <button className="button login-button" onClick={() => navigate('login')}>
                  <LogIn size={15} />
                  Zaloguj się
                </button>
              </>
            )}
          </div>
        </header>
        <main className={`main-content page-${page}`} id="main-content" tabIndex={-1}>
          <div className="page-heading">
            <div>
              <span className="eyebrow">
                {page === 'interview'
                  ? 'PRZESTRZEŃ NA TWOJĄ HISTORIĘ'
                  : page === 'summary'
                    ? 'TWÓJ RAPORT, TWOJA DECYZJA'
                    : 'DOBRA WIZYTA ZACZYNA SIĘ WCZEŚNIEJ'}
              </span>
              <h1>{titles[page].title}</h1>
              <p>{titles[page].subtitle}</p>
            </div>
            {page === 'interview' && (
              <span className="time-estimate">
                <span className="tiny-clock" />
                około 5–7 min
              </span>
            )}
          </div>
          {page === 'interview' && (
            <div className="interview-layout">
              <Conversation
                key={selectedId}
                interview={interview}
                onAnswer={answer}
                onSummary={() => navigate('summary')}
                onReset={() => setResetModal(true)}
              />
              <VisitContext
                appointment={appointment}
                interview={interview}
                authenticated={authenticated}
                onAppointment={() => navigate('appointment')}
                onSummary={() => navigate('summary')}
                onLogin={() => navigate('login')}
              />
            </div>
          )}
          {page === 'appointments' && authenticated && (
            <Appointments
              appointments={appointments}
              interviews={interviews}
              onSelect={(id) => {
                setSelectedId(id)
                navigate(interviews[id].status === 'shared' ? 'summary' : 'appointment')
              }}
            />
          )}
          {page === 'appointment' && (
            <AppointmentDetails
              appointment={appointment}
              interview={interview}
              onInterview={() => navigate('interview')}
              onSummary={() => navigate('summary')}
            />
          )}
          {page === 'summary' && (
            <Summary
              key={selectedId}
              interview={interview}
              appointment={appointment}
              authenticated={authenticated}
              onChange={updateInterview}
              onBack={() => navigate('interview')}
              notify={notify}
            />
          )}
          {page === 'profile' && session.mode === 'authenticated' && (
            <Profile patient={session.patient} onSave={savePatient} onLogout={logout} />
          )}
          <footer className="page-footer">
            <span>
              <ShieldCheck size={13} />
              Asystent zbiera informacje dla lekarza.
            </span>
            {!authenticated && (
              <button
                className="text-button muted"
                onClick={() => {
                  setCodeModal(true)
                  setCodeError('')
                  setCode('')
                }}
              >
                Mam kod innej wizyty <ArrowRight size={13} />
              </button>
            )}
            <span className="footer-tagline">Mały krok do spokojniejszej wizyty.</span>
          </footer>
        </main>
      </div>
      {toast && (
        <div className="toast" role="status">
          <span>
            <Check size={16} />
          </span>
          {toast}
          <button
            className="icon-button"
            onClick={() => setToast('')}
            aria-label="Zamknij powiadomienie"
          >
            <X size={15} />
          </button>
        </div>
      )}
      {info && (
        <Modal title="Poznaj Przed wizytą" onClose={() => setInfo(false)}>
          <p className="modal-description">
            To przestrzeń do przygotowania informacji przed spotkaniem z lekarzem. Asystent zbiera
            powód wizyty, objawy, leki, alergie i Twoje pytania, a Ty sprawdzasz raport oraz
            decydujesz o jego udostępnieniu.
          </p>
          <div className="notice">
            <ShieldCheck size={18} />
            <span>Asystent nie diagnozuje i nie zaleca leczenia.</span>
          </div>
          <p className="form-hint">
            To frontend demonstracyjny bez połączenia z backendem. Konto, pytania AI i udostępnianie
            są symulowane. Odpowiedzi są przechowywane wyłącznie w pamięci strony; odświeżenie je
            usuwa.
          </p>
          <button className="button primary full-width" onClick={() => setInfo(false)}>
            Rozumiem <Check size={16} />
          </button>
        </Modal>
      )}
      {codeModal && (
        <Modal title="Otwórz wywiad kodem wizyty" onClose={() => setCodeModal(false)}>
          <p className="modal-description">
            Wpisz kod otrzymany od placówki. W demo możesz użyć kodu <strong>DEMO2026</strong>.
          </p>
          <form
            onSubmit={async (event) => {
              event.preventDefault()
              const visit = await mockPatientService.getGuestAppointment(code)
              if (!visit) {
                setCodeError('Nieprawidłowy kod. W demo użyj DEMO2026.')
                return
              }
              setSelectedId(visit.id)
              setSession({ mode: 'guest', appointmentId: visit.id })
              setCodeModal(false)
              navigate('interview')
              notify('Otwarto demonstracyjny wywiad dla Twojej wizyty.')
            }}
          >
            <label className="form-label" htmlFor="visit-code">
              Kod wizyty
            </label>
            <input
              id="visit-code"
              className="form-input code-input"
              value={code}
              onChange={(event) => setCode(event.target.value)}
              placeholder="np. DEMO2026"
              autoFocus
              required
              maxLength={40}
            />
            {codeError && (
              <p className="form-error" role="alert">
                {codeError}
              </p>
            )}
            <button className="button primary full-width">
              Otwórz wywiad <ArrowRight size={16} />
            </button>
          </form>
        </Modal>
      )}
      {resetModal && (
        <Modal title="Rozpocznij nowy wywiad demo" onClose={() => setResetModal(false)}>
          <p className="modal-description">
            Dotychczasowe odpowiedzi i wersje raportu dla tej wizyty zostaną usunięte z sesji demo.
          </p>
          <div className="modal-actions">
            <button className="button secondary" onClick={() => setResetModal(false)}>
              Zachowaj wywiad
            </button>
            <button
              className="button primary"
              onClick={() => {
                updateInterview(createInterview(appointment))
                setResetModal(false)
                notify('Możesz rozpocząć rozmowę od początku.')
              }}
            >
              Rozpocznij od nowa
            </button>
          </div>
        </Modal>
      )}
    </div>
  )
}
