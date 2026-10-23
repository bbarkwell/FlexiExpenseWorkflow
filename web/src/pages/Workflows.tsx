import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api'
import { ErrorBox, Loading, Page, StatusBadge } from '../components/Common'
import { formatDateTime } from '../dates'
import type { Workflow } from '../types'
import { useLoad } from '../useLoad'

export function Workflows() {
  const navigate = useNavigate()
  const { data, error, loading } = useLoad(() => api.get<Workflow[]>('/admin/workflows'), [])
  const [name, setName] = useState('')
  const [actionError, setActionError] = useState<unknown>(null)

  async function create() {
    setActionError(null)
    try {
      const w = await api.post<Workflow>('/admin/workflows', { name: name.trim(), rules: [] })
      navigate(`/admin/workflows/${w.id}`)
    } catch (e) {
      setActionError(e)
    }
  }

  async function newVersion(id: number) {
    setActionError(null)
    try {
      const w = await api.post<Workflow>(`/admin/workflows/${id}/new-version`)
      navigate(`/admin/workflows/${w.id}`)
    } catch (e) {
      setActionError(e)
    }
  }

  return (
    <Page title="Approval workflows">
      <p className="muted">
        Each published version is fixed. To change the rules, make a new version, edit it, and publish. Reports already in review keep the version
        they were submitted under.
      </p>
      <ErrorBox error={error ?? actionError} />
      {loading ? (
        <Loading />
      ) : (
        <table>
          <thead>
            <tr>
              <th>Workflow</th>
              <th>Version</th>
              <th>Status</th>
              <th>Effective</th>
              <th>Rules</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {data?.map((w) => (
              <tr key={w.id}>
                <td>
                  <Link to={`/admin/workflows/${w.id}`}>{w.name}</Link>
                </td>
                <td>v{w.version}</td>
                <td>
                  <StatusBadge status={w.status} /> {w.isCurrent && <span className="chip">In use now</span>}
                </td>
                <td>{w.effectiveFrom ? formatDateTime(w.effectiveFrom) : '—'}</td>
                <td>{w.rules.length}</td>
                <td>
                  <button onClick={() => newVersion(w.id)}>New version</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2>Start a new workflow</h2>
      <div className="row">
        <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Workflow name" maxLength={100} />
        <button className="primary" disabled={!name.trim()} onClick={create}>
          Create draft
        </button>
      </div>
      <p className="muted">Only one workflow is used at a time: the newest published version across all names.</p>
    </Page>
  )
}
