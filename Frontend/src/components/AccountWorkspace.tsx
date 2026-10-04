import { useEffect, useRef, useState } from 'react'
import { ArrowLeft, CalendarDays, LogOut, UserRound } from 'lucide-react'
import { agentApi, type AgentAccess, type AgentInterviewInfo } from '../lib/agent-api'
import { patientAccountApi, type AccountUser, type AccountVisit } from '../lib/patient-account-api'
import { AppointmentCard } from './AppointmentCard'
import { LiveConversation } from './LiveConversation'
import { AccountEntry } from './AccountEntry'
import { Field } from './DraftEditor'

function Avatar({ user }: { user: AccountUser }) {
  const [failed, setFailed] = useState(false)
  return (
    <span className="avatar">
      {user.avatarUrl && !failed ? (
        <img
          src={user.avatarUrl}
          referrerPolicy="no-referrer"
          alt=""
          onError={() => setFailed(true)}
        />
      ) : (
        user.displayName
          .split(/\s+/)
          .map((word) => word[0])
          .slice(0, 2)
          .join('')
          .toUpperCase()
      )}
    </span>
  )
}
function AccountProfile({
  user,
  onSave,
}: {
  user: AccountUser
  onSave: (user: AccountUser) => void
}) {
  const [name, setName] = useState(user.displayName)
  const [avatar, setAvatar] = useState(user.avatarUrl ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [saved, setSaved] = useState(false)
  return (
    <form
      className="account-entry card"
      onSubmit={(event) => {
        event.preventDefault()
        if (busy) return
        setBusy(true)
        setError('')
        setSaved(false)
        if (avatar.trim() && !/^https?:\/\//i.test(avatar.trim())) {
          setError('Podaj adres awatara zaczynający się od https:// lub http://.')
          setBusy(false)
          return
        }
        patientAccountApi
          .updateProfile(name.trim(), avatar.trim() || null)
          .then((value) => {
            onSave(value)
            setSaved(true)
          })
          .catch((cause) =>
            setError(cause instanceof Error ? cause.message : 'Nie udało się zapisać profilu.'),
          )
          .finally(() => setBusy(false))
      }}
    >
      <h1>Moje konto</h1>
      <p>{user.email}</p>
      <fieldset disabled={busy}>
        <Field label="Nazwa konta" value={name} onChange={setName} maxLength={200} required />
        <Field
          label="Adres zdjęcia profilowego (opcjonalnie)"
          value={avatar}
          onChange={setAvatar}
          maxLength={2000}
          type="url"
        />
      </fieldset>
      {error && (
        <p role="alert" className="form-error">
          {error}
        </p>
      )}
      {saved && <p role="status">Profil zapisany.</p>}
      <button className="button primary" disabled={busy}>
        Zapisz profil
      </button>
    </form>
  )
}

export function AccountWorkspace() {
  const [user, setUser] = useState(patientAccountApi.user)
  const [page, setPage] = useState<'visits' | 'profile'>('visits')
  const [visits, setVisits] = useState<AccountVisit[]>([])
  const [selected, setSelected] = useState<{
    access: AgentAccess
    interview: AgentInterviewInfo
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const generation = useRef(0)
  const lock = useRef(false)
  useEffect(() => {
    if (!user) return
    let active = true
    setLoading(true)
    Promise.all([patientAccountApi.me(), patientAccountApi.visits()])
      .then(([profile, items]) => {
        if (active) {
          setUser(profile)
          setVisits(items.map((item) => item.visit))
          setError('')
        }
      })
      .catch((cause) => {
        if (active) {
          setError(cause instanceof Error ? cause.message : 'Nie udało się pobrać wizyt.')
          if (!patientAccountApi.user) {
            setUser(null)
            setSelected(null)
          }
        }
      })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => {
      active = false
      generation.current++
    }
  }, [user?.id])
  async function open(visit: AccountVisit) {
    if (lock.current) return
    lock.current = true
    setBusy(true)
    setError('')
    const current = ++generation.current
    try {
      const session = await patientAccountApi.createVisitSession(visit.id)
      if (generation.current !== current) return
      const { interview } = await agentApi.getVisitInterview(
        visit.id,
        undefined,
        session.sessionToken,
      )
      if (generation.current !== current) return
      setSelected({
        access: { kind: 'patient', interviewId: interview.id, sessionToken: session.sessionToken },
        interview,
      })
    } catch (cause) {
      if (generation.current === current) {
        setError(cause instanceof Error ? cause.message : 'Nie udało się otworzyć wizyty.')
        if (!patientAccountApi.user) {
          setUser(null)
          setSelected(null)
        }
      }
    } finally {
      lock.current = false
      setBusy(false)
    }
  }
  if (!user)
    return (
      <div className="standalone-shell">
        <header className="standalone-header">
          <span className="brand">
            <span className="brand-ring" />
            Przed wizytą.
          </span>
        </header>
        <main className="standalone-content">
          {error && (
            <p className="form-error" role="alert">
              {error}
            </p>
          )}
          <AccountEntry
            onDone={() => {
              setError('')
              setUser(patientAccountApi.user)
            }}
          />
        </main>
      </div>
    )
  return (
    <div className="account-workspace">
      <header className="standalone-header">
        <span className="brand">
          <span className="brand-ring" />
          Przed wizytą.
        </span>
        <div className="account-toolbar">
          <Avatar key={user.avatarUrl} user={user} />
          <span>{user.displayName}</span>
        </div>
      </header>
      <nav className="account-nav" aria-label="Konto pacjenta">
        <button
          className="text-button"
          disabled={busy}
          onClick={() => {
            setPage('visits')
            setSelected(null)
          }}
        >
          <CalendarDays size={17} />
          Moje wizyty
        </button>
        <button
          className="text-button"
          disabled={busy}
          onClick={() => {
            setPage('profile')
            setSelected(null)
          }}
        >
          <UserRound size={17} />
          Moje konto
        </button>
        <button
          className="text-button muted"
          disabled={busy}
          onClick={() => {
            generation.current++
            setSelected(null)
            setBusy(true)
            void patientAccountApi
              .logout()
              .catch(() =>
                setError(
                  'Wylogowano na tym urządzeniu. Serwer nie potwierdził unieważnienia sesji.',
                ),
              )
              .finally(() => {
                setUser(null)
                setVisits([])
                setBusy(false)
              })
          }}
        >
          <LogOut size={17} />
          Wyloguj
        </button>
      </nav>
      <main className="standalone-content">
        {error && (
          <p className="form-error" role="alert">
            {error}
          </p>
        )}
        {page === 'profile' ? (
          <AccountProfile user={user} onSave={setUser} />
        ) : selected ? (
          <>
            <button className="text-button muted" onClick={() => setSelected(null)}>
              <ArrowLeft size={16} />
              Wróć do wizyt
            </button>
            {selected.interview.visit && <AppointmentCard visit={selected.interview.visit} />}
            <LiveConversation
              key={selected.interview.id}
              access={selected.access}
              initialStatus={selected.interview.status}
            />
          </>
        ) : (
          <section className="account-visits">
            <h1>Moje wizyty</h1>
            {loading && <p role="status">Pobieram wizyty…</p>}
            {!loading && !visits.length && <p>Nie masz jeszcze wizyt przypisanych do konta.</p>}
            {visits.map((visit) => (
              <div key={visit.id}>
                <AppointmentCard visit={visit} />
                <button
                  className="button primary"
                  disabled={busy || ['expired', 'cancelled'].includes(visit.status)}
                  onClick={() => void open(visit)}
                >
                  {visit.interviewStatus === 'completed'
                    ? 'Otwórz podsumowanie'
                    : visit.interviewStatus === 'processing'
                      ? 'Sprawdź wynik rozmowy'
                      : 'Przygotuj się do wizyty'}
                </button>
              </div>
            ))}
            <button
              className="text-button"
              disabled={busy || loading}
              onClick={() => {
                setLoading(true)
                setError('')
                patientAccountApi
                  .visits()
                  .then((items) => setVisits(items.map((item) => item.visit)))
                  .catch((cause) => {
                    setError(cause instanceof Error ? cause.message : 'Nie udało się pobrać wizyt.')
                    if (!patientAccountApi.user) setUser(null)
                  })
                  .finally(() => setLoading(false))
              }}
            >
              Odśwież wizyty
            </button>
          </section>
        )}
      </main>
    </div>
  )
}
