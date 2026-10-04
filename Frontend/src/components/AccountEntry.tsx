import { useState, type FormEvent } from 'react'
import type { AgentAccess } from '../lib/agent-api'
import { patientAccountApi } from '../lib/patient-account-api'
import { Field } from './DraftEditor'

export function AccountEntry({
  access,
  onDone,
  onBack,
}: {
  access?: AgentAccess
  onDone: () => void
  onBack?: () => void
}) {
  const [register, setRegister] = useState(!!access)
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  async function submit(event: FormEvent) {
    event.preventDefault()
    if (busy) return
    setBusy(true)
    setError('')
    try {
      if (register && access) {
        if (
          password.length < 12 ||
          !/\p{Lu}/u.test(password) ||
          !/\p{Ll}/u.test(password) ||
          !/\p{Nd}/u.test(password)
        )
          throw new Error('Hasło musi mieć co najmniej 12 znaków, wielką i małą literę oraz cyfrę.')
        await patientAccountApi.register(access, email.trim(), password, displayName.trim())
      } else await patientAccountApi.login(email.trim(), password)
      setPassword('')
      onDone()
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Nie udało się otworzyć konta.')
      setBusy(false)
    }
  }
  return (
    <section className="account-entry card">
      <h1>{register ? 'Utwórz konto pacjenta' : 'Zaloguj się'}</h1>
      <p>
        {register
          ? 'Na koncie znajdziesz swoje wizyty i podsumowania.'
          : 'Otwórz swoje wizyty i podsumowania.'}
      </p>
      <form onSubmit={submit}>
        <fieldset disabled={busy}>
          {register && (
            <Field
              label="Imię i nazwisko lub nazwa konta"
              value={displayName}
              onChange={setDisplayName}
              required
              maxLength={200}
            />
          )}
          <label className="report-field">
            <span>Adres e-mail</span>
            <input
              type="email"
              autoComplete="username"
              required
              maxLength={320}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
          </label>
          <label className="report-field">
            <span>Hasło</span>
            <input
              type="password"
              autoComplete={register ? 'new-password' : 'current-password'}
              required
              minLength={register ? 12 : undefined}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </label>
          {register && (
            <p className="muted">Co najmniej 12 znaków, wielka i mała litera oraz cyfra.</p>
          )}
        </fieldset>
        {error && (
          <p className="form-error" role="alert">
            {error}
          </p>
        )}
        <button className="button primary" disabled={busy}>
          {busy ? 'Otwieram konto…' : register ? 'Utwórz konto' : 'Zaloguj się'}
        </button>
      </form>
      {access && (
        <button
          className="text-button"
          disabled={busy}
          onClick={() => {
            setRegister(!register)
            setError('')
          }}
        >
          {register ? 'Mam już konto — zaloguj się' : 'Utwórz konto pacjenta'}
        </button>
      )}
      {onBack ? (
        <button className="text-button muted" disabled={busy} onClick={onBack}>
          Wróć do rozmowy
        </button>
      ) : (
        <p className="muted">
          Bez konta otwórz link do rozmowy otrzymany od placówki. Konto możesz utworzyć z tego
          linka.
        </p>
      )}
    </section>
  )
}
