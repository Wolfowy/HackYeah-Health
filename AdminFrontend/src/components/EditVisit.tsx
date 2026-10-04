import { useState } from 'react'
import { Alert, DatePicker, Form, Input, InputNumber, Modal } from 'antd'
import type { Appointment, UpdateAppointment } from '../models'
import { inWarsaw } from '../lib/date'
import type { Dayjs } from 'dayjs'

export function EditVisit({
  visit,
  onClose,
  onSave,
}: {
  visit: Appointment
  onClose: () => void
  onSave: (visit: Appointment, input: UpdateAppointment) => Promise<Appointment>
}) {
  const [form] = Form.useForm()
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  async function save(values: {
    scheduledAt: Dayjs
    serviceExpiresAt: Dayjs
    room: string
    durationMinutes: number
  }) {
    if (
      values.scheduledAt.valueOf() <= Date.now() ||
      values.serviceExpiresAt.valueOf() <= values.scheduledAt.valueOf()
    ) {
      setError('Podaj przyszły termin i późniejszy koniec dostępu.')
      return
    }
    setBusy(true)
    setError('')
    try {
      await onSave(visit, {
        scheduledAt: values.scheduledAt.toISOString(),
        serviceExpiresAt: values.serviceExpiresAt.toISOString(),
        room: values.room || '',
        durationMinutes: values.durationMinutes,
      })
      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Nie udało się zapisać wizyty.')
    } finally {
      setBusy(false)
    }
  }
  return (
    <Modal
      open
      title="Zmień termin wizyty"
      okText="Zapisz zmiany"
      cancelText="Wróć"
      onCancel={onClose}
      onOk={() => form.submit()}
      confirmLoading={busy}
    >
      <Form
        form={form}
        layout="vertical"
        onFinish={save}
        initialValues={{
          scheduledAt: inWarsaw(visit.scheduledAt),
          serviceExpiresAt: inWarsaw(visit.serviceExpiresAt),
          room: visit.room,
          durationMinutes: visit.durationMinutes || 30,
        }}
      >
        <Form.Item name="scheduledAt" label="Nowy termin" rules={[{ required: true }]}>
          <DatePicker showTime format="DD.MM.YYYY HH:mm" style={{ width: '100%' }} />
        </Form.Item>
        <Form.Item
          name="serviceExpiresAt"
          label="Dostęp do wywiadu do"
          rules={[{ required: true }]}
        >
          <DatePicker showTime format="DD.MM.YYYY HH:mm" style={{ width: '100%' }} />
        </Form.Item>
        <Form.Item name="durationMinutes" label="Czas wizyty (min)" rules={[{ required: true }]}>
          <InputNumber min={5} max={240} precision={0} />
        </Form.Item>
        <Form.Item name="room" label="Gabinet">
          <Input maxLength={100} />
        </Form.Item>
        {error && <Alert type="error" showIcon title={error} />}
      </Form>
    </Modal>
  )
}
