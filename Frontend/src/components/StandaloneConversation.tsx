import { useEffect, useState } from 'react'
import { CalendarDays, LoaderCircle } from 'lucide-react'
import { appointments } from '../data/mock'
import { createInterview, submitAnswer } from '../lib/interview'
import { appointmentDay, appointmentTime } from '../lib/format'
import { agentApi, type AgentAccess, type AgentInterviewInfo } from '../lib/agent-api'
import type { StandaloneRoute } from '../lib/routes'
import { Conversation } from './Conversation'
import { LiveConversation } from './LiveConversation'
import { Summary } from './Summary'

export function StandaloneConversation({ route }: { route: StandaloneRoute }) {
  const demoAppointment =
    route.kind === 'invitation'
      ? appointments.find((appointment) => appointment.id === route.token)
      : undefined
  const [demoInterview, setDemoInterview] = useState(() =>
    createInterview(demoAppointment ?? appointments[0]),
  )
  const [showSummary, setShowSummary] = useState(false)
  const [access, setAccess] = useState<AgentAccess | null>(null)
  const [info, setInfo] = useState<AgentInterviewInfo | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(!demoAppointment)

  useEffect(() => {
    if (demoAppointment) return
    const controller = new AbortController()
    async function load() {
      try {
        if (route.kind === 'expired_session')
          throw new Error('Sesja zakończyła się. Otwórz ponownie link otrzymany od placówki.')
        if (route.kind === 'invitation') {
          const response = await agentApi.authorizeInvitation(route.token, controller.signal)
          if (controller.signal.aborted) return
          setAccess(response.access)
          setInfo(response.interview)
          window.history.replaceState(null, '', '/rozmowa')
        } else {
          const response = await agentApi.getVisitInterview(route.visitId, controller.signal)
          if (controller.signal.aborted) return
          setAccess({ kind: 'patient', interviewId: response.interviewId })
          setInfo(response.interview)
        }
      } catch (cause) {
        if (!controller.signal.aborted)
          setError(cause instanceof Error ? cause.message : 'Nie udało się otworzyć rozmowy.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [route, demoAppointment])

  const scheduledAt = demoAppointment?.scheduledAt ?? info?.visitDate
  return (
    <div className="standalone-shell">
      <header className="standalone-header">
        <span className="brand">
          <span className="brand-ring" />
          Przed wizytą<span className="brand-dot">.</span>
        </span>
        {scheduledAt && (
          <span className="standalone-visit">
            <CalendarDays size={15} />
            {appointmentDay(scheduledAt)} · {appointmentTime(scheduledAt)}
          </span>
        )}
      </header>
      <main className="standalone-content">
        {loading ? (
          <div className="invitation-state" role="status">
            <LoaderCircle className="spin" size={24} />
            <p>Otwieram rozmowę…</p>
          </div>
        ) : error ? (
          <div className="invitation-state">
            <h1>Nie można otworzyć rozmowy</h1>
            <p role="alert">{error}</p>
          </div>
        ) : demoAppointment ? (
          showSummary ? (
            <Summary
              interview={demoInterview}
              appointment={demoAppointment}
              authenticated={false}
              onChange={setDemoInterview}
              onBack={() => setShowSummary(false)}
              notify={() => {}}
            />
          ) : (
            <Conversation
              interview={demoInterview}
              onAnswer={(text, mode) =>
                setDemoInterview((previous) => submitAnswer(previous, text, mode))
              }
              onSummary={() => setShowSummary(true)}
              onReset={() => setDemoInterview(createInterview(demoAppointment))}
            />
          )
        ) : (
          access && <LiveConversation access={access} />
        )}
      </main>
    </div>
  )
}
