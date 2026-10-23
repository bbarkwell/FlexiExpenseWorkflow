import { useState } from 'react'
import { api } from '../api'
import type { Report } from '../types'
import { ErrorBox } from './Common'

/** Approve or reject one approval task. Rejecting requires a comment. */
export function DecisionBox({ taskId, onDone }: { taskId: number; onDone: (report: Report) => void }) {
  const [comment, setComment] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)

  async function decide(kind: 'approve' | 'reject') {
    if (kind === 'reject' && !comment.trim()) {
      setError(new Error('Add a comment explaining the rejection.'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      onDone(await api.post<Report>(`/approvals/${taskId}/${kind}`, { comment: comment.trim() || null }))
    } catch (e) {
      setError(e)
      setBusy(false)
    }
  }

  return (
    <div className="decision">
      <textarea
        placeholder="Comment (required to reject)"
        value={comment}
        onChange={(e) => setComment(e.target.value)}
        rows={2}
        maxLength={1000}
      />
      <div className="actions">
        <button className="primary" disabled={busy} onClick={() => decide('approve')}>
          Approve
        </button>
        <button className="danger" disabled={busy} onClick={() => decide('reject')}>
          Reject
        </button>
      </div>
      <ErrorBox error={error} />
    </div>
  )
}
