import { ArrowRight, CalendarDays, ChevronDown, MapPin, Stethoscope } from 'lucide-react'
import type { VisitDetails } from '../models'
import { formatVisitSchedule } from '../lib/visit'

export function AppointmentCard({
  visit,
  compact = false,
  onDetails,
}: {
  visit: VisitDetails
  compact?: boolean
  onDetails?: () => void
}) {
  const schedule = formatVisitSchedule(visit.scheduledAt, visit.timeZone ?? undefined)
  const hasDetails = !!(visit.room || visit.visitType || visit.locationInstructions)
  return (
    <section
      className={`appointment-card card${compact ? ' appointment-card-compact' : ''}`}
      aria-label="Informacje o wizycie"
    >
      <div className="visit-card-heading">
        <h2>Twoja wizyta</h2>
        <CalendarDays size={17} aria-hidden="true" />
      </div>
      <div className="visit-card-content">
        <div className="visit-card-schedule">
          <span className="visit-card-icon">
            <CalendarDays size={21} aria-hidden="true" />
          </span>
          <div>
            {schedule ? (
              <time dateTime={visit.scheduledAt}>
                <strong>{schedule.date}</strong> <span>godz. {schedule.time}</span>
              </time>
            ) : (
              <strong>Nie podano terminu</strong>
            )}
          </div>
        </div>
        <div className="visit-card-person">
          <span className="visit-card-icon">
            <Stethoscope size={20} aria-hidden="true" />
          </span>
          <div>
            <strong>{visit.doctor?.name || 'Nie podano lekarza'}</strong>
            {visit.doctor?.specialty && <span>{visit.doctor.specialty}</span>}
          </div>
        </div>
        <div className="visit-card-place">
          <span className="visit-card-icon">
            <MapPin size={20} aria-hidden="true" />
          </span>
          <div>
            <strong>{visit.facility?.name || 'Nie podano placówki'}</strong>
            {visit.facility?.address && <span>{visit.facility.address}</span>}
          </div>
        </div>
      </div>
      {hasDetails && (
        <details className="visit-card-details">
          <summary>
            Szczegóły wizyty <ChevronDown size={15} />
          </summary>
          <div>
            {visit.room && (
              <p>
                <span>Gabinet</span> {visit.room}
              </p>
            )}
            {visit.visitType && (
              <p>
                <span>Rodzaj wizyty</span>{' '}
                {{
                  InPerson: 'Wizyta w placówce',
                  Remote: 'Wizyta zdalna',
                  Video: 'Wideokonsultacja',
                  Phone: 'Konsultacja telefoniczna',
                }[visit.visitType] ?? visit.visitType}
              </p>
            )}
            {visit.locationInstructions && (
              <p className="visit-card-instructions">{visit.locationInstructions}</p>
            )}
          </div>
        </details>
      )}
      {onDetails && (
        <button className="visit-card-link" onClick={onDetails}>
          Zobacz wizytę <ArrowRight size={15} />
        </button>
      )}
    </section>
  )
}
