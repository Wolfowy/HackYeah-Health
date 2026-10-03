import { useEffect, useRef, useState } from 'react'
import {
  ArrowUp,
  AudioLines,
  Check,
  LoaderCircle,
  MessageCircle,
  Mic,
  MicOff,
  PhoneOff,
  Volume2,
  VolumeX,
} from 'lucide-react'
import { useAgentConversation } from '../hooks/useAgentConversation'
import type { AgentAccess } from '../lib/agent-api'
import { Orb } from './Orb'

export function LiveConversation({ access }: { access: AgentAccess }) {
  const agent = useAgentConversation(access)
  const [input, setInput] = useState('')
  const endOfChat = useRef<HTMLDivElement>(null)
  const busy = ['requesting_microphone', 'connecting', 'ending'].includes(agent.phase)
  const ended = agent.phase === 'processing' || agent.phase === 'completed'
  const latestQuestion = [...agent.messages]
    .reverse()
    .find((message) => message.role === 'assistant')?.text
  useEffect(() => {
    endOfChat.current?.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
  }, [agent.messages, agent.mode])

  async function submit() {
    if (agent.sendMessage(input)) setInput('')
  }

  return (
    <section className="conversation live-conversation" aria-label="Rozmowa z asystentem">
      {!ended && (
        <div className="conversation-top">
          <div className="mode-switch" role="group" aria-label="Forma rozmowy">
            <button
              className={agent.mode === 'voice' ? 'selected' : ''}
              aria-pressed={agent.mode === 'voice'}
              disabled={busy}
              onClick={() => void agent.changeMode('voice')}
            >
              <AudioLines size={17} />
              Rozmowa głosowa
            </button>
            <button
              className={agent.mode === 'text' ? 'selected' : ''}
              aria-pressed={agent.mode === 'text'}
              disabled={busy}
              onClick={() => void agent.changeMode('text')}
            >
              <MessageCircle size={17} />
              Czat
            </button>
          </div>
          {agent.mode === 'voice' && (
            <button
              className="icon-button sound-button"
              onClick={agent.toggleSound}
              aria-label={agent.sound ? 'Wyłącz dźwięk' : 'Włącz dźwięk'}
            >
              {agent.sound ? <Volume2 size={19} /> : <VolumeX size={19} />}
            </button>
          )}
        </div>
      )}
      {ended ? (
        <div className="agent-completed">
          <Orb small />
          <h2>
            {agent.phase === 'completed'
              ? 'Dziękuję za rozmowę.'
              : 'Przygotowuję Twoje podsumowanie…'}
          </h2>
          {agent.result?.summary ? (
            <div className="agent-result">
              <span className="small-icon">
                <Check size={18} />
              </span>
              <p>{agent.result.summary}</p>
            </div>
          ) : (
            <p>Możesz już zakończyć rozmowę. Wynik pojawi się po przetworzeniu wywiadu.</p>
          )}
          <button className="button secondary" onClick={() => void agent.refreshResult()}>
            Sprawdź podsumowanie
          </button>
        </div>
      ) : agent.mode === 'voice' ? (
        <div className="voice-experience live-voice">
          <Orb state={agent.voiceState} level={agent.level} />
          <div className="voice-copy" aria-live="polite">
            <h2>
              {agent.phase === 'requesting_microphone'
                ? 'Zezwól na dostęp do mikrofonu.'
                : agent.phase === 'connecting'
                  ? 'Łączę z asystentem…'
                  : agent.phase === 'ending'
                    ? 'Kończę rozmowę…'
                    : agent.phase !== 'connected'
                      ? 'Porozmawiajmy o Twoim zdrowiu.'
                      : agent.muted
                        ? 'Rozmowa wstrzymana.'
                        : agent.voiceState === 'speaking'
                          ? 'Posłuchajmy…'
                          : 'Słucham Cię…'}
            </h2>
            {latestQuestion && <p className="current-question">{latestQuestion}</p>}
          </div>
          <div className="voice-controls">
            <button
              className="microphone-button"
              disabled={busy}
              onClick={() =>
                agent.phase === 'connected' ? agent.toggleMute() : void agent.start('voice')
              }
              aria-label={
                agent.phase === 'connected'
                  ? agent.muted
                    ? 'Wznów mikrofon'
                    : 'Wycisz mikrofon'
                  : 'Rozpocznij rozmowę'
              }
            >
              {busy ? (
                <LoaderCircle className="spin" size={25} />
              ) : agent.muted ? (
                <MicOff size={28} />
              ) : (
                <Mic size={28} />
              )}
            </button>
            {agent.phase === 'connected' && (
              <button
                className="pause-button end-call"
                onClick={() => void agent.end()}
                aria-label="Zakończ rozmowę"
              >
                <PhoneOff size={20} />
              </button>
            )}
            <button
              className="text-button switch-to-text"
              disabled={busy}
              onClick={() => void agent.changeMode('text')}
            >
              Wolę pisać
            </button>
          </div>
        </div>
      ) : (
        <div className="chat-experience live-chat">
          <div
            className="chat-messages"
            role="log"
            aria-live="polite"
            aria-label="Historia rozmowy"
          >
            {agent.messages.map((message) => (
              <div key={message.id} className={`chat-message ${message.role}`}>
                <div>
                  <div className="chat-bubble">{message.text}</div>
                </div>
              </div>
            ))}
            <div ref={endOfChat} />
          </div>
          {agent.phase !== 'connected' ? (
            <button
              className="button primary chat-start"
              disabled={busy}
              onClick={() => void agent.start('text')}
            >
              {busy && <LoaderCircle size={16} className="spin" />}Rozpocznij czat
            </button>
          ) : (
            <>
              <form
                className="chat-composer"
                onSubmit={(event) => {
                  event.preventDefault()
                  void submit()
                }}
              >
                <textarea
                  aria-label="Twoja odpowiedź"
                  placeholder="Napisz odpowiedź…"
                  value={input}
                  onChange={(event) => setInput(event.target.value)}
                  rows={1}
                  maxLength={4000}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter' && !event.shiftKey) {
                      event.preventDefault()
                      void submit()
                    }
                  }}
                />
                <button
                  className="send-button"
                  disabled={!input.trim()}
                  aria-label="Wyślij odpowiedź"
                >
                  <ArrowUp size={20} />
                </button>
              </form>
              <button className="text-button muted end-chat" onClick={() => void agent.end()}>
                <PhoneOff size={14} />
                Zakończ rozmowę
              </button>
            </>
          )}
        </div>
      )}
      {agent.error && (
        <p className="agent-error" role="alert">
          {agent.error}
        </p>
      )}
    </section>
  )
}
