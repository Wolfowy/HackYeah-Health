import { useEffect, useRef, useState, type FormEvent } from 'react'
import {
  ArrowRight,
  ArrowUp,
  AudioLines,
  Check,
  ChevronRight,
  CircleHelp,
  CornerDownLeft,
  Keyboard,
  MessageCircle,
  Mic,
  Pause,
  Play,
  RotateCcw,
  Sparkles,
  Volume2,
  VolumeX,
} from 'lucide-react'
import { interviewQuestions } from '../data/mock'
import type { ConversationMode, Interview, VoiceState } from '../models'
import { Orb } from './Orb'

export function Conversation({
  interview,
  onAnswer,
  onSummary,
  onReset,
}: {
  interview: Interview
  onAnswer: (text: string, mode: ConversationMode) => void
  onSummary: () => void
  onReset: () => void
}) {
  const [mode, setMode] = useState<ConversationMode>('voice')
  const [voiceState, setVoiceState] = useState<VoiceState>('idle')
  const [input, setInput] = useState('')
  const [transcript, setTranscript] = useState('')
  const [sound, setSound] = useState(false)
  const [help, setHelp] = useState(false)
  const chatEnd = useRef<HTMLDivElement>(null)
  const voiceTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const voiceRun = useRef(0)
  const question = interviewQuestions[interview.questionIndex]
  const finished = !question
  const stage = question?.stage ?? 3
  const steps = ['Powód wizyty', 'Objawy', 'Twoje zdrowie', 'Podsumowanie']

  useEffect(() => {
    chatEnd.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
  }, [interview.messages.length, mode])

  useEffect(
    () => () => {
      voiceRun.current += 1
      if (voiceTimer.current) clearTimeout(voiceTimer.current)
      if ('speechSynthesis' in window) window.speechSynthesis.cancel()
    },
    [],
  )

  function speak(text: string) {
    const run = ++voiceRun.current
    if (voiceTimer.current) clearTimeout(voiceTimer.current)
    setVoiceState('speaking')
    const finishSpeaking = () => {
      if (voiceRun.current !== run) return
      if (voiceTimer.current) clearTimeout(voiceTimer.current)
      setVoiceState('listening')
    }
    voiceTimer.current = setTimeout(finishSpeaking, sound ? Math.max(2500, text.length * 80) : 1800)
    if (sound && 'speechSynthesis' in window) {
      window.speechSynthesis.cancel()
      const utterance = new SpeechSynthesisUtterance(text)
      utterance.lang = 'pl-PL'
      utterance.rate = 0.95
      utterance.onend = finishSpeaking
      utterance.onerror = finishSpeaking
      window.speechSynthesis.speak(utterance)
    }
  }

  function changeMode(next: ConversationMode) {
    voiceRun.current += 1
    if (voiceTimer.current) clearTimeout(voiceTimer.current)
    if ('speechSynthesis' in window) window.speechSynthesis.cancel()
    setVoiceState('idle')
    setMode(next)
    if (next === 'text' && transcript) {
      setInput(transcript)
      setTranscript('')
    }
  }

  function submit(event?: FormEvent, example?: string) {
    event?.preventDefault()
    const text = (example ?? (mode === 'voice' ? transcript : input)).trim()
    if (!text || finished) return
    onAnswer(text, mode)
    setInput('')
    setTranscript('')
    if (mode === 'voice' && interviewQuestions[interview.questionIndex + 1])
      speak(interviewQuestions[interview.questionIndex + 1].text)
    else {
      voiceRun.current += 1
      if (voiceTimer.current) clearTimeout(voiceTimer.current)
      if ('speechSynthesis' in window) window.speechSynthesis.cancel()
      setVoiceState('idle')
    }
  }

  function pause() {
    voiceRun.current += 1
    if (voiceTimer.current) clearTimeout(voiceTimer.current)
    if ('speechSynthesis' in window) window.speechSynthesis.cancel()
    setVoiceState(voiceState === 'paused' ? 'listening' : 'paused')
  }

  return (
    <section className="conversation card" aria-label="Rozmowa z asystentem">
      <div className="conversation-top">
        <div className="mode-switch" role="group" aria-label="Forma rozmowy">
          <button
            className={mode === 'voice' ? 'selected' : ''}
            onClick={() => changeMode('voice')}
            aria-pressed={mode === 'voice'}
          >
            <AudioLines size={17} />
            <span>Rozmowa głosowa</span>
          </button>
          <button
            className={mode === 'text' ? 'selected' : ''}
            onClick={() => changeMode('text')}
            aria-pressed={mode === 'text'}
          >
            <MessageCircle size={17} />
            Czat
          </button>
        </div>
        <button
          className={`icon-button sound-button ${sound ? 'is-active' : ''}`}
          onClick={() => setSound(!sound)}
          aria-label={sound ? 'Wyłącz odczytywanie pytań' : 'Włącz odczytywanie pytań'}
          title="Odczytywanie pytań"
        >
          {sound ? <Volume2 size={19} /> : <VolumeX size={19} />}
        </button>
      </div>
      <div className="interview-steps" aria-label="Etapy wywiadu">
        {steps.map((step, index) => (
          <div
            className={`interview-step ${index === stage ? 'current' : ''} ${index < stage ? 'completed' : ''}`}
            key={step}
            aria-current={index === stage ? 'step' : undefined}
          >
            <span className="step-number">{index < stage ? <Check size={13} /> : index + 1}</span>
            <span className="step-label">{step}</span>
            {index < 3 && <ChevronRight size={12} className="step-chevron" />}
          </div>
        ))}
      </div>
      <div className="mobile-progress">
        <span
          style={{
            width: `${finished ? 100 : Math.max(8, (interview.questionIndex / interviewQuestions.length) * 100)}%`,
          }}
        />
      </div>

      {finished ? (
        <div className="conversation-finished">
          <Orb small />
          <span className="pill success">
            <Check size={14} />
            Wywiad zakończony
          </span>
          <h2>Dziękuję za Twoją historię.</h2>
          <p>
            Wszystkie odpowiedzi są w podsumowaniu.
            <br />
            Sprawdź je spokojnie, zanim przekażesz je lekarzowi.
          </p>
          <button className="button primary" onClick={onSummary}>
            Sprawdź podsumowanie <ArrowRight size={17} />
          </button>
          <button className="text-button muted" onClick={onReset}>
            <RotateCcw size={14} />
            Rozpocznij wywiad od nowa
          </button>
        </div>
      ) : mode === 'voice' ? (
        <div className="voice-experience">
          <div className="voice-status">
            <span className={`status-dot ${voiceState !== 'paused' ? 'live' : ''}`} />
            {voiceState === 'idle'
              ? 'Twój asystent jest gotowy'
              : voiceState === 'speaking'
                ? 'Asystent zadaje pytanie'
                : voiceState === 'paused'
                  ? 'Rozmowa wstrzymana'
                  : 'Teraz Twoja kolej'}
          </div>
          <Orb state={voiceState} />
          <div className="voice-copy" aria-live="polite">
            <h2>
              {voiceState === 'idle'
                ? 'Jestem tu, żeby Cię wysłuchać.'
                : voiceState === 'speaking'
                  ? 'Porozmawiajmy spokojnie.'
                  : voiceState === 'paused'
                    ? 'Nie spiesz się.'
                    : 'Słucham Cię…'}
            </h2>
            <p className="current-question">{question.text}</p>
            <p className="voice-hint">
              {voiceState === 'paused'
                ? 'Wróć do rozmowy, kiedy będziesz gotowy.'
                : 'Nie ma złych odpowiedzi. Możesz zrobić przerwę.'}
            </p>
          </div>
          <div className="voice-controls">
            <button
              className={`microphone-button ${voiceState === 'listening' ? 'listening' : ''}`}
              onClick={() =>
                voiceState === 'idle' || voiceState === 'paused' ? speak(question.text) : pause()
              }
              aria-label={
                voiceState === 'idle'
                  ? 'Rozpocznij rozmowę demonstracyjną'
                  : voiceState === 'paused'
                    ? 'Wznów rozmowę'
                    : 'Wstrzymaj rozmowę'
              }
            >
              {voiceState === 'paused' ? (
                <Play size={27} fill="currentColor" />
              ) : (
                <Mic size={30} strokeWidth={1.7} />
              )}
            </button>
            <button
              className="pause-button"
              onClick={pause}
              disabled={voiceState === 'idle'}
              aria-label={voiceState === 'paused' ? 'Wznów rozmowę' : 'Pauza'}
            >
              {voiceState === 'paused' ? (
                <Play size={20} />
              ) : (
                <Pause size={20} fill="currentColor" />
              )}
            </button>
            <button className="text-button switch-to-text" onClick={() => changeMode('text')}>
              Wolę pisać <ArrowRight size={16} />
            </button>
          </div>
          <span className="mic-caption">
            {voiceState === 'idle' ? 'Kliknij, aby rozpocząć' : 'Symulacja rozmowy głosowej'}
          </span>
          {voiceState !== 'idle' && voiceState !== 'paused' && (
            <div className="transcript-box">
              <div className="transcript-label">
                <Keyboard size={14} />
                <span>Twoja odpowiedź w demo</span>
                <span className="mini-tag">Możesz ją edytować</span>
              </div>
              <form onSubmit={(event) => submit(event)}>
                <textarea
                  aria-label="Odpowiedź głosowa w demo"
                  placeholder="Wpisz odpowiedź lub wybierz przykład poniżej…"
                  value={transcript}
                  onChange={(event) => setTranscript(event.target.value)}
                  maxLength={4000}
                  rows={2}
                />
                <button
                  className="icon-button send-button"
                  disabled={!transcript.trim()}
                  aria-label="Zatwierdź odpowiedź"
                >
                  <ArrowUp size={20} />
                </button>
              </form>
              <div className="voice-examples">
                {question.examples.map((example) => (
                  <button key={example} onClick={() => setTranscript(example)}>
                    {example}
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>
      ) : (
        <div className="chat-experience">
          <div
            className="chat-messages"
            role="log"
            aria-label="Historia rozmowy"
            aria-live="polite"
          >
            <div className="chat-greeting">
              <Sparkles size={15} />
              Przestrzeń na Twoją historię. Opowiedz tyle, ile chcesz.
            </div>
            {interview.messages.map((message) => (
              <div className={`chat-message ${message.role}`} key={message.id}>
                {message.role === 'assistant' && (
                  <span className="assistant-avatar">
                    <span className="brand-ring" />
                  </span>
                )}
                <div>
                  <div className="chat-bubble">{message.text}</div>
                  <span className="message-time">
                    {message.role === 'assistant' ? 'Asystent' : 'Ty'} ·{' '}
                    {new Date(message.createdAt).toLocaleTimeString('pl-PL', {
                      hour: '2-digit',
                      minute: '2-digit',
                    })}
                  </span>
                </div>
              </div>
            ))}
            <div ref={chatEnd} />
          </div>
          <div className="chat-bottom">
            <div className="quick-replies">
              {question.examples.map((example) => (
                <button key={example} onClick={() => submit(undefined, example)}>
                  {example}
                </button>
              ))}
            </div>
            <form className="chat-composer" onSubmit={(event) => submit(event)}>
              <textarea
                aria-label="Twoja odpowiedź"
                placeholder="Napisz odpowiedź…"
                rows={1}
                value={input}
                maxLength={4000}
                onChange={(event) => setInput(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter' && !event.shiftKey) {
                    event.preventDefault()
                    submit()
                  }
                }}
              />
              <button
                type="button"
                className="icon-button"
                aria-label="Przejdź do rozmowy głosowej"
                onClick={() => changeMode('voice')}
              >
                <Mic size={20} />
              </button>
              <button
                className="send-button"
                disabled={!input.trim()}
                aria-label="Wyślij odpowiedź"
              >
                <ArrowUp size={20} />
              </button>
            </form>
            <div className="composer-hint">
              <span>{question.hint}</span>
              <span>
                <CornerDownLeft size={11} />
                wyślij
              </span>
            </div>
          </div>
        </div>
      )}
      <div className="conversation-footer">
        <span>
          <span className="demo-indicator" />
          Demo · odpowiedzi AI są symulowane
        </span>
        <button onClick={() => setHelp(!help)} className="text-button muted">
          <CircleHelp size={14} />
          Jak to działa?
        </button>
      </div>
      {help && (
        <div className="inline-help">
          Asystent zbiera informacje do wywiadu. W tym demo pytania są zaprogramowane, a mikrofon
          nie nagrywa. Opcjonalnie możesz włączyć odczytywanie pytań. Odpowiedzi wpisujesz lub
          wybierasz z przykładów i zawsze możesz je poprawić w podsumowaniu.
        </div>
      )}
    </section>
  )
}
