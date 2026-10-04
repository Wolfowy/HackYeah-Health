import { useEffect, useRef, useState, type FormEvent } from 'react'
import type { ReportDraft, ReportFieldState } from '../lib/patient-report'

export function Field({
  label,
  value,
  onChange,
  type = 'text',
  maxLength = 2000,
  required = false,
}: {
  label: string
  value: string | null
  onChange: (value: string) => void
  type?: string
  maxLength?: number
  required?: boolean
}) {
  return (
    <label className="report-field">
      <span>{label}</span>
      <input
        type={type}
        value={value ?? ''}
        onChange={(event) => onChange(event.target.value)}
        maxLength={maxLength}
        required={required}
      />
    </label>
  )
}
function StateField({
  label,
  value,
  onChange,
}: {
  label: string
  value: ReportFieldState
  onChange: (value: ReportFieldState) => void
}) {
  return (
    <label className="report-field">
      <span>{label}</span>
      <select value={value} onChange={(event) => onChange(event.target.value as ReportFieldState)}>
        <option value="Provided">Podano informacje / potwierdzono brak</option>
        <option value="Unknown">Nie wiem</option>
        <option value="NotAsked">Nie zebrano informacji</option>
        <option value="Contradictory">Wymaga wyjaśnienia</option>
      </select>
    </label>
  )
}

export type DraftEditorTarget = {
  section:
    | 'all'
    | 'reason'
    | 'symptoms'
    | 'medications'
    | 'allergies'
    | 'chronicConditions'
    | 'questions'
    | 'notes'
  index?: number
}

export function DraftEditor({
  initial,
  target = { section: 'all' },
  busy,
  onSave,
  onCancel,
}: {
  initial: ReportDraft
  target?: DraftEditorTarget
  busy: boolean
  onSave: (draft: ReportDraft) => Promise<void>
  onCancel: () => void
}) {
  const [draft, setDraft] = useState(() => structuredClone(initial))
  const form = useRef<HTMLFormElement>(null)
  useEffect(() => {
    form.current
      ?.querySelector<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>(
        'input, textarea, select',
      )
      ?.focus()
  }, [])
  const shows = (section: DraftEditorTarget['section']) =>
    target.section === 'all' || target.section === section
  const titles = {
    all: 'Edytuj podsumowanie',
    reason: 'Edytuj powód wizyty',
    symptoms: 'Edytuj objawy',
    medications: 'Edytuj leki',
    allergies: 'Edytuj alergie',
    chronicConditions: 'Edytuj choroby przewlekłe',
    questions: 'Edytuj pytania do lekarza',
    notes: 'Edytuj dodatkowe informacje',
  }
  function set<K extends keyof ReportDraft>(key: K, value: ReportDraft[K]) {
    setDraft((previous) => ({ ...previous, [key]: value }))
  }
  function submit(event: FormEvent) {
    event.preventDefault()
    void onSave(
      shows('questions')
        ? { ...draft, questions: draft.questions.map((text) => text.trim()).filter(Boolean) }
        : draft,
    )
  }
  return (
    <form ref={form} className="report-editor" onSubmit={submit}>
      <h3>{titles[target.section]}</h3>
      <fieldset disabled={busy}>
        {shows('reason') && (
          <>
            <Field
              label="Powód wizyty"
              value={draft.consultationReason}
              required
              onChange={(value) => set('consultationReason', value)}
            />
          </>
        )}
        {shows('symptoms') && (
          <>
            <h4>Objawy</h4>
            {draft.symptoms.map((symptom, index) => {
              const update = (patch: Partial<typeof symptom>) =>
                set(
                  'symptoms',
                  draft.symptoms.map((item, i) => (i === index ? { ...item, ...patch } : item)),
                )
              if (target.index != null && target.index !== index) return null
              return (
                <div className="report-editor-item" key={index}>
                  <Field
                    label={`Objaw ${index + 1}`}
                    value={symptom.name}
                    maxLength={200}
                    required
                    onChange={(name) => update({ name })}
                  />
                  <div className="report-field-grid">
                    <Field
                      label="Data początku"
                      type="date"
                      value={symptom.startedOn}
                      onChange={(startedOn) =>
                        update({
                          startedOn: startedOn || null,
                          startedOnState: startedOn ? 'Provided' : 'Unknown',
                        })
                      }
                    />
                    <StateField
                      label="Wiedza o początku objawu"
                      value={symptom.startedOnState}
                      onChange={(startedOnState) => update({ startedOnState })}
                    />
                    <Field
                      label="Częstotliwość"
                      value={symptom.frequency}
                      onChange={(frequency) => update({ frequency })}
                    />
                    <label className="report-field">
                      <span>Nasilenie (0–10)</span>
                      <input
                        type="number"
                        min="0"
                        max="10"
                        value={symptom.severity ?? ''}
                        onChange={(event) =>
                          update({
                            severity: event.target.value === '' ? null : Number(event.target.value),
                          })
                        }
                      />
                    </label>
                  </div>
                  <Field
                    label="Wpływ na codzienne życie"
                    value={symptom.dailyImpact}
                    onChange={(dailyImpact) => update({ dailyImpact })}
                  />
                  <Field
                    label="Opis objawu"
                    value={symptom.description}
                    onChange={(description) => update({ description })}
                  />
                  <details>
                    <summary>Przebieg w czasie</summary>
                    {symptom.timeline.map((entry, entryIndex) => {
                      const change = (patch: Partial<typeof entry>) =>
                        update({
                          timeline: symptom.timeline.map((item, i) =>
                            i === entryIndex ? { ...item, ...patch } : item,
                          ),
                        })
                      return (
                        <div className="report-editor-item" key={entryIndex}>
                          <Field
                            label="Data zdarzenia"
                            type="date"
                            value={entry.occurredOn}
                            onChange={(occurredOn) => change({ occurredOn: occurredOn || null })}
                          />
                          <Field
                            label="Okres"
                            value={entry.period}
                            onChange={(period) => change({ period })}
                          />
                          <Field
                            label="Opis zdarzenia"
                            value={entry.description}
                            required
                            onChange={(description) => change({ description })}
                          />
                          <button
                            type="button"
                            className="text-button muted"
                            onClick={() =>
                              update({
                                timeline: symptom.timeline.filter((_, i) => i !== entryIndex),
                              })
                            }
                          >
                            Usuń zdarzenie
                          </button>
                        </div>
                      )
                    })}
                    <button
                      type="button"
                      className="text-button"
                      onClick={() =>
                        update({
                          timeline: [
                            ...symptom.timeline,
                            { occurredOn: null, period: null, description: '' },
                          ],
                        })
                      }
                    >
                      Dodaj zdarzenie
                    </button>
                  </details>
                  <button
                    type="button"
                    className="text-button muted"
                    onClick={() =>
                      set(
                        'symptoms',
                        draft.symptoms.filter((_, i) => i !== index),
                      )
                    }
                  >
                    Usuń objaw
                  </button>
                </div>
              )
            })}
            {target.index == null && (
              <button
                type="button"
                className="text-button"
                onClick={() =>
                  set('symptoms', [
                    ...draft.symptoms,
                    {
                      name: '',
                      startedOn: null,
                      startedOnState: 'Unknown',
                      frequency: null,
                      severity: null,
                      dailyImpact: null,
                      description: null,
                      timeline: [],
                    },
                  ])
                }
              >
                Dodaj objaw
              </button>
            )}
          </>
        )}
        {shows('medications') && (
          <>
            <h4>Leki</h4>
            {target.index == null && (
              <StateField
                label="Informacje o lekach"
                value={draft.medicationsState}
                onChange={(value) => set('medicationsState', value)}
              />
            )}
            {draft.medications.map((medication, index) => {
              const update = (patch: Partial<typeof medication>) =>
                set(
                  'medications',
                  draft.medications.map((item, i) => (i === index ? { ...item, ...patch } : item)),
                )
              if (target.index != null && target.index !== index) return null
              return (
                <div className="report-editor-item" key={index}>
                  <Field
                    label={`Lek ${index + 1}`}
                    value={medication.name}
                    maxLength={200}
                    required
                    onChange={(name) => update({ name })}
                  />
                  <Field
                    label="Dawka"
                    value={medication.dose}
                    onChange={(dose) =>
                      update({ dose: dose || null, doseState: dose ? 'Provided' : 'Unknown' })
                    }
                  />
                  <StateField
                    label="Wiedza o dawce"
                    value={medication.doseState}
                    onChange={(doseState) => update({ doseState })}
                  />
                  <Field
                    label="Sposób przyjmowania"
                    value={medication.schedule}
                    onChange={(schedule) => update({ schedule })}
                  />
                  <Field
                    label="Powód przyjmowania"
                    value={medication.reason}
                    onChange={(reason) => update({ reason })}
                  />
                  <button
                    type="button"
                    className="text-button muted"
                    onClick={() =>
                      set(
                        'medications',
                        draft.medications.filter((_, i) => i !== index),
                      )
                    }
                  >
                    Usuń lek
                  </button>
                </div>
              )
            })}
            {target.index == null && (
              <button
                type="button"
                className="text-button"
                onClick={() => {
                  set('medications', [
                    ...draft.medications,
                    { name: '', dose: null, doseState: 'Unknown', schedule: null, reason: null },
                  ])
                  set('medicationsState', 'Provided')
                }}
              >
                Dodaj lek
              </button>
            )}
          </>
        )}
        {shows('allergies') && (
          <>
            <h4>Alergie</h4>
            {target.index == null && (
              <StateField
                label="Informacje o alergiach"
                value={draft.allergiesState}
                onChange={(value) => set('allergiesState', value)}
              />
            )}
            {draft.allergies.map((allergy, index) =>
              target.index != null && target.index !== index ? null : (
                <div className="report-editor-item" key={index}>
                  <Field
                    label={`Alergen ${index + 1}`}
                    value={allergy.substance}
                    maxLength={200}
                    required
                    onChange={(substance) =>
                      set(
                        'allergies',
                        draft.allergies.map((item, i) =>
                          i === index ? { ...item, substance } : item,
                        ),
                      )
                    }
                  />
                  <Field
                    label="Reakcja"
                    value={allergy.reaction}
                    onChange={(reaction) =>
                      set(
                        'allergies',
                        draft.allergies.map((item, i) =>
                          i === index ? { ...item, reaction } : item,
                        ),
                      )
                    }
                  />
                  <button
                    type="button"
                    className="text-button muted"
                    onClick={() =>
                      set(
                        'allergies',
                        draft.allergies.filter((_, i) => i !== index),
                      )
                    }
                  >
                    Usuń alergię
                  </button>
                </div>
              ),
            )}
            {target.index == null && (
              <button
                type="button"
                className="text-button"
                onClick={() => {
                  set('allergies', [...draft.allergies, { substance: '', reaction: null }])
                  set('allergiesState', 'Provided')
                }}
              >
                Dodaj alergię
              </button>
            )}
          </>
        )}
        {shows('chronicConditions') && (
          <>
            <h4>Choroby przewlekłe</h4>
            {target.index == null && (
              <StateField
                label="Informacje o chorobach"
                value={draft.chronicConditionsState}
                onChange={(value) => set('chronicConditionsState', value)}
              />
            )}
            {draft.chronicConditions.map((condition, index) =>
              target.index != null && target.index !== index ? null : (
                <div className="report-editor-item" key={index}>
                  <Field
                    label={`Choroba ${index + 1}`}
                    value={condition.name}
                    maxLength={200}
                    required
                    onChange={(name) =>
                      set(
                        'chronicConditions',
                        draft.chronicConditions.map((item, i) =>
                          i === index ? { ...item, name } : item,
                        ),
                      )
                    }
                  />
                  <Field
                    label="Opis choroby"
                    value={condition.description}
                    onChange={(description) =>
                      set(
                        'chronicConditions',
                        draft.chronicConditions.map((item, i) =>
                          i === index ? { ...item, description } : item,
                        ),
                      )
                    }
                  />
                  <button
                    type="button"
                    className="text-button muted"
                    onClick={() =>
                      set(
                        'chronicConditions',
                        draft.chronicConditions.filter((_, i) => i !== index),
                      )
                    }
                  >
                    Usuń chorobę
                  </button>
                </div>
              ),
            )}
            {target.index == null && (
              <button
                type="button"
                className="text-button"
                onClick={() => {
                  set('chronicConditions', [
                    ...draft.chronicConditions,
                    { name: '', description: null },
                  ])
                  set('chronicConditionsState', 'Provided')
                }}
              >
                Dodaj chorobę
              </button>
            )}
          </>
        )}
        {shows('questions') && (
          <>
            <label className="report-field">
              <span>Pytania do lekarza (po jednym w wierszu)</span>
              <textarea
                value={draft.questions.join('\n')}
                onChange={(event) => set('questions', event.target.value.split('\n'))}
              />
            </label>
          </>
        )}
        {shows('notes') && (
          <>
            <label className="report-field">
              <span>Dodatkowe informacje</span>
              <textarea
                value={draft.additionalNotes ?? ''}
                maxLength={8000}
                onChange={(event) => set('additionalNotes', event.target.value)}
              />
            </label>
          </>
        )}
      </fieldset>
      <div className="report-actions">
        <button className="button primary" disabled={busy}>
          Zapisz poprawki
        </button>
        <button type="button" className="button secondary" disabled={busy} onClick={onCancel}>
          Anuluj edycję
        </button>
      </div>
    </form>
  )
}
