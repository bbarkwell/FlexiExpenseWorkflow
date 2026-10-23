import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api } from '../api'
import { ErrorBox, Loading, Page, StatusBadge, Timeline } from '../components/Common'
import { DecisionBox } from '../components/DecisionBox'
import { formatDate } from '../dates'
import { centsToDollars, dollarsToCents, formatCents } from '../money'
import type { PendingApproval, Report, SaveReportRequest } from '../types'
import { useLoad } from '../useLoad'
import { useMe } from '../UserContext'

const CATEGORIES = ['Travel', 'Lodging', 'Meals', 'Supplies', 'Software', 'Other']

export function MyReports() {
  const { data, error, loading } = useLoad(() => api.get<Report[]>('/reports'), [])
  return (
    <Page
      title="My reports"
      actions={
        <Link className="button primary" to="/reports/new">
          New report
        </Link>
      }
    >
      <ErrorBox error={error} />
      {loading ? (
        <Loading />
      ) : data?.length === 0 ? (
        <p className="muted">You haven't created any reports yet.</p>
      ) : (
        <table>
          <thead>
            <tr>
              <th>Report</th>
              <th>Status</th>
              <th className="num">Total</th>
              <th>Submitted</th>
            </tr>
          </thead>
          <tbody>
            {data?.map((r) => (
              <tr key={r.id}>
                <td>
                  <Link to={`/reports/${r.id}`}>{r.title}</Link>
                </td>
                <td>
                  <StatusBadge status={r.status} />
                </td>
                <td className="num">{formatCents(r.totalCents)}</td>
                <td>{r.submittedAt ? formatDate(r.submittedAt) : '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Page>
  )
}

export function ReportDetail() {
  const { id } = useParams()
  const navigate = useNavigate()
  const { me } = useMe()
  const report = useLoad(() => api.get<Report>(`/reports/${id}`), [id])
  const pending = useLoad(() => api.get<PendingApproval[]>('/approvals/pending'), [id])
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  const r = report.data
  const myTask = pending.data?.find((p) => p.reportId === r?.id)
  const isMine = r?.submitterId === me.id

  async function run(action: () => Promise<unknown>) {
    setBusy(true)
    setError(null)
    try {
      await action()
      report.reload()
      pending.reload()
    } catch (e) {
      setError(e)
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    if (!confirm('Delete this draft?')) return
    setBusy(true)
    try {
      await api.del(`/reports/${id}`)
      navigate('/reports')
    } catch (e) {
      setError(e)
      setBusy(false)
    }
  }

  if (report.loading) return <Loading />
  if (!r) return <ErrorBox error={report.error ?? new Error('Report not found.')} />

  return (
    <Page
      title={r.title}
      actions={
        <>
          {isMine && r.status === 'Draft' && (
            <>
              <Link className="button" to={`/reports/${r.id}/edit`}>
                Edit
              </Link>
              <button className="primary" disabled={busy} onClick={() => run(() => api.post(`/reports/${r.id}/submit`))}>
                Submit for approval
              </button>
              <button className="danger" disabled={busy} onClick={remove}>
                Delete
              </button>
            </>
          )}
          {isMine && r.status === 'InReview' && (
            <button disabled={busy} onClick={() => run(() => api.post(`/reports/${r.id}/withdraw`))}>
              Withdraw
            </button>
          )}
        </>
      }
    >
      <ErrorBox error={error} />
      <p>
        <StatusBadge status={r.status} /> <span className="muted">Submitted by {r.submitterName}</span>
      </p>

      {myTask && (
        <div className="box action">
          <h2>Your decision</h2>
          <p>
            {myTask.delegatedFrom ? `You're covering for ${myTask.delegatedFrom}. ` : ''}This report is waiting for you.
          </p>
          <DecisionBox
            taskId={myTask.taskId}
            onDone={() => {
              report.reload()
              pending.reload()
            }}
          />
        </div>
      )}

      <h2>Expenses</h2>
      <table>
        <thead>
          <tr>
            <th>Date</th>
            <th>Category</th>
            <th>Description</th>
            <th className="num">Amount</th>
          </tr>
        </thead>
        <tbody>
          {r.lines.map((l) => (
            <tr key={l.id}>
              <td>{l.date}</td>
              <td>{l.category}</td>
              <td>{l.description}</td>
              <td className="num">{formatCents(l.amountCents)}</td>
            </tr>
          ))}
          {r.lines.length === 0 && (
            <tr>
              <td colSpan={4} className="muted">
                No lines yet.
              </td>
            </tr>
          )}
        </tbody>
        <tfoot>
          <tr>
            <th colSpan={3}>Total</th>
            <th className="num">{formatCents(r.totalCents)}</th>
          </tr>
        </tfoot>
      </table>

      <h2>Approval chain</h2>
      <Timeline tasks={r.tasks} />
    </Page>
  )
}

interface LineForm {
  date: string
  category: string
  amount: string
  description: string
}

const today = () => new Date().toISOString().slice(0, 10)
const blankLine = (): LineForm => ({ date: today(), category: 'Travel', amount: '', description: '' })

export function ReportEditor() {
  const { id } = useParams()
  const loaded = useLoad(async () => (id ? api.get<Report>(`/reports/${id}`) : null), [id])
  if (loaded.loading) return <Loading />
  if (id && !loaded.data) return <ErrorBox error={loaded.error ?? new Error('Report not found.')} />
  // Keyed so the form initialises from the loaded report exactly once.
  return <ReportForm key={id ?? 'new'} report={loaded.data ?? null} />
}

function ReportForm({ report }: { report: Report | null }) {
  const navigate = useNavigate()
  const [title, setTitle] = useState(report?.title ?? '')
  const [lines, setLines] = useState<LineForm[]>(
    report?.lines.length
      ? report.lines.map((l) => ({ date: l.date, category: l.category, amount: centsToDollars(l.amountCents), description: l.description }))
      : [blankLine()],
  )
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  const total = lines.reduce((sum, l) => sum + (dollarsToCents(l.amount) ?? 0), 0)

  function update(i: number, patch: Partial<LineForm>) {
    setLines((ls) => ls.map((l, j) => (j === i ? { ...l, ...patch } : l)))
  }

  function toRequest(): SaveReportRequest {
    if (!title.trim()) throw new Error('Give the report a title.')
    return {
      title: title.trim(),
      lines: lines.map((l, i) => {
        const cents = dollarsToCents(l.amount)
        if (!cents) throw new Error(`Line ${i + 1}: enter an amount greater than zero, like 12.50.`)
        if (!l.date) throw new Error(`Line ${i + 1}: choose a date.`)
        if (!l.category.trim()) throw new Error(`Line ${i + 1}: choose a category.`)
        return { date: l.date, category: l.category.trim(), amountCents: cents, description: l.description.trim() }
      }),
    }
  }

  async function save(submit: boolean) {
    setBusy(true)
    setError(null)
    try {
      const body = toRequest()
      const saved = report ? await api.put<Report>(`/reports/${report.id}`, body) : await api.post<Report>('/reports', body)
      if (submit) await api.post(`/reports/${saved.id}/submit`)
      navigate(`/reports/${saved.id}`)
    } catch (e) {
      setError(e)
      setBusy(false)
    }
  }

  return (
    <Page title={report ? 'Edit report' : 'New report'}>
      <ErrorBox error={error} />
      <label className="field">
        Title
        <input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={200} placeholder="e.g. Conference trip, October" />
      </label>

      <h2>Expenses</h2>
      <datalist id="categories">
        {CATEGORIES.map((c) => (
          <option key={c} value={c} />
        ))}
      </datalist>
      <table className="form-table">
        <thead>
          <tr>
            <th>Date</th>
            <th>Category</th>
            <th>Description</th>
            <th className="num">Amount ($)</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {lines.map((l, i) => (
            <tr key={i}>
              <td>
                <input type="date" value={l.date} onChange={(e) => update(i, { date: e.target.value })} />
              </td>
              <td>
                <input list="categories" value={l.category} onChange={(e) => update(i, { category: e.target.value })} />
              </td>
              <td>
                <input value={l.description} maxLength={500} onChange={(e) => update(i, { description: e.target.value })} />
              </td>
              <td className="num">
                <input className="amount" inputMode="decimal" value={l.amount} onChange={(e) => update(i, { amount: e.target.value })} placeholder="0.00" />
              </td>
              <td>
                <button aria-label={`Remove line ${i + 1}`} onClick={() => setLines((ls) => ls.filter((_, j) => j !== i))}>
                  ✕
                </button>
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <th colSpan={3}>
              <button onClick={() => setLines((ls) => [...ls, blankLine()])}>Add line</button>
            </th>
            <th className="num">{formatCents(total)}</th>
            <th />
          </tr>
        </tfoot>
      </table>

      <div className="actions">
        <button disabled={busy} onClick={() => save(false)}>
          Save draft
        </button>
        <button className="primary" disabled={busy} onClick={() => save(true)}>
          Save and submit
        </button>
        <Link className="button" to={report ? `/reports/${report.id}` : '/reports'}>
          Cancel
        </Link>
      </div>
    </Page>
  )
}
