import { interviewQuestions, reportFields } from '../data/mock'
import type {
  Appointment,
  ConversationMode,
  Interview,
  InterviewReport,
  ReportField,
  SummaryVersion,
} from '../models'

export function createInterview(appointment: Appointment): Interview {
  const sections = Object.fromEntries(
    reportFields.map(({ key }) => [
      key,
      {
        text: '',
        state: 'missing',
        source: 'patient',
      },
    ]),
  ) as InterviewReport['sections']
  return {
    appointmentId: appointment.id,
    status: 'not_started',
    questionIndex: 0,
    messages: [
      {
        id: crypto.randomUUID(),
        role: 'assistant',
        text: interviewQuestions[0].text,
        questionId: interviewQuestions[0].id,
        createdAt: new Date().toISOString(),
        mode: 'text',
      },
    ],
    answers: [],
    draft: { sections, observations: [] },
    versions: [],
    consent: {
      appointmentId: appointment.id,
      facilityId: appointment.facility.id,
      granted: false,
      grantedAt: null,
      revokedAt: null,
    },
    supplementRounds: [],
    updatedAt: null,
  }
}

export function submitAnswer(
  interview: Interview,
  rawText: string,
  mode: ConversationMode,
): Interview {
  const text = rawText.trim()
  const question = interviewQuestions[interview.questionIndex]
  if (!text || !question) return interview
  const now = new Date().toISOString()
  const nextIndex = interview.questionIndex + 1
  const nextQuestion = interviewQuestions[nextIndex]
  return {
    ...interview,
    questionIndex: nextIndex,
    updatedAt: now,
    status: nextQuestion ? 'in_progress' : 'awaiting_approval',
    answers: [
      ...interview.answers,
      { questionId: question.id, question: question.text, text, mode, answeredAt: now },
    ],
    messages: [
      ...interview.messages,
      {
        id: crypto.randomUUID(),
        role: 'patient',
        text,
        createdAt: now,
        mode,
        questionId: question.id,
      },
      {
        id: crypto.randomUUID(),
        role: 'assistant',
        text:
          nextQuestion?.text ??
          'Dziękuję za rozmowę. Twoje podsumowanie jest gotowe do sprawdzenia. Możesz poprawić każdą odpowiedź przed zatwierdzeniem.',
        createdAt: now,
        mode,
        questionId: nextQuestion?.id ?? null,
      },
    ],
    draft: {
      ...interview.draft,
      sections: {
        ...interview.draft.sections,
        [question.field]: {
          text,
          state: /^(nie wiem|nie pamiętam)[.!]?$/i.test(text) ? 'unknown' : 'provided',
          source: 'patient',
        },
      },
    },
  }
}

export function editReportField(
  interview: Interview,
  field: ReportField,
  rawText: string,
): Interview {
  const text = rawText.trim()
  return {
    ...interview,
    status: 'in_progress',
    updatedAt: new Date().toISOString(),
    draft: {
      ...interview.draft,
      sections: {
        ...interview.draft.sections,
        [field]: { text, state: text ? 'provided' : 'missing', source: 'patient_edited' },
      },
    },
  }
}

export function missingFields(report: InterviewReport): ReportField[] {
  return reportFields
    .filter(({ key }) => key !== 'additionalNotes' && report.sections[key].state !== 'provided')
    .map(({ key }) => key)
}

export function isCurrentVersionApproved(interview: Interview): boolean {
  const version = interview.versions.at(-1)
  return !!version && JSON.stringify(version.report) === JSON.stringify(interview.draft)
}

export function approveReport(interview: Interview, acknowledgeMissing: boolean): Interview {
  const missing = missingFields(interview.draft)
  if (missing.length && !acknowledgeMissing)
    throw new Error('Potwierdź zatwierdzenie niepełnego raportu.')
  if (isCurrentVersionApproved(interview)) return interview
  const version: SummaryVersion = {
    id: crypto.randomUUID(),
    version: interview.versions.length + 1,
    approvedAt: new Date().toISOString(),
    report: structuredClone(interview.draft),
    acknowledgedMissingFields: missing,
  }
  return {
    ...interview,
    versions: [...interview.versions, version],
    status: interview.consent.granted ? 'shared' : 'awaiting_approval',
    updatedAt: version.approvedAt,
  }
}

export function setSharingConsent(interview: Interview, granted: boolean): Interview {
  if (granted && !isCurrentVersionApproved(interview))
    throw new Error('Najpierw zatwierdź aktualną treść raportu.')
  const now = new Date().toISOString()
  return {
    ...interview,
    status: granted ? 'shared' : 'in_progress',
    updatedAt: now,
    consent: {
      ...interview.consent,
      granted,
      grantedAt: granted ? now : interview.consent.grantedAt,
      revokedAt: granted ? null : now,
    },
  }
}

export function reportForFacility(interview: Interview): SummaryVersion | null {
  return interview.consent.granted ? (interview.versions.at(-1) ?? null) : null
}
