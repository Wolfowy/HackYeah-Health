import { useEffect, useState } from 'react'
import {
  Alert,
  App,
  Avatar,
  Button,
  Descriptions,
  Drawer,
  Empty,
  Input,
  Modal,
  Segmented,
  Space,
  Tabs,
  Timeline,
} from 'antd'
import {
  CalendarOutlined,
  ClockCircleOutlined,
  CopyOutlined,
  FileTextOutlined,
  LinkOutlined,
  MailOutlined,
  PhoneOutlined,
  SendOutlined,
  ReloadOutlined,
  ExportOutlined,
} from '@ant-design/icons'
import type { Appointment, ContactChannel, DataMode, StaffUser, UpdateAppointment } from '../models'
import { canSendInvitation } from '../lib/appointments'
import { formatDateTime, formatTime, inWarsaw, initials } from '../lib/date'
import { invitationUrl, receptionService } from '../services/reception'
import { StatusTag } from './StatusTag'
import { VisitReport } from './VisitReport'
import { EditVisit } from './EditVisit'

export function AppointmentDrawer({
  visit,
  initialTab,
  onClose,
  onSend,
  onRegenerate,
  user,
  mode,
  onRefresh,
  onUpdate,
  onCancel,
  onError,
}: {
  visit: Appointment
  initialTab: string
  onClose: () => void
  onSend: (id: string, channel: ContactChannel, contact?: string) => Promise<void>
  onRegenerate: (id: string, channel: ContactChannel, contact?: string) => Promise<void>
  user: StaffUser
  mode: DataMode
  onRefresh: () => Promise<void>
  onUpdate: (visit: Appointment, input: UpdateAppointment) => Promise<Appointment>
  onCancel: (id: string) => Promise<void>
  onError: (error: unknown) => void
}) {
  const { message } = App.useApp()
  const [channel, setChannel] = useState<ContactChannel>(visit.invitation.channel || 'Sms')
  const [link, setLink] = useState(visit.invitationUrl || '')
  const [linkError, setLinkError] = useState('')
  const [regenerate, setRegenerate] = useState(false)
  const [contact, setContact] = useState('')
  const [activeTab, setActiveTab] = useState(initialTab)
  const [confirmSend, setConfirmSend] = useState(false)
  const [confirmCancel, setConfirmCancel] = useState(false)
  const [edit, setEdit] = useState(false)
  const [busy, setBusy] = useState(false)
  const admin = user.role !== 'Clinician'
  const demo = mode === 'demo'
  const destination =
    (channel === 'Sms' ? visit.patient.phone : visit.patient.email) || contact.trim()
  const url = link || invitationUrl(visit)
  const active = canSendInvitation(visit)
  const reportAvailable = visit.reportAvailable ?? !!visit.report
  useEffect(() => {
    let cancelled = false
    if (!admin || demo || !active) return
    setLinkError('')
    if (visit.invitationUrl) {
      setLink(visit.invitationUrl)
      return
    }
    receptionService
      .getInvitation(visit.visitId)
      .then((url) => {
        if (!cancelled) setLink(url)
      })
      .catch((err) => {
        if (!cancelled) {
          onError(err)
          setLinkError('Istniejący link nie jest dostępny. Możesz utworzyć nowy link.')
        }
      })
    return () => {
      cancelled = true
    }
  }, [visit.visitId, visit.invitationUrl, admin, demo, active])
  async function copyLink() {
    try {
      await navigator.clipboard.writeText(url)
      message.success(demo ? 'Skopiowano link demonstracyjny.' : 'Skopiowano link dla pacjenta.')
    } catch {
      message.error('Nie udało się skopiować. Zaznacz link i skopiuj go ręcznie.')
    }
  }
  async function send() {
    setBusy(true)
    try {
      if (regenerate) {
        await onRegenerate(visit.visitId, channel, contact.trim() || undefined)
        setLink('')
        setLinkError('')
      } else await onSend(visit.visitId, channel, destination)
      setConfirmSend(false)
      message.success(
        demo
          ? 'Symulacja zakończona. Zaproszenie oznaczono jako dostarczone.'
          : 'Utworzono nowe zaproszenie. Status dostarczenia jest widoczny w szczegółach.',
      )
    } catch (err) {
      message.error(err instanceof Error ? err.message : 'Nie udało się wysłać zaproszenia.')
    } finally {
      setBusy(false)
    }
  }
  async function cancel() {
    setBusy(true)
    try {
      await onCancel(visit.visitId)
      setConfirmCancel(false)
      message.success('Anulowano wizytę.')
    } catch (err) {
      message.error(err instanceof Error ? err.message : 'Nie udało się anulować wizyty.')
    } finally {
      setBusy(false)
    }
  }
  async function refresh() {
    setBusy(true)
    try {
      await onRefresh()
    } catch (err) {
      message.error(err instanceof Error ? err.message : 'Nie udało się odświeżyć wizyty.')
    } finally {
      setBusy(false)
    }
  }
  return (
    <>
      <Drawer
        open
        size="min(680px, 100vw)"
        onClose={onClose}
        title={
          <div className="drawer-title">
            Szczegóły wizyty <span>{visit.externalVisitId}</span>
          </div>
        }
        destroyOnHidden
        styles={{ body: { padding: '24px 28px' } }}
        footer={
          <div className="drawer-footer">
            <span>{demo ? 'Dane demonstracyjne · ' : ''}Europe/Warsaw</span>
            <Button onClick={onClose}>Zamknij szczegóły</Button>
          </div>
        }
      >
        <div className="visit-patient-heading">
          <Avatar size={60} className="patient-avatar">
            {initials(visit.patient.name)}
          </Avatar>
          <div>
            <h2>{visit.patient.name}</h2>
            <span>{visit.visitType === 'Remote' ? 'Teleporada' : 'Wizyta w przychodni'}</span>
          </div>
        </div>
        <div className="visit-quick-info">
          <span>
            <CalendarOutlined /> {inWarsaw(visit.scheduledAt).format('D MMMM YYYY')}
          </span>
          <span>
            <ClockCircleOutlined /> {formatTime(visit.scheduledAt)}
            {visit.endsAt ? `–${formatTime(visit.endsAt)}` : ''}
            {visit.durationMinutes ? ` · ${visit.durationMinutes} min` : ''}
          </span>
        </div>
        <div className="visit-actions">
          <Button
            icon={<ReloadOutlined />}
            loading={busy}
            onClick={refresh}
            aria-label="Odśwież status"
          >
            Odśwież status
          </Button>
          {admin && visit.status !== 'Cancelled' && (
            <>
              <Button onClick={() => setEdit(true)}>Zmień termin</Button>
              <Button danger onClick={() => setConfirmCancel(true)}>
                Anuluj wizytę
              </Button>
            </>
          )}
        </div>
        <Tabs
          activeKey={activeTab}
          onChange={setActiveTab}
          destroyOnHidden
          items={[
            {
              key: 'details',
              label: 'Szczegóły',
              children: (
                <div className="visit-details">
                  <div className="details-status">
                    <span>Przygotowanie do wizyty</span>
                    <StatusTag status={visit.status} />
                  </div>
                  <Descriptions
                    column={1}
                    colon={false}
                    size="small"
                    items={[
                      {
                        key: 'doctor',
                        label: 'Lekarz',
                        children: (
                          <div>
                            <b>{visit.doctor.name}</b>
                            <small className="block-muted">{visit.doctor.specialty}</small>
                          </div>
                        ),
                      },
                      {
                        key: 'room',
                        label: 'Miejsce',
                        children:
                          visit.visitType === 'Remote'
                            ? 'Teleporada'
                            : `${visit.room ? `Gabinet ${visit.room} · ` : ''}${visit.facility.name}`,
                      },
                      {
                        key: 'phone',
                        label: 'Telefon',
                        children: (
                          <span>
                            <PhoneOutlined /> {visit.patient.phone || 'Nie udostępniono'}
                          </span>
                        ),
                      },
                      {
                        key: 'email',
                        label: 'E-mail',
                        children: (
                          <span>
                            <MailOutlined /> {visit.patient.email || 'Nie udostępniono'}
                          </span>
                        ),
                      },
                      {
                        key: 'duration',
                        label: 'Czas wizyty',
                        children: visit.durationMinutes
                          ? `${visit.durationMinutes} min`
                          : 'Nie udostępniono',
                      },
                      {
                        key: 'expiry',
                        label: 'Dostęp do wywiadu',
                        children: `Do ${formatDateTime(visit.serviceExpiresAt)}`,
                      },
                    ]}
                  />
                  {admin && (
                    <section className="invitation-card">
                      <div className="section-heading">
                        <div>
                          <h3>
                            <LinkOutlined /> Zaproszenie do wywiadu
                          </h3>
                          <p>Przekaż pacjentowi link przed wizytą.</p>
                        </div>
                      </div>
                      <label className="field-label" htmlFor="invitation-link">
                        Link dla pacjenta
                      </label>
                      <Space.Compact block>
                        <Input
                          id="invitation-link"
                          value={active ? url : ''}
                          readOnly
                          prefix={<LinkOutlined />}
                          placeholder="Utwórz nowe zaproszenie, aby otrzymać link"
                        />
                        <Button
                          icon={<CopyOutlined />}
                          onClick={copyLink}
                          disabled={!active || !url}
                          aria-label="Kopiuj link zaproszenia"
                        />
                      </Space.Compact>
                      <Button
                        type="link"
                        icon={<ExportOutlined />}
                        href={active && url ? url : undefined}
                        target="_blank"
                        rel="noopener noreferrer"
                        disabled={!active || !url}
                        aria-label="Otwórz link pacjenta"
                      >
                        Otwórz wywiad
                      </Button>
                      <p className="invitation-demo-note">
                        {demo
                          ? 'Link demonstracyjny — nie uruchamia prawdziwego wywiadu.'
                          : 'Wysyłka zachowuje link. Utworzenie nowego linku unieważnia poprzednie zaproszenie.'}
                      </p>
                      {linkError && <Alert type="warning" showIcon title={linkError} />}
                      {visit.deliveryMode === 'demo' && (
                        <Alert
                          type="info"
                          showIcon
                          title="Operator działa w trybie demo — wiadomości nie są wysyłane."
                        />
                      )}
                      <div className="invitation-delivery">
                        <span>Status zaproszenia</span>
                        <span className={`delivery-label ${visit.deliveryStatus.toLowerCase()}`}>
                          <i />
                          {visit.deliveryStatus === 'Delivered'
                            ? 'Dostarczone'
                            : visit.deliveryStatus === 'Failed'
                              ? 'Błąd wysyłki'
                              : visit.deliveryStatus === 'Pending'
                                ? 'Oczekuje na wysyłkę'
                                : 'Nie wysłano'}
                        </span>
                      </div>
                      {visit.invitation.lastSentAt && (
                        <p className="last-sent">
                          Ostatnio: {formatDateTime(visit.invitation.lastSentAt)}
                        </p>
                      )}
                      {active ? (
                        <>
                          {demo || visit.patient.phone || visit.patient.email ? (
                            <Segmented
                              block
                              value={channel}
                              onChange={(value) => setChannel(value as ContactChannel)}
                              options={[
                                { label: 'SMS', value: 'Sms', disabled: !visit.patient.phone },
                                { label: 'E-mail', value: 'Email', disabled: !visit.patient.email },
                              ]}
                            />
                          ) : (
                            <>
                              <label className="field-label" htmlFor="invitation-contact">
                                Kontakt do ponowienia zaproszenia
                              </label>
                              <Input
                                id="invitation-contact"
                                value={contact}
                                onChange={(e) => setContact(e.target.value)}
                                maxLength={320}
                                placeholder="Telefon lub e-mail wskazany przy rejestracji"
                              />
                              <small>Kanał wysyłki jest ustalony podczas rejestracji wizyty.</small>
                            </>
                          )}
                          <Button
                            block
                            type="primary"
                            icon={<SendOutlined />}
                            aria-label={
                              visit.deliveryStatus === 'Delivered'
                                ? 'Ponów zaproszenie'
                                : 'Wyślij zaproszenie'
                            }
                            disabled={!destination || (!demo && !url)}
                            onClick={() => {
                              setRegenerate(false)
                              setConfirmSend(true)
                            }}
                          >
                            {visit.deliveryStatus === 'Delivered'
                              ? 'Ponów zaproszenie'
                              : 'Wyślij zaproszenie'}
                          </Button>
                          {!demo && (
                            <Button
                              block
                              disabled={!destination}
                              onClick={() => {
                                setRegenerate(true)
                                setConfirmSend(true)
                              }}
                            >
                              Utwórz nowy link
                            </Button>
                          )}
                        </>
                      ) : (
                        <Alert
                          showIcon
                          type="warning"
                          title={
                            visit.status === 'Cancelled'
                              ? 'Wizyta anulowana — zaproszenie jest nieaktywne.'
                              : 'Dostęp do wywiadu wygasł.'
                          }
                        />
                      )}
                    </section>
                  )}
                  <button className="report-shortcut" onClick={() => setActiveTab('report')}>
                    <span className="report-icon">
                      <FileTextOutlined />
                    </span>
                    <div>
                      <b>
                        {reportAvailable
                          ? 'Raport pacjenta jest dostępny'
                          : 'Raport nie jest jeszcze dostępny'}
                      </b>
                      <small>
                        {reportAvailable
                          ? 'Zobacz zatwierdzony raport pacjenta'
                          : 'Pojawi się po zatwierdzeniu i udostępnieniu przez pacjenta.'}
                      </small>
                    </div>
                  </button>
                </div>
              ),
            },
            {
              key: 'report',
              label: <span>Raport {reportAvailable && <span className="tab-count">1</span>}</span>,
              children: (
                <VisitReport
                  visit={visit}
                  user={user}
                  mode={mode}
                  onRefresh={onRefresh}
                  onError={onError}
                />
              ),
            },
            {
              key: 'activity',
              label: 'Aktywność',
              children: visit.activity.length ? (
                <div className="activity-list">
                  <Timeline
                    items={[...visit.activity]
                      .sort((a, b) => Date.parse(b.at) - Date.parse(a.at))
                      .map((item) => ({
                        color: '#7461dc',
                        content: (
                          <div>
                            <small>{formatDateTime(item.at)}</small>
                            <h4>{item.title}</h4>
                            <p>{item.description}</p>
                          </div>
                        ),
                      }))}
                  />
                </div>
              ) : (
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description="Historia zdarzeń nie została udostępniona"
                />
              ),
            },
          ]}
        />
      </Drawer>
      <Modal
        open={confirmSend}
        title={regenerate ? 'Utwórz nowy link do wywiadu' : 'Wyślij zaproszenie do wywiadu'}
        onCancel={() => setConfirmSend(false)}
        onOk={send}
        confirmLoading={busy}
        okText={demo ? 'Symuluj wysyłkę' : regenerate ? 'Utwórz nowy link' : 'Wyślij zaproszenie'}
        cancelText="Wróć"
        destroyOnHidden
      >
        <p className="send-description">
          Zaproszenie dla <b>{visit.patient.name}</b>
          {demo
            ? ' zostanie oznaczone jako dostarczone.'
            : regenerate
              ? ' otrzyma nowy link. Poprzedni link i sesje wywiadu zostaną unieważnione.'
              : ' zostanie wysłane na zapisany kontakt. Link pozostaje ten sam.'}
        </p>
        <div className="send-recipient">
          <span>Odbiorca</span>
          <b>{destination}</b>
        </div>
        {demo && (
          <Alert type="info" showIcon title="Wersja demo — żadna wiadomość nie zostanie wysłana." />
        )}
      </Modal>
      <Modal
        open={confirmCancel}
        title="Anuluj wizytę"
        okText="Potwierdź anulowanie"
        okButtonProps={{ danger: true }}
        cancelText="Wróć"
        onCancel={() => setConfirmCancel(false)}
        onOk={cancel}
        confirmLoading={busy}
      >
        <p>
          Anulować wizytę {visit.externalVisitId} z {formatDateTime(visit.scheduledAt)}? Link do
          wywiadu i udostępnienie raportu zostaną wyłączone.
        </p>
      </Modal>
      {edit && <EditVisit visit={visit} onClose={() => setEdit(false)} onSave={onUpdate} />}
    </>
  )
}
