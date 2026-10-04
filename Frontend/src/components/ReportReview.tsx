import { useEffect, useRef, useState } from 'react'
import { Check, Download, MessageCircle, Mic, Pencil } from 'lucide-react'
import { agentApi, AgentApiError, type AgentAccess, type AgentResult } from '../lib/agent-api'
import {
  appendNotes,
  emptyFieldText,
  type PatientInterview,
  type ReportDraft,
} from '../lib/patient-report'
import { displayAgentText } from '../lib/agent-text'
import { DraftEditor, type DraftEditorTarget } from './DraftEditor'
import { ReportSupplement } from './ReportSupplement'

export function ReportReview({
  access,
  result,
  onResultRefresh,
}: {
  access: AgentAccess
  result: AgentResult
  onResultRefresh: () => Promise<void>
}) {
  const [view, setView] = useState<PatientInterview | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [editing, setEditing] = useState<DraftEditorTarget | null>(null)
  const [supplement, setSupplement] = useState<'text' | 'voice' | null>(null)
  const [approvedRevision, setApprovedRevision] = useState<number | null>(null)
  const [pdfFailed, setPdfFailed] = useState(false)
  const [answers, setAnswers] = useState<Record<string, string>>({})
  const lock = useRef(false)
  const alive = useRef(true)
  useEffect(() => {
    alive.current = true
    const controller = new AbortController()
    agentApi
      .getPatientInterview(access, controller.signal)
      .then((value) => {
        if (!controller.signal.aborted) setView(value)
      })
      .catch((cause) => {
        if (!controller.signal.aborted)
          setError(cause instanceof Error ? cause.message : 'Nie udało się pobrać raportu.')
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => {
      alive.current = false
      controller.abort()
    }
  }, [access])

  async function reload() {
    const value = await agentApi.getPatientInterview(access)
    if (alive.current) setView(value)
    return value
  }
  async function operation(action: () => Promise<void>, approvalFailureRevision?: number) {
    if (lock.current) return
    lock.current = true
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await action()
    } catch (cause) {
      if (!alive.current) return
      setError(cause instanceof Error ? cause.message : 'Nie udało się zapisać zmian.')
      if (cause instanceof AgentApiError && cause.code === 'report.generation_failed') {
        setPdfFailed(true)
        if (approvalFailureRevision != null) setApprovedRevision(approvalFailureRevision)
        await reload().catch(() => {})
      }
    } finally {
      lock.current = false
      if (alive.current) setBusy(false)
    }
  }
  async function saveDraft(draft: ReportDraft) {
    await operation(async () => {
      const value = await agentApi.saveDraft(access, draft)
      if (!alive.current) return
      setView(value)
      setEditing(null)
      setSupplement(null)
      setApprovedRevision(null)
      setNotice('Poprawki zapisane. Sprawdź raport i zatwierdź jego nową wersję.')
    })
  }
  async function approve() {
    if (!view) return
    await operation(async () => {
      const version = await agentApi.approveReport(access, view.draft.clarifications.length > 0)
      if (!alive.current) return
      setApprovedRevision(view.draft.revision)
      setPdfFailed(false)
      setView(
        (previous) =>
          previous && { ...previous, latestVersion: version.versionNumber, consentActive: true },
      )
      await reload()
      setNotice(`Zatwierdzono i udostępniono lekarzowi wersję ${version.versionNumber} raportu.`)
    }, view.draft.revision)
  }
  async function download() {
    await operation(async () => {
      const blob = await agentApi.downloadReport(access, 'pdf')
      if (!alive.current) return
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = `Przed-wizyta-v${view?.latestVersion ?? ''}.pdf`
      link.click()
      setTimeout(() => URL.revokeObjectURL(url), 1000)
    })
  }
  const draft = view?.draft
  const questions = view?.supplementationRound?.questions ?? []
  const missingAnswers = questions.some(
    (question) => !(answers[question.id] ?? question.answer ?? '').trim(),
  )
  const unsavedAnswers = questions.some(
    (question) =>
      question.id in answers && answers[question.id].trim() !== (question.answer ?? '').trim(),
  )
  const approved = draft && approvedRevision === draft.revision
  const incomplete = !!draft?.clarifications.length
  const inactive =
    view &&
    (['Expired', 'Cancelled'].includes(view.status) ||
      Date.parse(view.serviceExpiresAt) <= Date.now())
  const controlsDisabled = busy || loading || !!inactive
  const unstructured = result.extractionStatus === 'failed' || result.importStatus === 'failed'

  return (
    <div className="report-review">
      {result.summary && (
        <details className="report-summary-text">
          <summary>Opis rozmowy</summary>
          <p>{displayAgentText(result.summary)}</p>
        </details>
      )}
      {(unstructured || result.extractionStatus === 'partial') && (
        <div className="report-warning">
          <p>
            {unstructured
              ? 'Nie udało się w pełni przenieść rozmowy do raportu. Sprawdź zebrane dane lub uzupełnij je ręcznie.'
              : 'Wywiad zawiera niepełne dane. Sprawdź informacje wymagające uzupełnienia.'}
          </p>
          <button
            className="text-button"
            disabled={busy}
            onClick={() =>
              void operation(async () => {
                await agentApi.retryImport(access)
                await onResultRefresh()
                await reload()
                setNotice('Ponownie sprawdzono import danych.')
              })
            }
          >
            Ponów import rozmowy
          </button>
          {!!result.issues?.length && (
            <ul>
              {result.issues.map((issue, index) => (
                <li key={index}>{issue.message}</li>
              ))}
            </ul>
          )}
        </div>
      )}
      {loading && <p role="status">Pobieram podsumowanie…</p>}
      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}
      {notice && (
        <p className="agent-approval-note" role="status">
          {notice}
        </p>
      )}
      {!view && !loading && (
        <button
          className="button secondary"
          disabled={busy}
          onClick={() =>
            void operation(async () => {
              await reload()
            })
          }
        >
          Ponów pobranie raportu
        </button>
      )}
      {view && draft && (
        <>
          {inactive && (
            <p className="report-warning">Zakończył się czas na przygotowanie tej wizyty.</p>
          )}
          {editing ? (
            <DraftEditor
              initial={draft}
              target={editing}
              busy={controlsDisabled}
              onSave={saveDraft}
              onCancel={() => setEditing(null)}
            />
          ) : supplement ? (
            <ReportSupplement
              key={supplement}
              access={access}
              voice={supplement === 'voice'}
              busy={controlsDisabled}
              onSave={(text) => {
                const updated = appendNotes(draft, text)
                if ((updated.additionalNotes?.length ?? 0) > 8000) {
                  setError(
                    'Dodatkowe informacje mogą zawierać maksymalnie 8000 znaków. Skróć tekst w edycji raportu.',
                  )
                  return Promise.resolve()
                }
                return saveDraft(updated)
              }}
              onCancel={() => setSupplement(null)}
            />
          ) : (
            <>
              <div className="report-sections">
                <section>
                  <h3>Powód wizyty</h3>
                  <button
                    className="text-button"
                    disabled={controlsDisabled}
                    onClick={() => setEditing({ section: 'reason' })}
                  >
                    <Pencil size={15} /> Edytuj: Powód wizyty
                  </button>
                  <p>{draft.consultationReason || 'Nie zebrano informacji'}</p>
                </section>
                <section>
                  <h3>Objawy i ich przebieg</h3>
                  <button
                    className="text-button"
                    disabled={controlsDisabled}
                    onClick={() => setEditing({ section: 'symptoms' })}
                  >
                    <Pencil size={15} /> Edytuj: Objawy i ich przebieg
                  </button>
                  {draft.symptoms.length ? (
                    draft.symptoms.map((symptom, index) => (
                      <div className="report-entry" key={index}>
                        <h4>{symptom.name}</h4>
                        <button
                          className="text-button"
                          disabled={controlsDisabled}
                          onClick={() => setEditing({ section: 'symptoms', index })}
                        >
                          Edytuj objaw {symptom.name}
                        </button>
                        <p>
                          {[
                            symptom.startedOn && `Początek: ${symptom.startedOn}`,
                            symptom.frequency,
                            symptom.severity != null && `Nasilenie: ${symptom.severity}/10`,
                            symptom.description,
                            symptom.dailyImpact,
                          ]
                            .filter(Boolean)
                            .join(' · ')}
                        </p>
                        {!symptom.startedOn && (
                          <p className="muted">
                            Początek objawu: {emptyFieldText(symptom.startedOnState)}
                          </p>
                        )}
                        {!!symptom.timeline.length && (
                          <ul>
                            {symptom.timeline.map((entry, i) => (
                              <li key={i}>
                                {[entry.occurredOn, entry.period, entry.description]
                                  .filter(Boolean)
                                  .join(' · ')}
                              </li>
                            ))}
                          </ul>
                        )}
                      </div>
                    ))
                  ) : (
                    <p>Nie zebrano objawów</p>
                  )}
                </section>
                <section>
                  <h3>Leki</h3>
                  <button
                    className="text-button"
                    disabled={controlsDisabled}
                    onClick={() => setEditing({ section: 'medications' })}
                  >
                    <Pencil size={15} /> Edytuj: Leki
                  </button>
                  {draft.medications.length ? (
                    draft.medications.map((medication, index) => (
                      <div className="report-entry" key={index}>
                        <h4>{medication.name}</h4>
                        <button
                          className="text-button"
                          disabled={controlsDisabled}
                          onClick={() => setEditing({ section: 'medications', index })}
                        >
                          Edytuj lek {medication.name}
                        </button>
                        <p>
                          {[
                            medication.dose
                              ? `Dawka: ${medication.dose}`
                              : `Dawka: ${emptyFieldText(medication.doseState)}`,
                            medication.schedule,
                            medication.reason && `Powód: ${medication.reason}`,
                          ]
                            .filter(Boolean)
                            .join(' · ')}
                        </p>
                      </div>
                    ))
                  ) : (
                    <p>{emptyFieldText(draft.medicationsState)}</p>
                  )}
                </section>
                <section>
                  <h3>Alergie</h3>
                  <button
                    className="text-button"
                    disabled={controlsDisabled}
                    onClick={() => setEditing({ section: 'allergies' })}
                  >
                    <Pencil size={15} /> Edytuj: Alergie
                  </button>
                  {draft.allergies.length ? (
                    <ul>
                      {draft.allergies.map((item, i) => (
                        <li key={i}>
                          {[item.substance, item.reaction].filter(Boolean).join(' · ')}
                          <button
                            className="text-button"
                            disabled={controlsDisabled}
                            onClick={() => setEditing({ section: 'allergies', index: i })}
                          >
                            Edytuj alergen {item.substance}
                          </button>
                        </li>
                      ))}
                    </ul>
                  ) : (
                    <p>{emptyFieldText(draft.allergiesState)}</p>
                  )}
                </section>
                <section>
                  <h3>Choroby przewlekłe</h3>
                  <button
                    className="text-button"
                    disabled={controlsDisabled}
                    onClick={() => setEditing({ section: 'chronicConditions' })}
                  >
                    <Pencil size={15} /> Edytuj: Choroby przewlekłe
                  </button>
                  {draft.chronicConditions.length ? (
                    <ul>
                      {draft.chronicConditions.map((item, i) => (
                        <li key={i}>
                          {[item.name, item.description].filter(Boolean).join(' · ')}
                          <button
                            className="text-button"
                            disabled={controlsDisabled}
                            onClick={() => setEditing({ section: 'chronicConditions', index: i })}
                          >
                            Edytuj chorobę {item.name}
                          </button>
                        </li>
                      ))}
                    </ul>
                  ) : (
                    <p>{emptyFieldText(draft.chronicConditionsState)}</p>
                  )}
                </section>
                <section>
                  <h3>Pytania do lekarza</h3>
                  <button
                    className="text-button"
                    disabled={controlsDisabled}
                    onClick={() => setEditing({ section: 'questions' })}
                  >
                    <Pencil size={15} /> Edytuj: Pytania do lekarza
                  </button>
                  {draft.questions.length ? (
                    <ul>
                      {draft.questions.map((question, i) => (
                        <li key={i}>{question}</li>
                      ))}
                    </ul>
                  ) : (
                    <p>Nie dodano pytań</p>
                  )}
                </section>
                <section>
                  <h3>Dodatkowe informacje</h3>
                  <button
                    className="text-button"
                    disabled={controlsDisabled}
                    onClick={() => setEditing({ section: 'notes' })}
                  >
                    <Pencil size={15} /> Edytuj: Dodatkowe informacje
                  </button>
                  <p>{draft.additionalNotes || 'Nie dodano informacji'}</p>
                </section>
              </div>
              {incomplete && (
                <div className="report-warning">
                  <h3>Informacje wymagające uzupełnienia</h3>
                  <ul>
                    {draft.clarifications.map((issue, i) => (
                      <li key={i}>{issue.message}</li>
                    ))}
                  </ul>
                </div>
              )}
              {view.observations.some((item) => item.decision !== 'Rejected') && (
                <section className="report-observations">
                  <h3>Obserwacje</h3>
                  <p>Obserwacje są częścią raportu, który zatwierdzasz w całości.</p>
                  {view.observations
                    .filter((item) => item.decision !== 'Rejected')
                    .map((observation) => (
                      <div className="report-entry" key={observation.id}>
                        <p>{observation.text}</p>
                      </div>
                    ))}
                </section>
              )}
              {!!questions.length && (
                <form
                  className="report-supplement"
                  onSubmit={(event) => {
                    event.preventDefault()
                    void operation(async () => {
                      await agentApi.answerSupplementation(
                        access,
                        questions.map((question) => ({
                          questionId: question.id,
                          answer: (answers[question.id] ?? question.answer ?? '').trim(),
                          mode: 'Text',
                        })),
                      )
                      await reload()
                      setApprovedRevision(null)
                      setNotice('Odpowiedzi dla lekarza zapisane. Możesz zatwierdzić raport.')
                    })
                  }}
                >
                  <h3>Pytania od lekarza</h3>
                  {questions.map((question) => (
                    <label className="report-field" key={question.id}>
                      <span>{question.text}</span>
                      <textarea
                        required
                        disabled={controlsDisabled}
                        value={answers[question.id] ?? question.answer ?? ''}
                        onChange={(event) =>
                          setAnswers((previous) => ({
                            ...previous,
                            [question.id]: event.target.value,
                          }))
                        }
                        maxLength={8000}
                      />
                    </label>
                  ))}
                  <button
                    className="button secondary"
                    disabled={controlsDisabled || missingAnswers}
                  >
                    Zapisz odpowiedzi dla lekarza
                  </button>
                </form>
              )}
              <p>
                Jednym zatwierdzeniem akceptujesz cały raport, w tym widoczne obserwacje i braki,
                oraz przekazujesz go lekarzowi przypisanemu do wizyty.
              </p>
              <div className="agent-summary-actions">
                <button
                  className="button primary"
                  disabled={
                    controlsDisabled ||
                    !draft.consultationReason?.trim() ||
                    unsavedAnswers ||
                    questions.some((question) => !question.answer?.trim()) ||
                    !!approved
                  }
                  onClick={() => void approve()}
                >
                  <Check size={17} />
                  {approved ? 'Raport zatwierdzony' : 'Zatwierdź i udostępnij raport'}
                </button>
                <div className="agent-supplement-actions">
                  <button
                    className="button secondary"
                    disabled={controlsDisabled}
                    onClick={() => setSupplement('voice')}
                  >
                    <Mic size={17} />
                    Dopowiedz głosowo
                  </button>
                  <button
                    className="button secondary"
                    disabled={controlsDisabled}
                    onClick={() => setSupplement('text')}
                  >
                    <MessageCircle size={17} />
                    Dopowiedz na czacie
                  </button>
                </div>
                <button
                  className="text-button"
                  disabled={controlsDisabled}
                  onClick={() => setEditing({ section: 'all' })}
                >
                  <Pencil size={16} />
                  Edytuj podsumowanie
                </button>
              </div>
            </>
          )}
          {view.latestVersion != null && (
            <section className="report-published">
              <h3>Zapisana wersja raportu: {view.latestVersion}</h3>
              <p>
                {view.consentActive
                  ? 'Zapisana wersja jest udostępniona placówce. Poprawki wymagają zatwierdzenia nowej wersji.'
                  : pdfFailed
                    ? 'Treść zatwierdzona. Dokończ przygotowanie PDF i udostępnienie raportu.'
                    : 'Raport nie jest obecnie udostępniony. Zatwierdzenie ponownie przekazuje go lekarzowi.'}
              </p>
              <div className="report-actions">
                <button
                  className="button secondary"
                  disabled={busy || pdfFailed}
                  onClick={() => void download()}
                >
                  <Download size={16} />
                  Pobierz PDF
                </button>
                {view.consentActive && (
                  <button
                    className="button secondary"
                    disabled={busy}
                    onClick={() =>
                      void operation(async () => {
                        await agentApi.setConsent(access, false)
                        await reload()
                        setApprovedRevision(null)
                        setNotice('Zgoda cofnięta. Placówka nie ma już dostępu do raportu.')
                      })
                    }
                  >
                    Cofnij zgodę na udostępnienie
                  </button>
                )}
              </div>
              {pdfFailed && (
                <button
                  className="button secondary"
                  disabled={
                    controlsDisabled || !!editing || !!supplement || unsavedAnswers || !approved
                  }
                  onClick={() =>
                    void operation(async () => {
                      await agentApi.regeneratePdf(access)
                      await agentApi.setConsent(access, true)
                      await reload()
                      setPdfFailed(false)
                      setNotice('Raport PDF jest gotowy i udostępniony lekarzowi.')
                    })
                  }
                >
                  Ponów przygotowanie i udostępnienie raportu
                </button>
              )}
            </section>
          )}
          <button
            className="text-button muted"
            disabled={busy}
            onClick={() =>
              void operation(async () => {
                await reload()
                await onResultRefresh()
              })
            }
          >
            Odśwież podsumowanie
          </button>
        </>
      )}
    </div>
  )
}
