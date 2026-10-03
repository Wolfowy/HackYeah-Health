import { useState, type FormEvent } from 'react'
import { ArrowLeft, ArrowRight, Eye, EyeOff, KeyRound, Mail, ShieldCheck } from 'lucide-react'
import { demoPatient } from '../data/mock'
import { Orb } from './Orb'

export function Login({
  onLogin,
  onBack,
}: {
  onLogin: (email: string) => Promise<void>
  onBack: () => void
}) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [visible, setVisible] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!email.trim() || !password.trim()) {
      setError('Uzupełnij adres e-mail i hasło demonstracyjne.')
      return
    }
    setLoading(true)
    try {
      await onLogin(email.trim())
    } catch {
      setError('Nie udało się otworzyć konta demo. Spróbuj ponownie.')
    } finally {
      setLoading(false)
    }
  }
  return (
    <main className="login-page">
      <div className="login-brand">
        <span className="brand-ring" />
        <span>
          Przed wizytą<span className="brand-dot">.</span>
        </span>
      </div>
      <button className="text-button muted login-back" onClick={onBack}>
        <ArrowLeft size={16} />
        Wróć do wywiadu
      </button>
      <div className="login-layout">
        <section className="login-story">
          <h1>
            Więcej spokoju.
            <br />
            Więcej przestrzeni
            <br />
            <span>na Twoją historię.</span>
          </h1>
          <p>
            Zbierz myśli przed wizytą. Do reszty
            <br className="desktop-only" /> przygotujemy się razem.
          </p>
          <Orb />
        </section>
        <section className="login-form-card card">
          <h2>Dobrze Cię widzieć.</h2>
          <p>Zaloguj się i przygotuj swoją kolejną wizytę.</p>
          <form onSubmit={submit}>
            <label className="form-label" htmlFor="login-email">
              Adres e-mail
            </label>
            <div className="input-with-icon">
              <Mail size={18} />
              <input
                id="login-email"
                type="email"
                placeholder="twoj@email.pl"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                autoComplete="off"
                required
              />
            </div>
            <label className="form-label" htmlFor="login-password">
              Hasło demonstracyjne
            </label>
            <div className="input-with-icon">
              <KeyRound size={18} />
              <input
                id="login-password"
                type={visible ? 'text' : 'password'}
                placeholder="Wpisz dowolne hasło demo"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                autoComplete="off"
                required
              />
              <button
                type="button"
                className="icon-button"
                onClick={() => setVisible(!visible)}
                aria-label={visible ? 'Ukryj hasło' : 'Pokaż hasło'}
              >
                {visible ? <EyeOff size={17} /> : <Eye size={17} />}
              </button>
            </div>
            {error && (
              <p className="form-error" role="alert">
                {error}
              </p>
            )}
            <button className="button primary login-submit" disabled={loading}>
              {loading ? 'Otwieranie konta…' : 'Zaloguj się'}
              <ArrowRight size={18} />
            </button>
          </form>
          <div className="login-divider">
            <span>lub poznaj aplikację</span>
          </div>
          <button
            className="button secondary demo-login"
            disabled={loading}
            onClick={async () => {
              setLoading(true)
              try {
                await onLogin(demoPatient.email)
              } catch {
                setError('Nie udało się otworzyć konta demo.')
                setLoading(false)
              }
            }}
          >
            Wejdź na konto demonstracyjne <ArrowRight size={17} />
          </button>
          <div className="login-demo-note">
            <ShieldCheck size={18} />
            <p>
              To makieta logowania. Użyj fikcyjnych danych. Hasło nie jest zapisywane ani wysyłane.
            </p>
          </div>
          <button className="text-button login-guest" onClick={onBack}>
            Kontynuuj bez logowania
          </button>
        </section>
      </div>
    </main>
  )
}
