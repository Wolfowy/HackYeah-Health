import { useEffect, useRef, useState } from 'react'
import type { Conversation, PartialOptions } from '@elevenlabs/client'
import type { ConversationMode, Message, VoiceState } from '../models'
import { agentApi, type AgentAccess, type AgentResult } from '../lib/agent-api'

export type AgentPhase =
  | 'idle'
  | 'requesting_microphone'
  | 'connecting'
  | 'connected'
  | 'ending'
  | 'processing'
  | 'completed'
  | 'error'

export function useAgentConversation(access: AgentAccess) {
  const [phase, setPhase] = useState<AgentPhase>('idle')
  const [voiceState, setVoiceState] = useState<VoiceState>('idle')
  const [mode, setMode] = useState<ConversationMode>('voice')
  const [messages, setMessages] = useState<Message[]>([])
  const [error, setError] = useState('')
  const [level, setLevel] = useState(0)
  const [result, setResult] = useState<AgentResult | null>(null)
  const [muted, setMuted] = useState(false)
  const [sound, setSound] = useState(true)
  const sdk = useRef<Conversation | null>(null)
  const sessionId = useRef<string | null>(null)
  const generation = useRef(0)
  const connecting = useRef(false)
  const abort = useRef<AbortController | null>(null)
  const history = useRef(messages)
  history.current = messages

  useEffect(
    () => () => {
      generation.current += 1
      abort.current?.abort()
      void sdk.current?.endSession().catch(() => {})
      if (sessionId.current) void agentApi.endSession(access, sessionId.current).catch(() => {})
    },
    [access],
  )

  useEffect(() => {
    if (phase !== 'connected' || mode !== 'voice') {
      setLevel(0)
      return
    }
    const timer = setInterval(() => {
      const conversation = sdk.current
      if (conversation)
        setLevel(Math.max(conversation.getInputVolume(), conversation.getOutputVolume()))
    }, 80)
    return () => clearInterval(timer)
  }, [phase, mode])

  useEffect(() => {
    if (phase !== 'processing') return
    const controller = new AbortController()
    let timeout: ReturnType<typeof setTimeout>
    const startedAt = Date.now()
    async function poll() {
      try {
        const value = await agentApi.getResult(access, controller.signal)
        if (controller.signal.aborted) return
        setResult(value)
        if (value.status === 'completed') {
          setPhase('completed')
          return
        }
      } catch (cause) {
        if (controller.signal.aborted) return
        setError(cause instanceof Error ? cause.message : 'Podsumowanie jest chwilowo niedostępne.')
      }
      if (Date.now() - startedAt < 60000) timeout = setTimeout(poll, 2000)
    }
    void poll()
    return () => {
      controller.abort()
      clearTimeout(timeout)
    }
  }, [phase, access])

  async function start(nextMode = mode) {
    if (connecting.current) return
    connecting.current = true
    const run = ++generation.current
    abort.current?.abort()
    const controller = new AbortController()
    abort.current = controller
    setError('')
    try {
      if (sessionId.current) {
        await agentApi.endSession(access, sessionId.current, true)
        sessionId.current = null
      }
      if (sdk.current) {
        await sdk.current.endSession()
        sdk.current = null
      }
      setMode(nextMode)
      setMuted(false)
      if (nextMode === 'voice') {
        setPhase('requesting_microphone')
        if (!navigator.mediaDevices?.getUserMedia)
          throw new Error(
            'Mikrofon wymaga HTTPS lub lokalnego adresu. Możesz kontynuować tekstowo.',
          )
        const permission = await navigator.mediaDevices.getUserMedia({ audio: true })
        permission.getTracks().forEach((track) => track.stop())
      }
      if (generation.current !== run) return
      setPhase('connecting')
      const credential = await agentApi.startSession(access, nextMode, controller.signal)
      sessionId.current = credential.sessionId
      if (generation.current !== run) {
        void agentApi.endSession(access, credential.sessionId).catch(() => {})
        return
      }
      if (credential.mode !== nextMode)
        throw new Error('Serwer zwrócił nieprawidłowy tryb rozmowy.')
      const { Conversation: ElevenConversation } = await import('@elevenlabs/client')
      if (generation.current !== run) return
      const options: PartialOptions = {
        ...(credential.mode === 'voice'
          ? {
              conversationToken: credential.conversationToken,
              connectionType: 'webrtc' as const,
              textOnly: false,
            }
          : {
              signedUrl: credential.signedUrl,
              connectionType: 'websocket' as const,
              textOnly: true,
            }),
        userId: credential.userId,
        dynamicVariables: credential.dynamicVariables,
        onMessage: (event) => {
          if (generation.current !== run || (event.role === 'user' && nextMode === 'text')) return
          const id = `${credential.sessionId}:${event.role}:${event.response_id ?? event.event_id}`
          setMessages((previous) => {
            const message: Message = {
              id,
              role: event.role === 'agent' ? 'assistant' : 'patient',
              text: event.message,
              mode: nextMode,
              createdAt: new Date().toISOString(),
              questionId: null,
            }
            return previous.some((item) => item.id === id)
              ? previous.map((item) => (item.id === id ? message : item))
              : [...previous, message]
          })
        },
        onModeChange: (event) => {
          if (generation.current === run) setVoiceState(event.mode)
        },
        onDisconnect: (details) => {
          if (generation.current !== run) return
          generation.current += 1
          sdk.current = null
          setVoiceState('idle')
          void agentApi
            .endSession(access, credential.sessionId)
            .catch(() =>
              setError('Nie udało się potwierdzić zakończenia. Oczekujemy na zapis rozmowy.'),
            )
          if (details.reason === 'error') {
            setError('Połączenie zostało przerwane. Możesz rozpocząć kolejną sesję.')
            setPhase('error')
          } else {
            setPhase('processing')
          }
        },
        onError: () => {
          if (generation.current === run) {
            generation.current += 1
            void sdk.current?.endSession().catch(() => {})
            sdk.current = null
            void agentApi.endSession(access, credential.sessionId).catch(() => {})
            setError('Nie udało się połączyć z asystentem. Spróbuj ponownie.')
            setPhase('error')
            setVoiceState('idle')
          }
        },
      }
      const instance = await ElevenConversation.startSession(options)
      if (generation.current !== run) {
        await instance.endSession()
        return
      }
      sdk.current = instance
      await agentApi.bindConversation(
        access,
        credential.sessionId,
        instance.getId(),
        controller.signal,
      )
      if (generation.current !== run) {
        await instance.endSession()
        return
      }
      if (history.current.length)
        instance.sendContextualUpdate(
          history.current
            .map(
              (message) =>
                `${message.role === 'patient' ? 'Pacjent' : 'Asystent'}: ${message.text}`,
            )
            .join('\n'),
        )
      instance.setVolume({ volume: sound ? 1 : 0 })
      setVoiceState('listening')
      setPhase('connected')
    } catch (cause) {
      if (generation.current !== run) return
      generation.current += 1
      await sdk.current?.endSession().catch(() => {})
      sdk.current = null
      if (sessionId.current) void agentApi.endSession(access, sessionId.current).catch(() => {})
      setError(
        cause instanceof DOMException && cause.name === 'NotAllowedError'
          ? 'Nie udzielono dostępu do mikrofonu. Możesz kontynuować tekstowo.'
          : cause instanceof Error
            ? cause.message
            : 'Nie udało się rozpocząć rozmowy.',
      )
      setPhase('error')
      setVoiceState('idle')
    } finally {
      connecting.current = false
    }
  }

  async function changeMode(nextMode: ConversationMode) {
    if (nextMode === mode || connecting.current) return
    if (phase === 'connected') await start(nextMode)
    else {
      setMode(nextMode)
      setError('')
      setPhase('idle')
    }
  }
  function sendMessage(text: string) {
    if (!text.trim() || phase !== 'connected' || !sdk.current) return false
    sdk.current.sendUserMessage(text.trim())
    setMessages((previous) => [
      ...previous,
      {
        id: crypto.randomUUID(),
        role: 'patient',
        text: text.trim(),
        mode,
        createdAt: new Date().toISOString(),
        questionId: null,
      },
    ])
    return true
  }
  async function end() {
    if (!sdk.current) return
    setPhase('ending')
    try {
      await sdk.current.endSession()
      setPhase('processing')
    } catch {
      setError('Nie udało się zakończyć połączenia. Spróbuj ponownie.')
      setPhase('error')
    }
  }
  function toggleMute() {
    sdk.current?.setMicMuted(!muted)
    setMuted(!muted)
    setVoiceState(muted ? 'listening' : 'paused')
  }
  function toggleSound() {
    sdk.current?.setVolume({ volume: sound ? 0 : 1 })
    setSound(!sound)
  }
  async function refreshResult() {
    try {
      const value = await agentApi.getResult(access)
      setResult(value)
      setError('')
      if (value.status === 'completed') setPhase('completed')
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Nie udało się pobrać podsumowania.')
    }
  }
  return {
    phase,
    voiceState: muted ? ('paused' as const) : voiceState,
    mode,
    messages,
    error,
    level,
    result,
    muted,
    sound,
    start,
    changeMode,
    sendMessage,
    end,
    toggleMute,
    toggleSound,
    refreshResult,
  }
}
