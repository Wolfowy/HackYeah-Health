import { useState } from 'react'
import {
  Activity,
  ArrowLeft,
  ArrowRight,
  Check,
  CheckCheck,
  CircleAlert,
  ClipboardList,
  Clock3,
  Download,
  FileText,
  HeartPulse,
  MessageCircle,
  Pencil,
  Pill,
  Plus,
  Printer,
  ShieldCheck,
} from 'lucide-react'
import { reportFields } from '../data/mock'
import {
  approveReport,
  editReportField,
  isCurrentVersionApproved,
  missingFields,
  setSharingConsent,
} from '../lib/interview'
import { appointmentDay, appointmentTime, downloadJson } from '../lib/format'
import type { Appointment, Interview, ReportField } from '../models'
import { Modal } from './Modal'

const fieldIcons = {
  reason: FileText,
  symptoms: Activity,
  timeline: Clock3,
  medications: Pill,
  allergies: ShieldCheck,
  conditions: HeartPulse,
  questions: MessageCircle,
  additionalNotes: Plus,
}

export function Summary({
  interview,
  appointment,
  authenticated,
  onChange,
  onBack,
  notify,
}: {
  interview: Interview
  appointment: Appointment
  authenticated: boolean
  onChange: (interview: Interview) => void
  onBack: () => void
  notify: (message: string) => void
}) {
  const [editing, setEditing] = useState<ReportField | null>(null)
  const [editText, setEditText] = useState('')
  const [acknowledged, setAcknowledged] = useState(false)
  const [sharing, setSharing] = useState(false)
  const [consentChecked, setConsentChecked] = useState(false)
  const approved = isCurrentVersionApproved(interview)
  const version = interview.versions.at(-1)
  const missing = missingFields(interview.draft)
  const hasContent = reportFields.some(({ key }) => !!interview.draft.sections[key].text)

  function edit(field: ReportField) {
    setEditing(field)
    setEditText(interview.draft.sections[field].text)
  }
  function approve() {
    onChange(approveReport(interview, acknowledged))
    notify(
      interview.consent.granted
        ? 'Nowa wersja raportu została zatwierdzona i objęta aktywną zgodą.'
        : 'Treść zatwierdzona. Teraz możesz osobno udostępnić raport.',
    )
    setAcknowledged(false)
  }
  const exportData = version && {
    appointmentId: appointment.id,
    facilityId: appointment.facility.id,
    scheduledAt: appointment.scheduledAt,
    doctor: appointment.doctor,
    ...version,
  }

  return (
    <div className="summary-page">
      <button className="text-button muted back-link" onClick={onBack}>
        <ArrowLeft size={16} />
        Wróć do rozmowy
      </button>
      <div className={`summary-banner ${approved ? 'approved' : ''}`}>
        <span className="banner-icon">
          {approved ? <CheckCheck size={23} /> : <ClipboardList size={23} />}
        </span>
        <div>
          <h2>{approved ? 'Treść zatwierdzona przez Ciebie' : 'Twoja historia, Twoje słowa.'}</h2>
          <p>
            {approved
              ? `Wersja ${version?.version} · ${interview.consent.granted ? 'Raport udostępniony placówce w trybie demo.' : 'Raport czeka na osobną zgodę na udostępnienie.'}`
              : 'Sprawdź odpowiedzi. Każdą informację możesz zmienić lub usunąć.'}
          </p>
        </div>
      </div>
      {version && !approved && (
        <div className="notice">
          <CircleAlert size={17} />
          <span>
            Edytujesz nową wersję.{' '}
            {interview.consent.granted
              ? `Placówka nadal widzi zatwierdzoną wersję ${version.version}.`
              : `Zatwierdzona wersja ${version.version} jest zachowana.`}
          </span>
        </div>
      )}
      <div className="summary-grid">
        {reportFields
          .filter(
            ({ key }) =>
              key !== 'additionalNotes' || authenticated || interview.draft.sections[key].text,
          )
          .map(({ key, label }) => {
            const section = interview.draft.sections[key]
            const Icon = fieldIcons[key]
            return (
              <section className={`summary-section card ${!section.text ? 'empty' : ''}`} key={key}>
                <div className="summary-section-top">
                  <span className="small-icon">
                    <Icon size={18} />
                  </span>
                  <h3>{label}</h3>
                  <button
                    className="icon-button edit-button"
                    onClick={() => edit(key)}
                    aria-label={`Edytuj: ${label}`}
                  >
                    <Pencil size={15} />
                  </button>
                </div>
                <p>{section.text || 'Do uzupełnienia'}</p>
                <span className={`field-source ${section.state !== 'provided' ? 'missing' : ''}`}>
                  {section.state === 'missing'
                    ? 'Nie podano informacji'
                    : section.state === 'unknown'
                      ? 'Informacja nieznana — do wyjaśnienia'
                      : section.state === 'conflicting'
                        ? 'Sprzeczność — do wyjaśnienia'
                        : section.source === 'patient_edited'
                          ? 'Edytowane przez Ciebie'
                          : 'Twoja odpowiedź'}
                </span>
              </section>
            )
          })}
      </div>
      <div className="summary-actions card">
        <div className="summary-actions-heading">
          <ShieldCheck size={21} />
          <div>
            <h3>
              {approved
                ? 'Raport gotowy. Ty wybierasz kolejny krok.'
                : 'Spokojnie sprawdź, zanim zatwierdzisz.'}
            </h3>
            <p>Zatwierdzenie treści i udostępnienie placówce to dwa osobne kroki.</p>
          </div>
        </div>
        {!approved && missing.length > 0 && (
          <div className="missing-fields">
            <span>
              <CircleAlert size={16} />
              Pozostało do wyjaśnienia:{' '}
              {missing
                .map((key) =>
                  reportFields.find((field) => field.key === key)?.shortLabel.toLowerCase(),
                )
                .join(', ')}
              .
            </span>
            <label className="checkbox-row">
              <input
                type="checkbox"
                checked={acknowledged}
                onChange={(event) => setAcknowledged(event.target.checked)}
              />
              Rozumiem, że raport jest niepełny, i chcę zatwierdzić go w tej formie.
            </label>
          </div>
        )}
        <div className="action-buttons">
          {!approved ? (
            <button
              className="button primary"
              disabled={!hasContent || (missing.length > 0 && !acknowledged)}
              onClick={approve}
            >
              <Check size={17} />
              Zatwierdź treść raportu
            </button>
          ) : interview.consent.granted ? (
            <span className="pill success">
              <Check size={15} />
              Udostępniony w demo
            </span>
          ) : (
            <button
              className="button primary"
              onClick={() => {
                setSharing(true)
                setConsentChecked(false)
              }}
            >
              Udostępnij placówce <ArrowRight size={17} />
            </button>
          )}
          {version && (
            <>
              <button
                className="button secondary"
                onClick={() =>
                  downloadJson(exportData, `przed-wizyta-raport-v${version.version}.json`)
                }
              >
                <Download size={16} />
                Pobierz JSON
              </button>
              <button className="button secondary" onClick={() => window.print()}>
                <Printer size={16} />
                Drukuj / PDF
              </button>
            </>
          )}
        </div>
        {interview.consent.granted && (
          <button
            className="text-button muted revoke-consent"
            onClick={() => {
              onChange(setSharingConsent(interview, false))
              notify('Zgoda cofnięta w demo. Placówka nie ma już dostępu do raportu.')
            }}
          >
            Cofnij zgodę na udostępnienie
          </button>
        )}
        <p className="summary-footnote">
          {version && !approved
            ? 'Pobieranie i drukowanie obejmuje ostatnią zatwierdzoną wersję, a nie bieżącą wersję roboczą.'
            : 'Wersja demonstracyjna. Raport nie jest wysyłany do rzeczywistej placówki.'}
        </p>
      </div>
      {editing && (
        <Modal
          title={`Edytuj: ${reportFields.find((field) => field.key === editing)?.label}`}
          onClose={() => setEditing(null)}
        >
          <form
            onSubmit={(event) => {
              event.preventDefault()
              onChange(editReportField(interview, editing, editText))
              setAcknowledged(false)
              setEditing(null)
              notify('Zmiana zapisana w wersji roboczej.')
            }}
          >
            <label className="form-label" htmlFor="edit-report">
              Treść informacji
            </label>
            <textarea
              id="edit-report"
              className="form-textarea"
              value={editText}
              onChange={(event) => setEditText(event.target.value)}
              rows={6}
              maxLength={4000}
              autoFocus
            />
            <p className="form-hint">
              Aby usunąć informację, pozostaw pole puste. Zmiana wymaga ponownego zatwierdzenia
              raportu.
            </p>
            <div className="modal-actions">
              <button type="button" className="button secondary" onClick={() => setEditing(null)}>
                Anuluj
              </button>
              <button className="button primary">
                Zapisz zmiany <Check size={16} />
              </button>
            </div>
          </form>
        </Modal>
      )}
      {sharing && (
        <Modal title="Udostępnij raport placówce" onClose={() => setSharing(false)}>
          <div className="sharing-recipient">
            <span className="small-icon">
              <ShieldCheck size={22} />
            </span>
            <div>
              <strong>{appointment.facility.name}</strong>
              <p>
                {appointment.doctor.name} · {appointmentDay(appointment.scheduledAt)},{' '}
                {appointmentTime(appointment.scheduledAt)}
              </p>
            </div>
          </div>
          <p className="modal-description">
            Udostępnisz zatwierdzoną wersję {version?.version} tego wywiadu. Kolejne zatwierdzone
            wersje będą objęte tą samą zgodą.
          </p>
          <label className="checkbox-row consent-checkbox">
            <input
              type="checkbox"
              checked={consentChecked}
              onChange={(event) => setConsentChecked(event.target.checked)}
            />
            Zgadzam się na udostępnienie tego wywiadu wskazanej placówce.
          </label>
          <p className="form-hint">
            Zgodę możesz cofnąć. Nie wycofa to kopii wcześniej pobranych przez placówkę. W demo
            udostępnienie jest symulowane.
          </p>
          <div className="modal-actions">
            <button className="button secondary" onClick={() => setSharing(false)}>
              Jeszcze nie
            </button>
            <button
              className="button primary"
              disabled={!consentChecked}
              onClick={() => {
                onChange(setSharingConsent(interview, true))
                setSharing(false)
                notify('Raport udostępniony placówce w trybie demo.')
              }}
            >
              Potwierdź udostępnienie <ArrowRight size={16} />
            </button>
          </div>
        </Modal>
      )}
      {version && (
        <article className="print-report">
          <div className="print-title">
            <h1>Przed wizytą</h1>
            <span>Raport przygotowany przez pacjenta · wersja {version.version}</span>
          </div>
          <h2>
            {appointment.doctor.specialty} · {appointment.doctor.name}
          </h2>
          <p>
            {fullPrintDate(appointment.scheduledAt)} · {appointment.facility.name}
          </p>
          {reportFields.map(({ key, label }) => (
            <section key={key}>
              <h3>{label}</h3>
              <p>
                {version.report.sections[key].text || 'Nie podano — do wyjaśnienia podczas wizyty.'}
              </p>
            </section>
          ))}
          <footer>
            Zatwierdzono: {new Date(version.approvedAt).toLocaleString('pl-PL')} · ID wersji:{' '}
            {version.id}
            <br />
            Raport demonstracyjny. Informacje zgłoszone przez pacjenta; bez diagnozy i zaleceń.
          </footer>
        </article>
      )}
    </div>
  )
}

function fullPrintDate(iso: string) {
  return new Date(iso).toLocaleString('pl-PL', {
    timeZone: 'Europe/Warsaw',
    dateStyle: 'long',
    timeStyle: 'short',
  })
}
