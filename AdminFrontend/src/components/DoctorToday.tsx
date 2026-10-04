import { Button, DatePicker, Empty, Tag } from 'antd'
import {
  FileTextOutlined,
  LeftOutlined,
  RightOutlined,
  ArrowRightOutlined,
} from '@ant-design/icons'
import type { Dayjs } from 'dayjs'
import type { Appointment } from '../models'
import { formatTime, inWarsaw } from '../lib/date'
import { StatusTag } from './StatusTag'

export function DoctorToday({
  visits,
  date,
  onDateChange,
  onOpen,
}: {
  visits: Appointment[]
  date: Dayjs
  onDateChange: (date: Dayjs) => void
  onOpen: (visit: Appointment, tab?: string) => void
}) {
  const daily = visits
    .filter((v) => inWarsaw(v.scheduledAt).isSame(date, 'day'))
    .sort((a, b) => Date.parse(a.scheduledAt) - Date.parse(b.scheduledAt))
  const ready = daily.filter((v) => v.reportAvailable ?? !!v.report).length
  return (
    <section className="doctor-day">
      <div className="doctor-day-toolbar">
        <div>
          <span className="eyebrow">TWÓJ PLAN DNIA</span>
          <h2>{date.format('dddd, D MMMM YYYY')}</h2>
          <p>
            {daily.length} wizyt · {ready} udostępnionych raportów
          </p>
        </div>
        <div className="doctor-date-control">
          <Button
            aria-label="Poprzedni dzień"
            icon={<LeftOutlined />}
            onClick={() => onDateChange(date.subtract(1, 'day'))}
          />
          <DatePicker
            value={date}
            allowClear={false}
            format="DD.MM.YYYY"
            onChange={(d) => d && onDateChange(d)}
            aria-label="Dzień wizyt lekarza"
          />
          <Button
            aria-label="Następny dzień"
            icon={<RightOutlined />}
            onClick={() => onDateChange(date.add(1, 'day'))}
          />
          <Button onClick={() => onDateChange(inWarsaw())}>Dzisiaj</Button>
        </div>
      </div>
      <div className="doctor-queue">
        {daily.map((visit, index) => {
          const available = visit.reportAvailable ?? !!visit.report
          return (
            <article className="doctor-queue-item" key={visit.visitId} data-testid="doctor-visit">
              <div className="queue-time">
                <span>{String(index + 1).padStart(2, '0')}</span>
                <time>{formatTime(visit.scheduledAt)}</time>
                {visit.durationMinutes && <small>{visit.durationMinutes} min</small>}
              </div>
              <div className="queue-patient">
                <h3>{visit.patient.name}</h3>
                <p>
                  {visit.visitType === 'Remote'
                    ? 'Teleporada'
                    : visit.room
                      ? `Gabinet ${visit.room}`
                      : 'Wizyta w przychodni'}{' '}
                  · {visit.externalVisitId}
                </p>
                <div>
                  <StatusTag status={visit.status} />
                  {available ? (
                    <Tag color="green">Raport udostępniony</Tag>
                  ) : (
                    <Tag>Bez raportu</Tag>
                  )}
                </div>
              </div>
              <div className="queue-actions">
                {available && (
                  <Button
                    type="primary"
                    icon={<FileTextOutlined />}
                    onClick={() => onOpen(visit, 'report')}
                    aria-label={`Otwórz raport ${visit.patient.name}`}
                  >
                    Otwórz raport
                  </Button>
                )}
                <Button
                  icon={<ArrowRightOutlined />}
                  onClick={() => onOpen(visit)}
                  aria-label={`Szczegóły ${visit.patient.name}`}
                >
                  Szczegóły wizyty
                </Button>
              </div>
            </article>
          )
        })}
        {!daily.length && (
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nie masz wizyt w tym dniu" />
        )}
      </div>
    </section>
  )
}
