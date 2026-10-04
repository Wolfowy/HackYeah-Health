import { useEffect, useState } from 'react'
import {
  Alert,
  Col,
  DatePicker,
  Form,
  Input,
  InputNumber,
  Modal,
  Row,
  Select,
  TimePicker,
} from 'antd'
import type { Dayjs } from 'dayjs'
import type { Appointment, NewAppointment, DataMode, Doctor } from '../models'
import { doctors } from '../data/mock'
import { receptionService } from '../services/reception'
import { atTime, dayjs, inWarsaw } from '../lib/date'

interface Values {
  patientName: string
  externalVisitId?: string
  pesel?: string
  phone?: string
  email?: string
  doctorId: string
  date: Dayjs
  time: Dayjs
  durationMinutes: number
  room: string
  visitType: 'InPerson' | 'Remote'
}
export function NewAppointmentModal({
  date,
  mode,
  onError,
  onClose,
  onCreate,
}: {
  date: Dayjs
  mode: DataMode
  onError: (error: unknown) => void
  onClose: () => void
  onCreate: (input: NewAppointment) => Promise<Appointment>
}) {
  const [form] = Form.useForm<Values>()
  const [choices, setChoices] = useState<Doctor[]>(mode === 'demo' ? doctors : [])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  useEffect(() => {
    if (mode === 'demo') return
    let cancelled = false
    receptionService
      .getDoctors()
      .then((data) => {
        if (!cancelled) setChoices(data)
      })
      .catch((err) => {
        if (!cancelled) {
          onError(err)
          setError('Nie udało się pobrać lekarzy. Zamknij formularz i spróbuj ponownie.')
        }
      })
    return () => {
      cancelled = true
    }
  }, [mode])
  async function submit(values: Values) {
    setError('')
    setSaving(true)
    try {
      await onCreate({
        patientName: values.patientName.trim(),
        phone: values.phone?.trim() || '',
        email: values.email?.trim() || '',
        externalVisitId: values.externalVisitId?.trim(),
        pesel: values.pesel?.trim(),
        doctorId: values.doctorId,
        scheduledAt: atTime(values.date, values.time.format('HH:mm')).toISOString(),
        durationMinutes: values.durationMinutes,
        room: values.room || '',
        visitType: values.visitType,
      })
      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Nie udało się dodać wizyty.')
    } finally {
      setSaving(false)
    }
  }
  return (
    <Modal
      open
      title="Nowa wizyta"
      onCancel={onClose}
      onOk={() => form.submit()}
      confirmLoading={saving}
      okText="Dodaj wizytę"
      cancelText="Anuluj"
      destroyOnHidden
      width={580}
    >
      <p className="modal-intro">
        Dodaj wizytę do kalendarza. Zaproszenie możesz wysłać z jej szczegółów.
      </p>
      <Form
        form={form}
        layout="vertical"
        requiredMark={false}
        onFinish={submit}
        initialValues={{
          date,
          time: dayjs('2000-01-01T10:00'),
          durationMinutes: 30,
          doctorId: mode === 'demo' ? doctors[0].id : undefined,
          room: '01',
          visitType: 'InPerson',
        }}
      >
        <Form.Item
          name="patientName"
          label="Imię i nazwisko pacjenta"
          rules={[{ required: true, whitespace: true, message: 'Podaj imię i nazwisko.' }]}
        >
          <Input placeholder="np. Jan Malinowski" maxLength={200} />
        </Form.Item>
        {mode === 'api' && (
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="externalVisitId" label="Numer wizyty (opcjonalnie)">
                <Input maxLength={100} placeholder="Nadany automatycznie" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item
                name="pesel"
                label="PESEL (opcjonalnie)"
                rules={[{ pattern: /^\d{11}$/, message: 'Podaj 11 cyfr lub pozostaw puste.' }]}
              >
                <Input maxLength={11} autoComplete="off" />
              </Form.Item>
            </Col>
          </Row>
        )}
        <Row gutter={16}>
          <Col span={12}>
            <Form.Item
              name="phone"
              label="Telefon (do SMS)"
              dependencies={['email']}
              rules={[
                ({ getFieldValue }) => ({
                  validator(_, value: string) {
                    if (!value?.trim() && !getFieldValue('email')?.trim())
                      return Promise.reject(new Error('Podaj telefon lub e-mail.'))
                    if (value?.trim() && !/^\+?[\d\s()-]{9,20}$/.test(value.trim()))
                      return Promise.reject(new Error('Podaj poprawny numer telefonu.'))
                    return Promise.resolve()
                  },
                }),
              ]}
            >
              <Input placeholder="+48 500 100 200" maxLength={20} />
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item
              name="email"
              label="E-mail"
              rules={[{ type: 'email', message: 'Podaj poprawny e-mail.' }]}
            >
              <Input placeholder="pacjent@example.com" maxLength={320} />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item
          name="doctorId"
          label="Lekarz"
          rules={[{ required: true, message: 'Wybierz lekarza.' }]}
        >
          <Select
            showSearch
            optionFilterProp="label"
            placeholder="Wybierz lekarza"
            onChange={(id) =>
              form.setFieldValue(
                'room',
                mode === 'demo'
                  ? String(doctors.findIndex((d) => d.id === id) + 1).padStart(2, '0')
                  : choices.find((d) => d.id === id)?.defaultRoom || '',
              )
            }
            options={choices.map((d) => ({ value: d.id, label: `${d.name} · ${d.specialty}` }))}
          />
        </Form.Item>
        <Row gutter={16}>
          <Col span={12}>
            <Form.Item
              name="date"
              label="Data wizyty"
              rules={[{ required: true, message: 'Wybierz datę.' }]}
            >
              <DatePicker
                style={{ width: '100%' }}
                format="DD.MM.YYYY"
                disabledDate={(d) => d.isBefore(inWarsaw().startOf('day'), 'day')}
              />
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item
              name="time"
              label="Godzina"
              rules={[{ required: true, message: 'Wybierz godzinę.' }]}
            >
              <TimePicker
                style={{ width: '100%' }}
                format="HH:mm"
                minuteStep={15}
                needConfirm={false}
              />
            </Form.Item>
          </Col>
        </Row>
        <Row gutter={16}>
          <Col span={8}>
            <Form.Item name="durationMinutes" label="Czas (min)" rules={[{ required: true }]}>
              <InputNumber min={5} max={240} step={5} precision={0} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col span={8}>
            <Form.Item name="visitType" label="Rodzaj wizyty">
              <Select
                options={[
                  { label: 'W przychodni', value: 'InPerson' },
                  { label: 'Teleporada', value: 'Remote' },
                ]}
              />
            </Form.Item>
          </Col>
          <Col span={8}>
            <Form.Item
              name="room"
              label="Gabinet"
              dependencies={['visitType']}
              rules={[
                ({ getFieldValue }) => ({
                  validator(_, value) {
                    return getFieldValue('visitType') === 'InPerson' && !value?.trim()
                      ? Promise.reject(new Error('Podaj gabinet.'))
                      : Promise.resolve()
                  },
                }),
              ]}
            >
              <Input maxLength={100} />
            </Form.Item>
          </Col>
        </Row>
        {error && <Alert type="error" showIcon title={error} className="form-alert" />}
        {mode === 'demo' && (
          <Alert
            type="info"
            showIcon
            title="Wizyta zostanie dodana tylko do wersji demo."
            description="Zmiany znikną po odświeżeniu strony. Używaj fikcyjnych danych."
          />
        )}
      </Form>
    </Modal>
  )
}
