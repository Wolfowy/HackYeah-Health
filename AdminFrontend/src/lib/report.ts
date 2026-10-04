import type { ReportSnapshot } from '../models'

/** A faithful overview of approved fields, with no additional clinical inference. */
export function conversationSummary(report: ReportSnapshot): string {
  const parts = [report.consultationReason]
  if (report.symptoms.length)
    parts.push(`Zgłaszane objawy: ${report.symptoms.map((s) => s.name).join(', ')}.`)
  if (report.medications.length)
    parts.push(`Przyjmowane leki: ${report.medications.map((m) => m.name).join(', ')}.`)
  if (report.patientQuestions.length)
    parts.push(`Pytania pacjenta: ${report.patientQuestions.join(' ')}`)
  if (report.additionalNotes) parts.push(report.additionalNotes)
  return (
    parts.filter(Boolean).join(' ') || 'Brak informacji do podsumowania w zatwierdzonym raporcie.'
  )
}
