import { useEffect, useRef, useState } from 'react'
import { Mic, Square } from 'lucide-react'
import { agentApi, type AgentAccess } from '../lib/agent-api'

/** Audio is temporary; only reviewed text is written to the draft. */
export function ReportSupplement({
  access,
  voice,
  busy,
  onSave,
  onCancel,
}: {
  access: AgentAccess
  voice: boolean
  busy: boolean
  onSave: (text: string) => Promise<void>
  onCancel: () => void
}) {
  const [text, setText] = useState('')
  const [state, setState] = useState<'idle' | 'permission' | 'recording' | 'transcribing'>('idle')
  const [error, setError] = useState('')
  const run = useRef(0)
  const recorder = useRef<MediaRecorder | null>(null)
  const stream = useRef<MediaStream | null>(null)
  const abort = useRef<AbortController | null>(null)
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  useEffect(
    () => () => {
      run.current++
      abort.current?.abort()
      if (timer.current) clearTimeout(timer.current)
      if (recorder.current?.state === 'recording') recorder.current.stop()
      stream.current?.getTracks().forEach((track) => track.stop())
    },
    [],
  )

  async function record() {
    if (state !== 'idle') return
    const current = ++run.current
    setError('')
    setState('permission')
    try {
      if (!navigator.mediaDevices?.getUserMedia || !window.MediaRecorder)
        throw new Error(
          'Nagrywanie nie jest dostępne w tej przeglądarce. Możesz dopowiedzieć tekstowo.',
        )
      const audio = await navigator.mediaDevices.getUserMedia({ audio: true })
      if (run.current !== current) {
        audio.getTracks().forEach((track) => track.stop())
        return
      }
      stream.current = audio
      const mimeType = ['audio/webm', 'audio/mp4', 'audio/ogg'].find((type) =>
        MediaRecorder.isTypeSupported(type),
      )
      if (!mimeType)
        throw new Error(
          'Ta przeglądarka nie obsługuje formatu nagrania. Możesz dopowiedzieć tekstowo.',
        )
      const instance = new MediaRecorder(audio, { mimeType })
      recorder.current = instance
      const chunks: Blob[] = []
      let size = 0
      let failed = false
      instance.ondataavailable = (event) => {
        if (run.current !== current) return
        size += event.data.size
        if (size > 15_000_000) {
          failed = true
          setError('Nagranie jest za duże. Nagraj krótsze dopowiedzenie.')
          if (instance.state === 'recording') instance.stop()
        } else chunks.push(event.data)
      }
      instance.onerror = () => {
        failed = true
        setError('Nie udało się nagrać dopowiedzenia. Spróbuj ponownie lub napisz tekstowo.')
        audio.getTracks().forEach((track) => track.stop())
        setState('idle')
      }
      instance.onstop = async () => {
        audio.getTracks().forEach((track) => track.stop())
        if (timer.current) clearTimeout(timer.current)
        if (run.current !== current) return
        if (failed) {
          setState('idle')
          return
        }
        setState('transcribing')
        const controller = new AbortController()
        abort.current = controller
        try {
          const blob = new Blob(chunks, { type: instance.mimeType.split(';')[0] })
          if (!blob.size) throw new Error('Nagranie jest puste. Spróbuj ponownie.')
          const value = await agentApi.transcribe(access, blob, controller.signal)
          if (run.current === current)
            setText((previous) => [previous, value].filter(Boolean).join('\n'))
        } catch (cause) {
          if (run.current === current)
            setError(cause instanceof Error ? cause.message : 'Nie udało się rozpoznać nagrania.')
        } finally {
          if (run.current === current) setState('idle')
        }
      }
      instance.start(1000)
      setState('recording')
      timer.current = setTimeout(() => {
        if (instance.state === 'recording') instance.stop()
      }, 120000)
    } catch (cause) {
      stream.current?.getTracks().forEach((track) => track.stop())
      if (run.current !== current) return
      setState('idle')
      setError(
        cause instanceof DOMException && cause.name === 'NotAllowedError'
          ? 'Nie udzielono dostępu do mikrofonu. Możesz dopowiedzieć tekstowo.'
          : cause instanceof Error
            ? cause.message
            : 'Nie udało się uruchomić mikrofonu.',
      )
    }
  }
  return (
    <form
      className="report-supplement"
      onSubmit={(event) => {
        event.preventDefault()
        void onSave(text)
      }}
    >
      <h3>{voice ? 'Dopowiedz głosowo' : 'Dopowiedz na czacie'}</h3>
      {voice && (
        <div className="report-actions">
          <button
            type="button"
            className="button secondary"
            disabled={busy || state === 'permission' || state === 'transcribing'}
            onClick={() => (state === 'recording' ? recorder.current?.stop() : void record())}
          >
            {state === 'recording' ? <Square size={16} /> : <Mic size={17} />}
            {state === 'recording'
              ? 'Zatrzymaj nagranie'
              : state === 'transcribing'
                ? 'Rozpoznaję nagranie…'
                : state === 'permission'
                  ? 'Otwieram mikrofon…'
                  : 'Nagraj dopowiedzenie'}
          </button>
          <span className="muted">Maksymalnie 2 minuty. Sprawdź tekst przed zapisem.</span>
        </div>
      )}
      <label className="report-field">
        <span>Twoje dopowiedzenie</span>
        <textarea
          value={text}
          onChange={(event) => setText(event.target.value)}
          maxLength={8000}
          disabled={busy || state !== 'idle'}
          required
          placeholder="Co chcesz dodać do podsumowania?"
        />
      </label>
      {error && (
        <p role="alert" className="form-error">
          {error}
        </p>
      )}
      <div className="report-actions">
        <button className="button primary" disabled={busy || state !== 'idle' || !text.trim()}>
          Zapisz dopowiedzenie
        </button>
        <button type="button" className="button secondary" disabled={busy} onClick={onCancel}>
          Wróć do podsumowania
        </button>
      </div>
    </form>
  )
}
