import { Avatar, Button, Table } from 'antd'
import { ArrowRightOutlined, FileTextOutlined } from '@ant-design/icons'
import type { Appointment } from '../models'
import { formatTime, inWarsaw, initials } from '../lib/date'
import { StatusTag } from './StatusTag'

export function AppointmentTable({
  visits,
  onOpen,
  reportsOnly = false,
}: {
  visits: Appointment[]
  onOpen: (visit: Appointment) => void
  reportsOnly?: boolean
}) {
  return (
    <Table<Appointment>
      rowKey="visitId"
      dataSource={visits}
      scroll={{ x: 860 }}
      pagination={{
        pageSize: 8,
        showSizeChanger: false,
        hideOnSinglePage: true,
        showTotal: (total) => `${total} wizyt`,
      }}
      locale={{ emptyText: 'Brak wizyt spełniających wybrane filtry' }}
      columns={[
        {
          title: 'Pacjent',
          key: 'patient',
          width: 235,
          render: (_, visit) => (
            <div className="patient-cell">
              <Avatar className="patient-avatar">{initials(visit.patient.name)}</Avatar>
              <div>
                <b>{visit.patient.name}</b>
                <small>{visit.externalVisitId}</small>
              </div>
            </div>
          ),
        },
        {
          title: 'Termin',
          key: 'date',
          width: 150,
          render: (_, visit) => (
            <div className="table-two-lines">
              <b>{inWarsaw(visit.scheduledAt).format('D MMM YYYY')}</b>
              <small>
                {formatTime(visit.scheduledAt)}
                {visit.durationMinutes ? ` · ${visit.durationMinutes} min` : ''}
              </small>
            </div>
          ),
        },
        {
          title: 'Lekarz',
          key: 'doctor',
          width: 195,
          render: (_, visit) => (
            <div className="table-two-lines">
              <b>{visit.doctor.name}</b>
              <small>{visit.visitType === 'Remote' ? 'Teleporada' : `Gabinet ${visit.room}`}</small>
            </div>
          ),
        },
        {
          title: reportsOnly ? 'Raport' : 'Przygotowanie',
          key: 'status',
          width: 185,
          render: (_, visit) =>
            reportsOnly ? (
              <div className="table-two-lines">
                <b>
                  <FileTextOutlined />{' '}
                  {visit.report ? `Wersja ${visit.report.versionNumber}` : 'Otwórz raport'}
                </b>
                <small>Zatwierdzony przez pacjenta</small>
              </div>
            ) : (
              <StatusTag status={visit.status} />
            ),
        },
        {
          title: 'Zaproszenie',
          key: 'delivery',
          width: 145,
          render: (_, visit) => (
            <span className={`delivery-label ${visit.deliveryStatus.toLowerCase()}`}>
              <i />
              {visit.deliveryStatus === 'Delivered'
                ? 'Dostarczone'
                : visit.deliveryStatus === 'Failed'
                  ? 'Błąd wysyłki'
                  : 'Do wysłania'}
            </span>
          ),
        },
        {
          title: '',
          key: 'action',
          width: 58,
          render: (_, visit) => (
            <Button
              type="text"
              icon={<ArrowRightOutlined />}
              aria-label={`Otwórz wizytę ${visit.patient.name}`}
              onClick={() => onOpen(visit)}
            />
          ),
        },
      ]}
    />
  )
}
