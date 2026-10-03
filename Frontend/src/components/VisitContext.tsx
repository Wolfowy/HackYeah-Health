import {
  ArrowRight,
  CalendarDays,
  Check,
  ChevronRight,
  FileText,
  MessageCircle,
} from 'lucide-react'
import type { Appointment, Interview } from '../models'
import { appointmentDay, appointmentTime } from '../lib/format'
import { interviewQuestions } from '../data/mock'

export function VisitContext({
  appointment,
  interview,
  onAppointment,
  onSummary,
}: {
  appointment: Appointment
  interview: Interview
  onAppointment: () => void
  onSummary: () => void
}) {
  const completed = Math.min(interview.questionIndex, interviewQuestions.length)
  const percent = Math.round((completed / interviewQuestions.length) * 100)
  return (
    <aside className="context-column" aria-label="Informacje o wywiadzie i wizycie">
      <section className="visit-context card">
        <div className="section-heading">
          <h2>Twoja wizyta</h2>
          <CalendarDays size={18} />
        </div>
        <div className="appointment-date">
          <span className="date-icon">
            <CalendarDays size={21} />
          </span>
          <div>
            <strong>
              {appointmentDay(appointment.scheduledAt)}
              <span> · </span>
              {appointmentTime(appointment.scheduledAt)}
            </strong>
            <p>{appointment.doctor.specialty}</p>
          </div>
        </div>
        <div className="doctor-line">
          <span className="doctor-avatar">{appointment.doctor.initials}</span>
          <div>
            <strong>{appointment.doctor.name}</strong>
            <span>{appointment.facility.name}</span>
          </div>
        </div>
        <button className="context-link" onClick={onAppointment}>
          Szczegóły wizyty
          <ArrowRight size={15} />
        </button>
      </section>
      <section className="interview-context card">
        <div className="section-heading">
          <h2>Twój wywiad</h2>
          <span className="mini-tag">
            {interview.consent.granted ? 'Udostępniony' : 'Wersja robocza'}
          </span>
        </div>
        <div className="progress-label">
          <span>
            {completed === 0
              ? 'Zacznij w swoim tempie'
              : completed === interviewQuestions.length
                ? 'Gotowy do sprawdzenia'
                : 'Krok po kroku do wizyty'}
          </span>
          <strong>{percent}%</strong>
        </div>
        <div
          className="progress-track"
          role="progressbar"
          aria-label="Postęp wywiadu"
          aria-valuenow={percent}
          aria-valuemin={0}
          aria-valuemax={100}
        >
          <span style={{ width: `${percent}%` }} />
        </div>
        <div className="live-report">
          <span className="small-icon">
            <MessageCircle size={16} />
          </span>
          <div>
            <span>Powód wizyty</span>
            <p>{interview.draft.sections.reason.text || 'Twoja historia zaczyna się tutaj.'}</p>
            {interview.draft.sections.reason.text && (
              <small>
                <Check size={12} />
                Odpowiedź dodana
              </small>
            )}
          </div>
        </div>
        <div className="live-report">
          <span className="small-icon">
            <FileText size={16} />
          </span>
          <div>
            <span>Podsumowanie dla lekarza</span>
            <p className="subtle">
              {completed > 0
                ? `${completed} z ${interviewQuestions.length} odpowiedzi zebranych`
                : 'Powstanie z Twoich odpowiedzi.'}
            </p>
          </div>
        </div>
        <button className="context-link" onClick={onSummary}>
          Zobacz podsumowanie
          <ChevronRight size={16} />
        </button>
      </section>
    </aside>
  )
}
