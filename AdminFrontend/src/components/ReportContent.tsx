import { Alert, Tag } from 'antd'
import { CheckCircleOutlined, FileTextOutlined } from '@ant-design/icons'
import type { ReportSnapshot } from '../models'
import { conversationSummary } from '../lib/report'
import { formatDateTime, inWarsaw } from '../lib/date'

export function ReportContent({ report, demo = true }: { report: ReportSnapshot; demo?: boolean }) {
  return (
    <div className="report-content">
      <div className="report-banner">
        <div className="report-icon">
          <FileTextOutlined />
        </div>
        <div>
          <b>
            Raport przed wizytą <Tag color="green">Wersja {report.versionNumber}</Tag>
          </b>
          <small>
            <CheckCircleOutlined /> Zatwierdzony {formatDateTime(report.approvedAt)}
          </small>
        </div>
      </div>
      <section className="conversation-summary">
        <span className="eyebrow">NA PODSTAWIE ZATWIERDZONEGO RAPORTU</span>
        <h3>Podsumowanie rozmowy</h3>
        <p>{conversationSummary(report)}</p>
      </section>
      {demo && (
        <Alert
          type="info"
          showIcon
          title="Przykładowy raport pacjenta"
          description="Dane w raporcie są fikcyjne. Podgląd prezentuje zatwierdzoną wersję udostępnioną przed wizytą."
        />
      )}
      {report.confirmedIncomplete && (
        <Alert
          type="warning"
          showIcon
          title="Pacjent zatwierdził raport z brakującymi informacjami."
        />
      )}
      <section className="report-section">
        <h3>Powód konsultacji</h3>
        <p>{report.consultationReason}</p>
      </section>
      <section className="report-section">
        <h3>Zgłaszane objawy</h3>
        {report.symptoms.length ? (
          report.symptoms.map((symptom, index) => (
            <article className="symptom" key={index}>
              <div>
                <b>{symptom.name}</b>
                <Tag>Relacja pacjenta</Tag>
              </div>
              <p>{symptom.description || 'Brak dodatkowego opisu.'}</p>
              <dl>
                <div>
                  <dt>Od kiedy</dt>
                  <dd>
                    {symptom.startedOn
                      ? inWarsaw(symptom.startedOn).format('D MMM YYYY')
                      : 'Nie podano'}
                  </dd>
                </div>
                <div>
                  <dt>Częstotliwość</dt>
                  <dd>{symptom.frequency || 'Nie podano'}</dd>
                </div>
                <div>
                  <dt>Nasilenie</dt>
                  <dd>{symptom.severity !== null ? `${symptom.severity}/10` : 'Nie podano'}</dd>
                </div>
              </dl>
              {symptom.dailyImpact && (
                <p>
                  <b>Wpływ na codzienność:</b> {symptom.dailyImpact}
                </p>
              )}
              {symptom.timeline.map((item, i) => (
                <p key={i} className="report-timeline-note">
                  {item.occurredOn
                    ? inWarsaw(item.occurredOn).format('D MMM')
                    : item.period || 'Nie podano daty'}
                  : {item.description}
                </p>
              ))}
            </article>
          ))
        ) : (
          <p>Nie podano objawów.</p>
        )}
      </section>
      <section className="report-section">
        <h3>Przyjmowane leki</h3>
        {report.medications.length ? (
          report.medications.map((medication, index) => (
            <div className="report-list-item" key={index}>
              <b>{medication.name}</b>
              <p>
                Dawka: {medication.dose || 'nie podano'} ·{' '}
                {medication.schedule || 'Nie podano schematu'}
              </p>
              {medication.reason && <small>Powód przyjmowania: {medication.reason}</small>}
            </div>
          ))
        ) : (
          <p>Brak leków w raporcie.</p>
        )}
      </section>
      <div className="report-columns">
        <section className="report-section">
          <h3>Alergie</h3>
          {report.allergies.length ? (
            report.allergies.map((item, index) => (
              <p key={index}>
                <b>{item.substance}</b>
                <br />
                {item.reaction || 'Nie podano reakcji'}
              </p>
            ))
          ) : (
            <p>Brak alergii w raporcie.</p>
          )}
        </section>
        <section className="report-section">
          <h3>Choroby przewlekłe</h3>
          {report.chronicConditions.length ? (
            report.chronicConditions.map((item, index) => (
              <p key={index}>
                <b>{item.name}</b>
                <br />
                {item.description}
              </p>
            ))
          ) : (
            <p>Brak chorób w raporcie.</p>
          )}
        </section>
      </div>
      <section className="report-section">
        <h3>Pytania do lekarza</h3>
        {report.patientQuestions.length ? (
          <ol>
            {report.patientQuestions.map((question, index) => (
              <li key={index}>{question}</li>
            ))}
          </ol>
        ) : (
          <p>Pacjent nie dodał pytań.</p>
        )}
      </section>
      {report.observations.length > 0 && (
        <section className="report-section">
          <h3>Obserwacje z wywiadu</h3>
          {report.observations.map((item) => (
            <div key={item.observationId} className="report-list-item">
              <Tag>
                {['AiObservation', 'ai_observation'].includes(item.source)
                  ? 'Obserwacja AI'
                  : 'Relacja pacjenta'}
              </Tag>
              <p>{item.text}</p>
            </div>
          ))}
        </section>
      )}
      {report.clarifications.length > 0 && (
        <section className="report-section">
          <h3>Informacje do wyjaśnienia</h3>
          {report.clarifications.map((item, index) => (
            <Alert key={index} type="warning" title={item.message} showIcon />
          ))}
        </section>
      )}
      {report.supplementationAnswers.length > 0 && (
        <section className="report-section">
          <h3>Odpowiedzi uzupełniające</h3>
          {report.supplementationAnswers.map((item, index) => (
            <div key={index} className="report-list-item">
              <b>{item.question}</b>
              <p>{item.answer}</p>
            </div>
          ))}
        </section>
      )}
      {report.additionalNotes && (
        <section className="report-section">
          <h3>Dodatkowe uwagi</h3>
          <p>{report.additionalNotes}</p>
        </section>
      )}
      <p className="report-footnote">
        Raport porządkuje informacje zgłoszone przed wizytą. Nie stanowi diagnozy ani zaleceń
        leczenia.
      </p>
    </div>
  )
}
