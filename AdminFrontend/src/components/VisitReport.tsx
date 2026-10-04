import { useEffect, useState } from 'react'
import { Alert, App, Button, Empty, Input, Select, Skeleton, Modal } from 'antd'
import { DownloadOutlined, ReloadOutlined, SendOutlined } from '@ant-design/icons'
import type { Appointment, DataMode, ReportSnapshot, ReportVersion, StaffUser } from '../models'
import { receptionService } from '../services/reception'
import { ReportContent } from './ReportContent'
import { formatDateTime } from '../lib/date'

export function VisitReport({
  visit,
  user,
  mode,
  onRefresh,
  onError,
}: {
  visit: Appointment
  user: StaffUser
  mode: DataMode
  onRefresh: () => Promise<void>
  onError: (error: unknown) => void
}) {
  const { message } = App.useApp()
  const [versions, setVersions] = useState<ReportVersion[]>([])
  const [versionId, setVersionId] = useState('')
  const [report, setReport] = useState<ReportSnapshot | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [revision, setRevision] = useState(0)
  const [questions, setQuestions] = useState('')
  const [confirm, setConfirm] = useState(false)
  const [busy, setBusy] = useState(false)
  const allowed = mode === 'demo' || user.role === 'Clinician'
  const available = visit.reportAvailable ?? !!visit.report
  function fail(err: unknown) {
    setReport(null)
    setError(err instanceof Error ? err.message : 'Nie udało się wczytać raportu.')
    onError(err)
  }
  useEffect(() => {
    let cancelled = false
    setReport(null)
    setError('')
    setVersions([])
    setVersionId('')
    if (!allowed || !available || visit.status === 'Cancelled') {
      setLoading(false)
      return
    }
    setLoading(true)
    receptionService
      .getReportVersions(visit.visitId)
      .then((data) => {
        if (cancelled) return
        const sorted = [...data].sort((a, b) => b.versionNumber - a.versionNumber)
        setVersions(sorted)
        setVersionId(sorted[0]?.versionId || '')
      })
      .catch((err) => {
        if (!cancelled) fail(err)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
    // Session and visit changes invalidate all clinical content.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visit.visitId, visit.status, allowed, available, revision])
  useEffect(() => {
    if (!versionId) return
    let cancelled = false
    setLoading(true)
    setReport(null)
    setError('')
    receptionService
      .getReport(visit.visitId, versionId)
      .then((data) => {
        if (!cancelled) setReport(data)
      })
      .catch((err) => {
        if (!cancelled) fail(err)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visit.visitId, versionId, revision])

  async function download() {
    setBusy(true)
    try {
      const blob = await receptionService.getReportPdf(visit.visitId, versionId)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `DocPrep-${visit.externalVisitId}-v${report?.versionNumber}.pdf`
      a.click()
      setTimeout(() => URL.revokeObjectURL(url), 1000)
    } catch (err) {
      fail(err)
    } finally {
      setBusy(false)
    }
  }
  async function sendQuestions() {
    setBusy(true)
    try {
      await receptionService.addQuestions(
        visit.visitId,
        questions
          .split('\n')
          .map((q) => q.trim())
          .filter(Boolean),
      )
      setQuestions('')
      setConfirm(false)
      await onRefresh()
      message.success(
        mode === 'demo'
          ? 'Dodano pytania w wersji demo.'
          : 'Wysłano pytania do uzupełnienia przez pacjenta.',
      )
    } catch (err) {
      fail(err)
    } finally {
      setBusy(false)
    }
  }
  if (!allowed)
    return (
      <Alert
        type="info"
        showIcon
        title="Raport jest dostępny dla przypisanego lekarza"
        description="Recepcja widzi status przygotowania wizyty. Treść raportu otworzysz na koncie lekarza."
      />
    )
  if (!available || visit.status === 'Cancelled')
    return (
      <Empty
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        description={
          <>
            <h3>Raport jeszcze nie jest gotowy</h3>
            <p>
              {visit.status === 'Cancelled'
                ? 'Ta wizyta została anulowana.'
                : 'Wizyta pozostaje w planie dnia. Raport pojawi się po zatwierdzeniu i udostępnieniu przez pacjenta.'}
            </p>
          </>
        }
      />
    )
  const questionList = questions
    .split('\n')
    .map((q) => q.trim())
    .filter(Boolean)
  const valid =
    questionList.length > 0 &&
    questionList.length <= 20 &&
    questionList.every((q) => q.length <= 1000)
  return (
    <div>
      <div className="report-tools">
        <Select
          aria-label="Wersja raportu"
          value={versionId || undefined}
          placeholder="Wybierz wersję"
          onChange={setVersionId}
          options={versions.map((v) => ({
            value: v.versionId,
            label: `Wersja ${v.versionNumber} · ${formatDateTime(v.approvedAt)}`,
          }))}
        />
        <Button
          aria-label="Odśwież raport"
          icon={<ReloadOutlined />}
          onClick={() => setRevision((r) => r + 1)}
        />
        <Button
          icon={<DownloadOutlined />}
          disabled={!report || mode === 'demo'}
          loading={busy}
          onClick={download}
        >
          Pobierz PDF
        </Button>
      </div>
      {error && <Alert type="error" showIcon title={error} className="form-alert" />}
      {loading ? (
        <Skeleton active paragraph={{ rows: 6 }} />
      ) : report ? (
        <ReportContent report={report} demo={mode === 'demo'} />
      ) : (
        !error && <Empty description="Brak dostępnej zatwierdzonej wersji raportu" />
      )}
      {report && user.role === 'Clinician' && (
        <section className="questions-card">
          <h3>Pytania uzupełniające</h3>
          <p>
            Pacjent odpowie w dodatkowej rundzie wywiadu. Obecna wersja raportu pozostanie dostępna.
          </p>
          <Input.TextArea
            aria-label="Pytania uzupełniające"
            value={questions}
            onChange={(e) => setQuestions(e.target.value)}
            autoSize={{ minRows: 3, maxRows: 10 }}
            placeholder="Jedno pytanie w każdym wierszu"
          />
          <small>Maksymalnie 20 pytań, po 1000 znaków.</small>
          <Button
            type="primary"
            icon={<SendOutlined />}
            aria-label="Wyślij pytania"
            disabled={!valid}
            onClick={() => setConfirm(true)}
          >
            Wyślij pytania
          </Button>
        </section>
      )}
      <Modal
        open={confirm}
        title="Przekaż pytania pacjentowi"
        okText={mode === 'demo' ? 'Dodaj w demo' : 'Wyślij do pacjenta'}
        cancelText="Wróć"
        onCancel={() => setConfirm(false)}
        onOk={sendQuestions}
        confirmLoading={busy}
      >
        <p>
          {mode === 'demo'
            ? 'Symulacja rundy uzupełniającej.'
            : 'Pacjent otrzyma powiadomienie i dostęp do dodatkowego wywiadu.'}
        </p>
        <ol>
          {questionList.map((q, i) => (
            <li key={i}>{q}</li>
          ))}
        </ol>
      </Modal>
    </div>
  )
}
