import {
  ArrowRight,
  CalendarDays,
  Check,
  Clock3,
  FileText,
  MapPin,
  ShieldCheck,
} from 'lucide-react'
import type { Appointment, Interview } from '../models'
import { appointmentDay, appointmentTime, fullDate } from '../lib/format'
import { statusLabels } from '../data/mock'

export function Appointments({
  appointments,
  interviews,
  onSelect,
}: {
  appointments: Appointment[]
  interviews: Record<string, Interview>
  onSelect: (id: string) => void
}) {
  return (
    <div className="appointments-page">
      <div className="list-tabs">
        <span className="active">
          Nadchodzące <span>{appointments.length}</span>
        </span>
        <span className="list-note">Zadbaj o spokojny początek wizyty.</span>
      </div>
      <div className="appointment-list">
        {appointments.map((appointment) => {
          const status = interviews[appointment.id].status
          const date = new Date(appointment.scheduledAt)
          return (
            <article className="appointment-list-card card" key={appointment.id}>
              <div className="date-tile">
                <span>
                  {date
                    .toLocaleDateString('pl-PL', { month: 'short', timeZone: 'Europe/Warsaw' })
                    .replace('.', '')}
                </span>
                <strong>
                  {date.toLocaleDateString('pl-PL', { day: 'numeric', timeZone: 'Europe/Warsaw' })}
                </strong>
              </div>
              <div className="appointment-list-main">
                <span className={`pill ${status === 'shared' ? 'success' : 'lavender'}`}>
                  {status === 'shared' && <Check size={12} />}
                  {statusLabels[status]}
                </span>
                <h2>{appointment.doctor.specialty}</h2>
                <p>{appointment.doctor.name}</p>
                <div className="appointment-metadata">
                  <span>
                    <CalendarDays size={14} />
                    {appointmentDay(appointment.scheduledAt)},{' '}
                    {appointmentTime(appointment.scheduledAt)}
                  </span>
                  <span>
                    <MapPin size={14} />
                    {appointment.facility.name}
                  </span>
                </div>
              </div>
              <button className="button secondary" onClick={() => onSelect(appointment.id)}>
                {status === 'shared'
                  ? 'Zobacz raport'
                  : status === 'not_started'
                    ? 'Przygotuj się'
                    : 'Kontynuuj'}
                <ArrowRight size={16} />
              </button>
            </article>
          )
        })}
      </div>
      <div className="appointment-tip">
        <span className="small-icon">
          <FileText size={20} />
        </span>
        <div>
          <h3>Kilka minut rozmowy. Więcej czasu dla Ciebie.</h3>
          <p>
            Przygotuj objawy, leki i pytania. Lekarz będzie mógł zapoznać się z zatwierdzonym
            raportem przed wizytą.
          </p>
        </div>
      </div>
    </div>
  )
}

export function AppointmentDetails({
  appointment,
  interview,
  onInterview,
  onSummary,
}: {
  appointment: Appointment
  interview: Interview
  onInterview: () => void
  onSummary: () => void
}) {
  return (
    <div className="appointment-details">
      <section className="appointment-detail-card card">
        <div className="detail-card-heading">
          <span className="pill lavender">
            <CalendarDays size={14} />
            {appointmentDay(appointment.scheduledAt)} · {appointmentTime(appointment.scheduledAt)}
          </span>
          <span className="mini-tag">{appointment.externalAppointmentId}</span>
        </div>
        <div className="detail-doctor">
          <span className="doctor-avatar large">{appointment.doctor.initials}</span>
          <div>
            <span>{appointment.doctor.specialty}</span>
            <h2>{appointment.doctor.name}</h2>
            <p>Twoja nadchodząca wizyta</p>
          </div>
        </div>
        <div className="detail-rows">
          <div>
            <CalendarDays size={20} />
            <div>
              <span>Termin wizyty</span>
              <strong>
                {fullDate(appointment.scheduledAt)}, {appointmentTime(appointment.scheduledAt)}
              </strong>
            </div>
          </div>
          <div>
            <MapPin size={20} />
            <div>
              <span>Placówka</span>
              <strong>{appointment.facility.name}</strong>
              <p>
                {appointment.facility.address} · {appointment.room}
              </p>
            </div>
          </div>
          <div>
            <Clock3 size={20} />
            <div>
              <span>Możesz uzupełnić wywiad do</span>
              <strong>
                {fullDate(appointment.editDeadline)}, {appointmentTime(appointment.editDeadline)}
              </strong>
              <p>Przykładowy termin w wersji demonstracyjnej.</p>
            </div>
          </div>
        </div>
        <div className="action-buttons">
          <button className="button primary" onClick={onInterview}>
            {interview.answers.length ? 'Wróć do rozmowy' : 'Rozpocznij wywiad'}
            <ArrowRight size={17} />
          </button>
          <button className="button secondary" onClick={onSummary}>
            <FileText size={16} />
            Moje podsumowanie
          </button>
        </div>
      </section>
      <div className="before-visit-card">
        <span className="small-icon">
          <ShieldCheck size={22} />
        </span>
        <h3>Przygotuj się bez pośpiechu</h3>
        <p>
          Rozmowę możesz przerwać w dowolnym momencie i kontynuować w ramach otwartej sesji demo.
        </p>
        <ul>
          <li>
            <Check size={15} />
            Opisz to, co Cię niepokoi.
          </li>
          <li>
            <Check size={15} />
            Przygotuj nazwy i dawki leków.
          </li>
          <li>
            <Check size={15} />
            Zapisz pytania do lekarza.
          </li>
          <li>
            <Check size={15} />
            Sprawdź raport przed udostępnieniem.
          </li>
        </ul>
      </div>
    </div>
  )
}
