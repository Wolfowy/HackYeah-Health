import { Button, Calendar, Empty } from 'antd'
import { FileTextOutlined, VideoCameraOutlined } from '@ant-design/icons'
import type { Dayjs } from 'dayjs'
import type { Appointment } from '../models'
import { formatTime, inWarsaw, weekStart } from '../lib/date'
import { calendarPlacements, statusConfig } from '../lib/appointments'

export function AppointmentCalendar({
  visits,
  selectedDate,
  view,
  onDateChange,
  onOpen,
}: {
  visits: Appointment[]
  selectedDate: Dayjs
  view: 'week' | 'month'
  onDateChange: (date: Dayjs) => void
  onOpen: (visit: Appointment) => void
}) {
  if (view === 'month')
    return (
      <div className="month-calendar">
        <Calendar
          value={selectedDate}
          headerRender={() => null}
          onSelect={(date, info) => {
            if (info.source === 'date') onDateChange(date)
          }}
          cellRender={(date, info) => {
            if (info.type !== 'date') return info.originNode
            const daily = visits.filter((visit) => inWarsaw(visit.scheduledAt).isSame(date, 'day'))
            return (
              <div className="month-events">
                {daily.slice(0, 3).map((visit) => (
                  <button
                    key={visit.visitId}
                    className={`month-event ${statusConfig[visit.status].className}`}
                    onClick={(event) => {
                      event.stopPropagation()
                      onOpen(visit)
                    }}
                    title={`${formatTime(visit.scheduledAt)} ${visit.patient.name}`}
                  >
                    <span />
                    {formatTime(visit.scheduledAt)} {visit.patient.name}
                  </button>
                ))}
                {daily.length > 3 && (
                  <Button
                    type="link"
                    size="small"
                    onClick={(event) => {
                      event.stopPropagation()
                      onDateChange(date)
                    }}
                  >
                    +{daily.length - 3} więcej
                  </Button>
                )}
              </div>
            )
          }}
        />
      </div>
    )

  const start = weekStart(selectedDate)
  const days = Array.from({ length: 7 }, (_, index) => start.add(index, 'day'))
  const hours = Array.from({ length: 11 }, (_, index) => 8 + index)
  // Include appointments outside usual clinic hours instead of silently hiding them.
  const firstHour = Math.min(8, ...visits.map((visit) => inWarsaw(visit.scheduledAt).hour()))
  const lastHour = Math.max(
    18,
    ...visits.map((visit) =>
      Math.ceil(
        (inWarsaw(visit.scheduledAt).hour() * 60 +
          inWarsaw(visit.scheduledAt).minute() +
          (visit.durationMinutes ?? 30)) /
          60,
      ),
    ),
  )
  const displayedHours =
    firstHour === 8 && lastHour === 18
      ? hours
      : Array.from({ length: lastHour - firstHour + 1 }, (_, index) => firstHour + index)
  return (
    <div className="week-scroll">
      <div className="week-calendar">
        <div className="week-header">
          <div className="time-zone">Warszawa</div>
          {days.map((date) => (
            <button
              key={date.toISOString()}
              className={`day-heading ${date.isSame(selectedDate, 'day') ? 'selected' : ''} ${date.isSame(inWarsaw(), 'day') ? 'today' : ''}`}
              onClick={() => onDateChange(date)}
            >
              <span>{date.format('ddd')}</span>
              <b>{date.format('D')}</b>
              {date.isSame(inWarsaw(), 'day') && <i />}
            </button>
          ))}
        </div>
        <div className="week-body" style={{ height: displayedHours.length * 60 }}>
          <div className="time-labels">
            {displayedHours.map((hour) => (
              <span key={hour}>{String(hour).padStart(2, '0')}:00</span>
            ))}
          </div>
          {days.map((date) => (
            <div
              key={date.toISOString()}
              className={`day-column ${date.isSame(selectedDate, 'day') ? 'selected' : ''} ${date.day() === 0 || date.day() === 6 ? 'weekend' : ''}`}
            >
              {displayedHours.map((hour) => (
                <div key={hour} className="hour-line" />
              ))}
              {calendarPlacements(
                visits.filter((visit) => inWarsaw(visit.scheduledAt).isSame(date, 'day')),
              ).map(({ visit, column, columnCount }) => {
                const time = inWarsaw(visit.scheduledAt)
                const top = time.hour() * 60 + time.minute() - firstHour * 60
                return (
                  <button
                    key={visit.visitId}
                    className={`calendar-event ${statusConfig[visit.status].className} ${(visit.durationMinutes ?? 30) < 40 ? 'compact' : ''} ${(visit.durationMinutes ?? 30) < 25 ? 'short' : ''}`}
                    style={{
                      top,
                      height: (visit.durationMinutes ?? 30) - 2,
                      left: `calc(${(column * 100) / columnCount}% + 4px)`,
                      width: `calc(${100 / columnCount}% - 8px)`,
                    }}
                    onClick={() => onOpen(visit)}
                    title={`${formatTime(visit.scheduledAt)} · ${visit.patient.name} · ${visit.doctor.name} · ${statusConfig[visit.status].label}`}
                    aria-label={`${formatTime(visit.scheduledAt)} ${visit.patient.name}, ${statusConfig[visit.status].label}`}
                  >
                    <span className="event-time">
                      {formatTime(visit.scheduledAt)}{' '}
                      {visit.visitType === 'Remote' ? (
                        <VideoCameraOutlined />
                      ) : visit.report ? (
                        <FileTextOutlined />
                      ) : null}
                    </span>
                    <b>{visit.patient.name}</b>
                    <small>{visit.doctor.name.replace('dr ', '')}</small>
                  </button>
                )
              })}
            </div>
          ))}
        </div>
        {!visits.length && (
          <div className="calendar-empty">
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description="Brak wizyt spełniających wybrane filtry"
            />
          </div>
        )}
      </div>
    </div>
  )
}
