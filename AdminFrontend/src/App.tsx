import { useEffect, useState } from 'react'
import {
  App as AntApp,
  Alert,
  Avatar,
  Badge,
  Button,
  Calendar,
  Dropdown,
  Empty,
  Input,
  Menu,
  Segmented,
  Select,
  Skeleton,
  Tag,
} from 'antd'
import {
  ArrowRightOutlined,
  CalendarOutlined,
  CheckCircleOutlined,
  LeftOutlined,
  RightOutlined,
  DownOutlined,
  FileTextOutlined,
  LogoutOutlined,
  MedicineBoxOutlined,
  PlusOutlined,
  SearchOutlined,
  SendOutlined,
  UnorderedListOutlined,
  ReloadOutlined,
} from '@ant-design/icons'
import type { Dayjs } from 'dayjs'
import type {
  Appointment,
  ContactChannel,
  NewAppointment,
  StaffUser,
  VisitStatus,
  DataMode,
  UpdateAppointment,
  Doctor,
} from './models'
import { doctors, DEMO_EMAIL, facility } from './data/mock'
import { filterAppointments, statusConfig } from './lib/appointments'
import { atTime, formatTime, initialDate, inWarsaw, weekStart } from './lib/date'
import { receptionService, selectDataMode } from './services/reception'
import { AppointmentCalendar } from './components/AppointmentCalendar'
import { AppointmentDrawer } from './components/AppointmentDrawer'
import { AppointmentTable } from './components/AppointmentTable'
import { Brand } from './components/Brand'
import { Login } from './components/Login'
import { ApiError } from './services/http'
import { DoctorToday } from './components/DoctorToday'
import { NewAppointmentModal } from './components/NewAppointmentModal'

type Page = 'calendar' | 'appointments' | 'reports' | 'doctor'
type CalendarView = 'week' | 'month' | 'list'
const pageTitles: Record<Page, string> = {
  doctor: 'Pacjenci na dziś',
  calendar: 'Kalendarz wizyt',
  appointments: 'Wizyty',
  reports: 'Raporty pacjentów',
}
const sessionKey = 'docprep-reception-demo'

function readDemoSession(): StaffUser | null {
  try {
    const role = sessionStorage.getItem(sessionKey)
    if (role !== 'active' && role !== 'doctor') return null
    selectDataMode('demo')
    return {
      id: role === 'doctor' ? 'demo-doctor' : 'demo-receptionist',
      facilityId: facility.id,
      displayName: role === 'doctor' ? 'dr Anna Kowalska' : 'Anna Nowak',
      email: role === 'doctor' ? 'doctor@docprep.local' : DEMO_EMAIL,
      role: role === 'doctor' ? 'Clinician' : 'Administrative',
      clinicianId: role === 'doctor' ? doctors[0].id : null,
    }
  } catch {
    return null
  }
}

export function App() {
  const { message } = AntApp.useApp()
  const [user, setUser] = useState<StaffUser | null>(readDemoSession)
  const [mode, setMode] = useState<DataMode>(() => (readDemoSession() ? 'demo' : 'api'))
  const [visits, setVisits] = useState<Appointment[]>([])
  const [apiDoctors, setApiDoctors] = useState<Doctor[]>([])
  const [apiFacility, setApiFacility] = useState<Appointment['facility']>({
    id: '',
    name: 'Twoja placówka',
    address: '',
  })
  const [loading, setLoading] = useState(false)
  const [loadError, setLoadError] = useState('')
  const [page, setPage] = useState<Page>(() =>
    readDemoSession()?.role === 'Clinician' ? 'doctor' : 'calendar',
  )
  const [view, setView] = useState<CalendarView>('week')
  const [selectedDate, setSelectedDate] = useState<Dayjs>(() =>
    readDemoSession()?.role === 'Clinician' ? inWarsaw() : initialDate(),
  )
  const [search, setSearch] = useState('')
  const [doctorId, setDoctorId] = useState<string>()
  const [status, setStatus] = useState<VisitStatus>()
  const [selectedVisitId, setSelectedVisitId] = useState<string | null>(null)
  const [drawerTab, setDrawerTab] = useState('details')
  const [createOpen, setCreateOpen] = useState(false)

  useEffect(() => {
    if (!user) return
    let cancelled = false
    if (user.role !== 'Clinician') {
      receptionService
        .getDoctors()
        .then((data) => {
          if (!cancelled) setApiDoctors(data)
        })
        .catch((err) => {
          if (!cancelled) handleError(err)
        })
      receptionService
        .getFacility()
        .then((data) => {
          if (!cancelled) setApiFacility(data)
        })
        .catch((err) => {
          if (!cancelled) handleError(err)
        })
    }
    setLoading(true)
    setLoadError('')
    receptionService
      .getAppointments()
      .then((data) => {
        if (!cancelled) setVisits(data)
      })
      .catch((err) => {
        if (!cancelled) {
          setLoadError(err instanceof Error ? err.message : 'Nie udało się wczytać wizyt.')
          handleError(err)
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [user])

  async function login(email: string, password: string, selectedMode: DataMode) {
    selectDataMode(selectedMode)
    const staff = await receptionService.signIn(email, password)
    try {
      if (selectedMode === 'demo')
        sessionStorage.setItem(sessionKey, staff.role === 'Clinician' ? 'doctor' : 'active')
      else sessionStorage.removeItem(sessionKey)
    } catch {
      /* Demo can work without storage. */
    }
    setMode(selectedMode)
    setVisits([])
    setUser(staff)
    setPage(staff.role === 'Clinician' ? 'doctor' : 'calendar')
    setSelectedDate(
      staff.role === 'Clinician' || selectedMode === 'api' ? inWarsaw() : initialDate(),
    )
  }
  function handleError(err: unknown) {
    if (err instanceof ApiError && err.status === 401) {
      clearSession()
      message.warning(err.message)
    }
  }
  async function logout() {
    try {
      await receptionService.signOut()
    } catch (err) {
      message.warning(
        err instanceof Error ? err.message : 'Nie udało się zakończyć sesji na serwerze.',
      )
    }
    clearSession()
  }
  function clearSession() {
    try {
      sessionStorage.removeItem(sessionKey)
    } catch {
      /* No real token is stored. */
    }
    setSelectedVisitId(null)
    setCreateOpen(false)
    setUser(null)
    setVisits([])
    setApiDoctors([])
    setApiFacility({ id: '', name: 'Twoja placówka', address: '' })
    setPage('calendar')
    setSearch('')
    setDoctorId(undefined)
    setStatus(undefined)
  }
  function openVisit(visit: Appointment, tab = 'details') {
    setDrawerTab(tab)
    setSelectedVisitId(visit.visitId)
  }
  function replaceVisit(updated: Appointment) {
    setVisits((current) => current.map((v) => (v.visitId === updated.visitId ? updated : v)))
  }
  async function refreshVisit(id: string) {
    try {
      replaceVisit(await receptionService.getAppointment(id))
    } catch (err) {
      handleError(err)
      throw err
    }
  }
  async function refreshAll() {
    setLoading(true)
    setLoadError('')
    try {
      setVisits(await receptionService.getAppointments())
    } catch (err) {
      handleError(err)
      setLoadError(err instanceof Error ? err.message : 'Nie udało się odświeżyć wizyt.')
    } finally {
      setLoading(false)
    }
  }
  async function sendInvitation(id: string, channel: ContactChannel, contact?: string) {
    try {
      replaceVisit(await receptionService.sendInvitation(id, channel, contact))
    } catch (err) {
      handleError(err)
      throw err
    }
  }
  async function updateVisit(visit: Appointment, input: UpdateAppointment) {
    try {
      const updated = await receptionService.updateAppointment(visit, input)
      replaceVisit(updated)
      return updated
    } catch (err) {
      handleError(err)
      throw err
    }
  }
  async function cancelVisit(id: string) {
    try {
      replaceVisit(await receptionService.cancelAppointment(id))
    } catch (err) {
      handleError(err)
      throw err
    }
  }
  async function createAppointment(input: NewAppointment) {
    let visit: Appointment
    try {
      visit = await receptionService.createAppointment(input)
    } catch (err) {
      handleError(err)
      throw err
    }
    setVisits((current) => [...current, visit])
    setSelectedDate(inWarsaw(visit.scheduledAt))
    setSearch('')
    setDoctorId(undefined)
    setStatus(undefined)
    setPage('calendar')
    openVisit(visit)
    message.success(
      mode === 'demo'
        ? 'Dodano wizytę do kalendarza demo.'
        : 'Zarejestrowano wizytę i zaproszenie.',
    )
    return visit
  }

  if (!user) return <Login onLogin={login} />

  const scopedVisits =
    user.role === 'Clinician'
      ? visits.filter((v) => !!user.clinicianId && v.assignedClinicianId === user.clinicianId)
      : visits
  const doctorOptions =
    mode === 'demo'
      ? doctors
      : user.role !== 'Clinician'
        ? apiDoctors
        : [
            ...new Map(
              visits.filter((v) => v.assignedClinicianId).map((v) => [v.doctor.id, v.doctor]),
            ).values(),
          ]
  const currentFacility =
    mode === 'demo'
      ? facility
      : user.role !== 'Clinician'
        ? apiFacility
        : scopedVisits[0]?.facility || { name: 'Twoja placówka', address: '' }
  const period =
    page === 'reports' || page === 'doctor'
      ? 'all'
      : view === 'month' && page === 'calendar'
        ? 'month'
        : 'week'
  const filtered = filterAppointments(scopedVisits, {
    search,
    doctorId,
    status,
    date: selectedDate,
    period,
    reportsOnly: page === 'reports',
  })
  const periodVisits = filterAppointments(scopedVisits, {
    search: '',
    date: selectedDate,
    period,
  })
  const activeVisits = periodVisits.filter(
    (visit) => visit.status !== 'Cancelled' && visit.status !== 'Expired',
  )
  const reports = activeVisits.filter((visit) => visit.reportAvailable ?? !!visit.report)
  const pendingInvites = activeVisits.filter((visit) => visit.deliveryStatus !== 'Delivered')
  const waiting = activeVisits.filter((visit) => !(visit.reportAvailable ?? !!visit.report))
  const daily = filtered.filter((visit) => inWarsaw(visit.scheduledAt).isSame(selectedDate, 'day'))
  const selectedVisit = visits.find((visit) => visit.visitId === selectedVisitId) || null
  const start = weekStart(selectedDate)
  const rangeLabel =
    period === 'month'
      ? selectedDate.format('MMMM YYYY')
      : `${start.format('D MMM')} – ${start.add(6, 'day').format('D MMM YYYY')}`
  const statsLabel =
    period === 'all'
      ? 'Wszystkie wizyty'
      : period === 'month'
        ? 'W wybranym miesiącu'
        : 'W wybranym tygodniu'
  function navigate(amount: number) {
    setSelectedDate((date) =>
      atTime(date.add(amount, view === 'month' && page === 'calendar' ? 'month' : 'week'), '00:00'),
    )
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <Brand />
        <div className="sidebar-label">PRZESTRZEŃ PLACÓWKI</div>
        <Menu
          mode="inline"
          selectedKeys={[page]}
          onClick={({ key }) => {
            setPage(key as Page)
            setSearch('')
            setStatus(undefined)
          }}
          items={[
            {
              key: 'doctor',
              icon: <MedicineBoxOutlined />,
              label: user.role === 'Clinician' ? 'Pacjenci na dziś' : 'Plan lekarza',
            },
            { key: 'calendar', icon: <CalendarOutlined />, label: 'Kalendarz' },
            {
              key: 'appointments',
              icon: <UnorderedListOutlined />,
              label: 'Wizyty',
            },
            {
              key: 'reports',
              icon: <FileTextOutlined />,
              label: (
                <span className="nav-label">
                  Raporty{' '}
                  <span>
                    {scopedVisits.filter((visit) => visit.reportAvailable ?? !!visit.report).length}
                  </span>
                </span>
              ),
            },
          ]}
        />
        <div className="sidebar-note">
          <span className="sidebar-note-icon">
            <CheckCircleOutlined />
          </span>
          <b>Dobra wizyta zaczyna się wcześniej.</b>
          <p>Pomóż pacjentom przygotować informacje przed spotkaniem z lekarzem.</p>
        </div>
        <div className="sidebar-bottom">
          <div className="facility-switch">
            <span className="facility-icon">
              <MedicineBoxOutlined />
            </span>
            <div>
              <b>{currentFacility.name}</b>
              <small>{currentFacility.address || 'Placówka'}</small>
            </div>
          </div>
          <span className="demo-pill">
            <span /> {mode === 'demo' ? 'Tryb demonstracyjny' : 'Połączono z placówką'}
          </span>
        </div>
      </aside>
      <div className="main-shell">
        <header className="topbar">
          <div className="breadcrumb">
            <span>Panel placówki</span>
            <i>/</i>
            <b>{pageTitles[page]}</b>
          </div>
          <div className="topbar-right">
            <span className="topbar-date">{inWarsaw().format('D MMMM YYYY')}</span>
            <Dropdown
              trigger={['click']}
              menu={{
                items: [
                  {
                    key: 'logout',
                    label: 'Wyloguj się',
                    icon: <LogoutOutlined />,
                    onClick: logout,
                  },
                ],
              }}
            >
              <button className="user-menu">
                <Avatar size={34} className="user-avatar">
                  {user.displayName
                    .split(' ')
                    .filter(Boolean)
                    .slice(0, 2)
                    .map((p) => p[0])
                    .join('')}
                </Avatar>
                <span>
                  <b>{user.displayName}</b>
                  <small>{user.role === 'Clinician' ? 'Lekarz' : 'Recepcja'}</small>
                </span>
                <DownOutlined />
              </button>
            </Dropdown>
          </div>
        </header>
        <main className="workspace">
          <div className="page-heading">
            <div>
              <span className="page-eyebrow">DOBRZE CIĘ WIDZIEĆ</span>
              <h1>
                {pageTitles[page]}
                <span className="heading-dot">.</span>
              </h1>
              <p>
                {page === 'doctor'
                  ? 'Wizyty w kolejności godzin. Raport i status zawsze pod ręką.'
                  : page === 'reports'
                    ? 'Zatwierdzone raporty, które pacjenci udostępnili przed wizytą.'
                    : 'Twój plan wizyt i przygotowanie pacjentów w jednym miejscu.'}
              </p>
            </div>
            <div className="page-actions">
              <Button
                size="large"
                icon={<ReloadOutlined />}
                aria-label="Odśwież wizyty"
                onClick={refreshAll}
                loading={loading}
              >
                Odśwież wizyty
              </Button>
              {user.role !== 'Clinician' && (
                <Button
                  size="large"
                  type="primary"
                  icon={<PlusOutlined />}
                  onClick={() => setCreateOpen(true)}
                >
                  Nowa wizyta
                </Button>
              )}
            </div>
          </div>
          {page !== 'doctor' && (
            <section className="stats-grid" aria-label={statsLabel}>
              {[
                {
                  label: 'Zaplanowane wizyty',
                  value: activeVisits.length,
                  hint: statsLabel,
                  icon: <CalendarOutlined />,
                  className: 'violet',
                },
                {
                  label: 'Gotowe raporty',
                  value: reports.length,
                  hint: 'Pacjenci przygotowani',
                  icon: <FileTextOutlined />,
                  className: 'green',
                },
                {
                  label: 'W trakcie przygotowania',
                  value: waiting.length,
                  hint: 'Oczekują na raport',
                  icon: <ClockIcon />,
                  className: 'amber',
                },
                {
                  label: 'Zaproszenia do wysłania',
                  value: pendingInvites.length,
                  hint: 'W tym nieudane dostarczenia',
                  icon: <SendOutlined />,
                  className: 'blue',
                },
              ].map((stat) => (
                <div className="stat-card" key={stat.label}>
                  <div>
                    <span>{stat.label}</span>
                    <b>{loading ? '—' : stat.value}</b>
                    <small>{stat.hint}</small>
                  </div>
                  <span className={`stat-icon ${stat.className}`}>{stat.icon}</span>
                </div>
              ))}
            </section>
          )}
          {loadError && <Alert type="error" showIcon title={loadError} className="form-alert" />}
          {page === 'doctor' ? (
            loading ? (
              <Skeleton active paragraph={{ rows: 8 }} />
            ) : (
              <>
                {user.role !== 'Clinician' && (
                  <Select
                    className="doctor-select"
                    aria-label="Lekarz w planie dnia"
                    placeholder="Wybierz lekarza"
                    value={doctorId}
                    onChange={setDoctorId}
                    options={doctorOptions.map((d) => ({ value: d.id, label: d.name }))}
                  />
                )}
                {user.role === 'Clinician' || doctorId ? (
                  <DoctorToday
                    visits={scopedVisits.filter(
                      (v) => !doctorId || v.assignedClinicianId === doctorId,
                    )}
                    date={selectedDate}
                    onDateChange={setSelectedDate}
                    onOpen={openVisit}
                  />
                ) : (
                  <Empty description="Wybierz lekarza, aby zobaczyć jego plan dnia" />
                )}
              </>
            )
          ) : (
            <div className={`schedule-layout ${page !== 'calendar' ? 'without-aside' : ''}`}>
              <section className="schedule-card">
                <div className="schedule-toolbar">
                  <div className="range-control">
                    {page !== 'reports' && (
                      <>
                        <Button
                          type="text"
                          icon={<LeftOutlined />}
                          aria-label="Poprzedni okres"
                          onClick={() => navigate(-1)}
                        />
                        <Button
                          type="text"
                          icon={<RightOutlined />}
                          aria-label="Następny okres"
                          onClick={() => navigate(1)}
                        />
                      </>
                    )}
                    <h2>{page === 'reports' ? 'Udostępnione raporty' : rangeLabel}</h2>
                    {page !== 'reports' && (
                      <Button size="small" onClick={() => setSelectedDate(inWarsaw())}>
                        Dzisiaj
                      </Button>
                    )}
                  </div>
                  {page === 'calendar' && (
                    <Segmented
                      value={view}
                      onChange={(value) => setView(value as CalendarView)}
                      options={[
                        { label: 'Tydzień', value: 'week' },
                        { label: 'Miesiąc', value: 'month' },
                        { label: 'Lista', value: 'list' },
                      ]}
                    />
                  )}
                </div>
                <div className="filters">
                  <Input
                    allowClear
                    prefix={<SearchOutlined />}
                    placeholder="Szukaj pacjenta lub wizyty…"
                    value={search}
                    onChange={(event) => setSearch(event.target.value)}
                    aria-label="Szukaj pacjenta lub wizyty"
                  />
                  <Select
                    allowClear
                    placeholder="Wszyscy lekarze"
                    aria-label="Filtr lekarza"
                    value={doctorId}
                    onChange={setDoctorId}
                    options={doctorOptions.map((doctor) => ({
                      label: doctor.name,
                      value: doctor.id,
                    }))}
                  />
                  <Select
                    allowClear
                    placeholder="Wszystkie statusy"
                    aria-label="Filtr statusu"
                    value={status}
                    onChange={setStatus}
                    options={Object.entries(statusConfig).map(([value, config]) => ({
                      value,
                      label: config.label,
                    }))}
                  />
                  {(search || doctorId || status) && (
                    <Button
                      type="text"
                      size="small"
                      onClick={() => {
                        setSearch('')
                        setDoctorId(undefined)
                        setStatus(undefined)
                      }}
                    >
                      Wyczyść
                    </Button>
                  )}
                </div>
                {loading ? (
                  <div className="loading-panel">
                    <Skeleton active paragraph={{ rows: 8 }} />
                  </div>
                ) : page === 'calendar' && view !== 'list' ? (
                  <AppointmentCalendar
                    visits={filtered}
                    selectedDate={selectedDate}
                    view={view}
                    onDateChange={setSelectedDate}
                    onOpen={openVisit}
                  />
                ) : (
                  <AppointmentTable
                    visits={filtered}
                    reportsOnly={page === 'reports'}
                    onOpen={(visit) => openVisit(visit, page === 'reports' ? 'report' : 'details')}
                  />
                )}
                <div className="calendar-legend">
                  <span>
                    <i className="shared" />
                    Raport gotowy
                  </span>
                  <span>
                    <i className="progress" />
                    Wywiad w trakcie
                  </span>
                  <span>
                    <i className="approval" />
                    Do zatwierdzenia
                  </span>
                  <span>
                    <i className="pending" />
                    Nie rozpoczęto
                  </span>
                  <span>
                    <i className="supplement" />
                    Do uzupełnienia
                  </span>
                </div>
              </section>
              {page === 'calendar' && (
                <aside className="schedule-aside">
                  <section className="mini-calendar-card">
                    <div className="mini-header">
                      <h3>{selectedDate.format('MMMM YYYY')}</h3>
                      <span>Wybierz dzień</span>
                    </div>
                    <Calendar
                      fullscreen={false}
                      value={selectedDate}
                      headerRender={() => null}
                      onSelect={setSelectedDate}
                    />
                  </section>
                  <section className="day-agenda">
                    <div className="section-heading">
                      <div>
                        <span className="eyebrow">PLAN DNIA</span>
                        <h3>{selectedDate.format('dddd, D MMM')}</h3>
                      </div>
                      <Badge
                        count={daily.length}
                        color="#f0edfc"
                        style={{ color: '#7461dc', boxShadow: 'none' }}
                      />
                    </div>
                    {daily.length ? (
                      <div className="agenda-list">
                        {daily.slice(0, 4).map((visit) => (
                          <button
                            key={visit.visitId}
                            className="agenda-visit"
                            onClick={() => openVisit(visit)}
                          >
                            <time>{formatTime(visit.scheduledAt)}</time>
                            <div>
                              <b>{visit.patient.name}</b>
                              <small>{visit.doctor.name}</small>
                            </div>
                            <span
                              className={`agenda-dot ${statusConfig[visit.status].className}`}
                            />
                          </button>
                        ))}
                      </div>
                    ) : (
                      <Empty
                        image={Empty.PRESENTED_IMAGE_SIMPLE}
                        description="Brak wizyt w tym dniu"
                      />
                    )}
                    {daily.length > 4 && (
                      <Button
                        type="link"
                        size="small"
                        onClick={() => {
                          setPage('appointments')
                          setSearch('')
                        }}
                      >
                        Zobacz wszystkie wizyty <ArrowRightOutlined />
                      </Button>
                    )}
                  </section>
                  <section className="pending-card">
                    <span className="pending-card-icon">
                      <SendOutlined />
                    </span>
                    <h3>Zaproszenia czekają</h3>
                    <p>
                      {pendingInvites.length
                        ? `${pendingInvites.length} wizyt wymaga wysłania lub ponowienia zaproszenia do wywiadu.`
                        : 'Wszystkie zaproszenia w tym okresie są dostarczone.'}
                    </p>
                    <Button
                      size="small"
                      block
                      onClick={() => pendingInvites[0] && openVisit(pendingInvites[0])}
                      disabled={!pendingInvites.length}
                    >
                      Przejdź do zaproszenia <ArrowRightOutlined />
                    </Button>
                  </section>
                </aside>
              )}
            </div>
          )}
          <footer className="workspace-footer">
            <span>DocPrep · Panel personelu</span>
            <span>
              {mode === 'demo' ? (
                <>
                  <Tag>DEMO</Tag> Dane fikcyjne · zmiany znikają po odświeżeniu
                </>
              ) : (
                <>
                  <Tag color="green">API</Tag> Dane placówki · Europe/Warsaw
                </>
              )}
            </span>
          </footer>
        </main>
      </div>
      {selectedVisit && (
        <AppointmentDrawer
          key={`${selectedVisit.visitId}-${drawerTab}`}
          visit={selectedVisit}
          initialTab={drawerTab}
          onClose={() => setSelectedVisitId(null)}
          onSend={sendInvitation}
          onRegenerate={async (id, channel, contact) => {
            try {
              replaceVisit(await receptionService.regenerateInvitation(id, channel, contact))
            } catch (err) {
              handleError(err)
              throw err
            }
          }}
          user={user}
          mode={mode}
          onRefresh={() => refreshVisit(selectedVisit.visitId)}
          onUpdate={updateVisit}
          onCancel={cancelVisit}
          onError={handleError}
        />
      )}
      {createOpen && (
        <NewAppointmentModal
          date={selectedDate}
          mode={mode}
          onError={handleError}
          onClose={() => setCreateOpen(false)}
          onCreate={createAppointment}
        />
      )}
    </div>
  )
}

function ClockIcon() {
  return (
    <span className="clock-icon" aria-hidden="true">
      <i />
    </span>
  )
}
