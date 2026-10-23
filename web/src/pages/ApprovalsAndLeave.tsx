import { useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { ErrorBox, Loading, Page } from '../components/Common'
import { DecisionBox } from '../components/DecisionBox'
import { formatDate } from '../dates'
import { formatCents } from '../money'
import type { Delegation, PendingApproval } from '../types'
import { useLoad } from '../useLoad'
import { useMe } from '../UserContext'

export function Approvals() {
  const { data, error, loading, reload } = useLoad(() => api.get<PendingApproval[]>('/approvals/pending'), [])
  const [open, setOpen] = useState<number | null>(null)

  return (
    <Page title="Waiting for my approval">
      <ErrorBox error={error} />
      {loading ? (
        <Loading />
      ) : data?.length === 0 ? (
        <p className="muted">Nothing is waiting for you.</p>
      ) : (
        <div className="cards">
          {data?.map((p) => (
            <article key={p.taskId} className="card">
              <div className="card-head">
                <Link to={`/reports/${p.reportId}`}>
                  <strong>{p.title}</strong>
                </Link>
                <strong>{formatCents(p.totalCents)}</strong>
              </div>
              <div className="muted">
                {p.submitterName}
                {p.submittedAt ? ` · submitted ${formatDate(p.submittedAt)}` : ''} · step {p.sequence}
              </div>
              {p.delegatedFrom && <div className="chip">Covering for {p.delegatedFrom}</div>}
              {open === p.taskId ? (
                <DecisionBox
                  taskId={p.taskId}
                  onDone={() => {
                    setOpen(null)
                    reload()
                  }}
                />
              ) : (
                <div className="actions">
                  <button onClick={() => setOpen(p.taskId)}>Review…</button>
                </div>
              )}
            </article>
          ))}
        </div>
      )}
    </Page>
  )
}

const iso = (offsetDays: number) => new Date(Date.now() + offsetDays * 864e5).toISOString().slice(0, 10)

export function OutOfOffice() {
  const { users, me } = useMe()
  const list = useLoad(() => api.get<Delegation[]>('/me/delegations'), [])
  const [delegateId, setDelegateId] = useState('')
  const [startsOn, setStartsOn] = useState(iso(0))
  const [endsOn, setEndsOn] = useState(iso(7))
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  async function add() {
    setBusy(true)
    setError(null)
    try {
      if (!delegateId) throw new Error('Choose who will approve for you.')
      await api.post('/me/delegations', { delegateUserId: Number(delegateId), startsOn, endsOn })
      setDelegateId('')
      list.reload()
    } catch (e) {
      setError(e)
    } finally {
      setBusy(false)
    }
  }

  async function remove(id: number) {
    setError(null)
    try {
      await api.del(`/me/delegations/${id}`)
      list.reload()
    } catch (e) {
      setError(e)
    }
  }

  return (
    <Page title="Out of office">
      <p className="muted">While a window is active, approvals that would come to you go to your delegate instead.</p>
      <ErrorBox error={error ?? list.error} />

      {list.loading ? (
        <Loading />
      ) : (
        <table>
          <thead>
            <tr>
              <th>Delegate</th>
              <th>From</th>
              <th>Until</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {list.data?.map((d) => (
              <tr key={d.id}>
                <td>{d.delegateName}</td>
                <td>{d.startsOn}</td>
                <td>{d.endsOn}</td>
                <td>
                  <button onClick={() => remove(d.id)}>Cancel</button>
                </td>
              </tr>
            ))}
            {list.data?.length === 0 && (
              <tr>
                <td colSpan={4} className="muted">
                  No upcoming out-of-office windows.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      )}

      <h2>Add a window</h2>
      <div className="row">
        <label className="field">
          Delegate
          <select value={delegateId} onChange={(e) => setDelegateId(e.target.value)}>
            <option value="">Choose…</option>
            {users
              .filter((u) => u.id !== me.id)
              .map((u) => (
                <option key={u.id} value={u.id}>
                  {u.name}
                </option>
              ))}
          </select>
        </label>
        <label className="field">
          From
          <input type="date" value={startsOn} onChange={(e) => setStartsOn(e.target.value)} />
        </label>
        <label className="field">
          Until
          <input type="date" value={endsOn} onChange={(e) => setEndsOn(e.target.value)} />
        </label>
        <button className="primary" disabled={busy} onClick={add}>
          Add
        </button>
      </div>
    </Page>
  )
}
