import { useState } from 'react'
import { Alert, Button, Form, Input, Segmented } from 'antd'
import {
  ArrowRightOutlined,
  CalendarOutlined,
  CheckCircleFilled,
  FileTextOutlined,
  LockOutlined,
  MailOutlined,
} from '@ant-design/icons'
import type { DataMode } from '../models'
import { DEMO_EMAIL, DEMO_PASSWORD } from '../data/mock'
import { Brand } from './Brand'

export function Login({
  onLogin,
}: {
  onLogin: (email: string, password: string, mode: DataMode) => Promise<void>
}) {
  const [mode, setMode] = useState<DataMode>('api')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  async function submit(
    values: { email: string; password: string },
    selectedMode: DataMode = mode,
  ) {
    setLoading(true)
    setError('')
    try {
      await onLogin(values.email, values.password, selectedMode)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Nie udało się zalogować.')
    } finally {
      setLoading(false)
    }
  }
  return (
    <main className="login-page">
      <section className="login-story">
        <Brand light />
        <div className="login-story-content">
          <span className="eyebrow">DOBRY DZIEŃ ZACZYNA SIĘ OD DOBREJ ORGANIZACJI</span>
          <h1>
            Spokojniejsza recepcja.
            <br />
            <span>Lepiej przygotowana wizyta.</span>
          </h1>
          <p>Kalendarz, zaproszenia i przygotowanie pacjentów — wszystko w jednym miejscu.</p>
          <div className="login-preview" aria-hidden="true">
            <div className="preview-heading">
              <span>
                <CalendarOutlined /> Plan dnia
              </span>
              <small>PONIEDZIAŁEK</small>
            </div>
            <div className="preview-visit">
              <time>08:30</time>
              <div>
                <b>Jan Malinowski</b>
                <small>dr Anna Kowalska · Gabinet 01</small>
              </div>
              <CheckCircleFilled />
            </div>
            <div className="preview-visit">
              <time>09:30</time>
              <div>
                <b>Zofia Wójcik</b>
                <small>dr Piotr Wiśniewski · Gabinet 02</small>
              </div>
              <span className="preview-dot" />
            </div>
            <div className="preview-report">
              <FileTextOutlined />
              <div>
                <b>Wszystko gotowe na wizytę</b>
                <small>Raport pacjenta jest już dostępny.</small>
              </div>
            </div>
          </div>
        </div>
        <footer>DocPrep · Przestrzeń na dobrą opiekę</footer>
      </section>
      <section className="login-form-side">
        <span className="demo-pill">
          <span /> Panel personelu
        </span>
        <div className="login-form-wrap">
          <div className="login-icon">
            <LockOutlined />
          </div>
          <h2>Witaj w panelu przychodni</h2>
          <p>Zaloguj się, aby zobaczyć plan wizyt w przychodni.</p>
          <Segmented
            block
            value={mode}
            onChange={(value) => {
              setMode(value as DataMode)
              setError('')
            }}
            options={[
              { label: 'Konto placówki', value: 'api' },
              { label: 'Demo', value: 'demo' },
            ]}
            className="login-mode"
          />
          <Form
            layout="vertical"
            onFinish={submit}
            requiredMark={false}
            initialValues={{ email: DEMO_EMAIL }}
          >
            <Form.Item
              name="email"
              label="Adres e-mail"
              rules={[
                { required: true, message: 'Podaj adres e-mail.' },
                { type: 'email', message: 'Podaj poprawny adres e-mail.' },
              ]}
            >
              <Input
                size="large"
                prefix={<MailOutlined />}
                autoComplete="username"
                placeholder="recepcja@przychodnia.pl"
              />
            </Form.Item>
            <Form.Item
              name="password"
              label="Hasło"
              rules={[{ required: true, message: 'Podaj hasło.' }]}
            >
              <Input.Password
                size="large"
                prefix={<LockOutlined />}
                autoComplete="current-password"
                placeholder="Hasło"
              />
            </Form.Item>
            {error && <Alert title={error} type="error" showIcon className="form-alert" />}
            <Button
              block
              size="large"
              type="primary"
              htmlType="submit"
              aria-label="Zaloguj się"
              loading={loading}
            >
              Zaloguj się <ArrowRightOutlined />
            </Button>
          </Form>
          <div className="login-divider">
            <span>poznaj panel bez konfiguracji</span>
          </div>
          <Button
            block
            size="large"
            loading={loading}
            onClick={() => submit({ email: DEMO_EMAIL, password: DEMO_PASSWORD }, 'demo')}
          >
            Otwórz wersję demo <ArrowRightOutlined />
          </Button>
          <Button
            block
            size="large"
            className="doctor-demo-button"
            aria-label="Otwórz demo lekarza"
            loading={loading}
            onClick={() =>
              submit({ email: 'doctor@docprep.local', password: DEMO_PASSWORD }, 'demo')
            }
          >
            Otwórz demo lekarza <ArrowRightOutlined />
          </Button>
          <div className="demo-credentials">
            <b>Konto demonstracyjne</b>
            <span>{DEMO_EMAIL}</span>
            <span>
              Hasło: <code>{DEMO_PASSWORD}</code>
            </span>
            <small>
              Przyciski demo korzystają z fikcyjnych danych. Konto placówki łączy się z serwerem.
            </small>
          </div>
        </div>
        <footer>Panel dla zespołu przychodni</footer>
      </section>
    </main>
  )
}
