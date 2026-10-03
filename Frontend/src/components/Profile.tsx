import { useState } from 'react'
import { Check, LogOut, Mail, ShieldCheck, UserRound } from 'lucide-react'
import type { PatientProfile } from '../models'

export function Profile({
  patient,
  onSave,
  onLogout,
}: {
  patient: PatientProfile
  onSave: (patient: PatientProfile) => void
  onLogout: () => void
}) {
  const [draft, setDraft] = useState(patient)
  return (
    <div className="profile-page">
      <section className="profile-card card">
        <div className="profile-hero">
          <span className="patient-avatar large">
            {patient.firstName[0]}
            {patient.lastName[0]}
          </span>
          <div>
            <h2>
              {patient.firstName} {patient.lastName}
            </h2>
            <span>Konto demonstracyjne pacjenta</span>
          </div>
        </div>
        <form
          onSubmit={(event) => {
            event.preventDefault()
            onSave({ ...draft, firstName: draft.firstName.trim(), lastName: draft.lastName.trim() })
          }}
        >
          <div className="profile-form-grid">
            <label className="form-label">
              Imię
              <input
                className="form-input"
                value={draft.firstName}
                onChange={(event) => setDraft({ ...draft, firstName: event.target.value })}
                required
                pattern=".*\S.*"
                maxLength={60}
              />
            </label>
            <label className="form-label">
              Nazwisko
              <input
                className="form-input"
                value={draft.lastName}
                onChange={(event) => setDraft({ ...draft, lastName: event.target.value })}
                required
                pattern=".*\S.*"
                maxLength={80}
              />
            </label>
            <label className="form-label">
              <span>
                <Mail size={14} />
                Adres e-mail
              </span>
              <input
                className="form-input"
                type="email"
                value={draft.email}
                onChange={(event) => setDraft({ ...draft, email: event.target.value })}
                required
                maxLength={254}
              />
            </label>
            <label className="form-label">
              Telefon
              <input
                className="form-input"
                type="tel"
                value={draft.phone}
                onChange={(event) => setDraft({ ...draft, phone: event.target.value })}
                maxLength={30}
              />
            </label>
          </div>
          <div className="profile-save">
            <span>
              <UserRound size={14} />
              Zmiany działają tylko w tej sesji demo.
            </span>
            <button className="button primary">
              Zapisz zmiany <Check size={16} />
            </button>
          </div>
        </form>
      </section>
      <section className="profile-privacy card">
        <span className="small-icon">
          <ShieldCheck size={23} />
        </span>
        <div>
          <h3>Twoje dane, Twoja decyzja</h3>
          <p>
            Zgoda na udostępnienie dotyczy konkretnego wywiadu i placówki. Możesz nią zarządzać w
            podsumowaniu każdej wizyty.
          </p>
          <p>
            Demo przechowuje odpowiedzi wyłącznie w pamięci otwartej strony. Odświeżenie lub
            wylogowanie usuwa bieżącą sesję.
          </p>
        </div>
      </section>
      <button className="button secondary logout-button" onClick={onLogout}>
        <LogOut size={17} />
        Wyloguj się z konta demo
      </button>
    </div>
  )
}
