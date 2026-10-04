import type { PatientInterview } from '../src/lib/patient-report'
export function reportFixture(): PatientInterview {
  return {
    visitId: 'test-visit',
    scheduledAt: '2026-12-10T10:00:00Z',
    serviceExpiresAt: '2026-12-11T10:00:00Z',
    status: 'AwaitingApproval',
    latestVersion: null,
    consentActive: false,
    observations: [],
    supplementationRound: null,
    draft: {
      revision: 4,
      consultationReason: 'Ból głowy od trzech dni.',
      symptoms: [
        {
          name: 'Ból głowy',
          startedOn: null,
          startedOnState: 'Unknown',
          frequency: 'Okresowy',
          severity: 4,
          dailyImpact: 'Trudności w pracy',
          description: 'Ból skroni',
          timeline: [{ occurredOn: null, period: 'Od trzech dni', description: 'Początek bólu' }],
        },
      ],
      medications: [
        {
          name: 'Paracetamol',
          dose: null,
          doseState: 'Unknown',
          schedule: 'Doraźnie',
          reason: 'Ból głowy',
        },
      ],
      allergies: [],
      chronicConditions: [],
      questions: ['Czy potrzebne są badania?'],
      additionalNotes: 'Wcześniejsze uwagi',
      medicationsState: 'Provided',
      allergiesState: 'Unknown',
      chronicConditionsState: 'NotAsked',
      clarifications: [],
    },
  }
}
