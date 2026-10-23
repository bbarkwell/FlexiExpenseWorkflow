import type { ReactNode } from 'react'
import { ApiError } from '../api'
import { formatDateTime } from '../dates'
import type { ApprovalTask } from '../types'

export function ErrorBox({ error }: { error: unknown }) {
  if (!error) return null
  const e = error instanceof ApiError ? error : null
  return (
    <div className="box error" role="alert">
      <strong>{error instanceof Error ? error.message : String(error)}</strong>
      {e && e.errors.length > 0 && (
        <ul>
          {e.errors.map((m, i) => (
            <li key={i}>{m}</li>
          ))}
        </ul>
      )}
      {e && e.warnings.length > 0 && <Warnings warnings={e.warnings} />}
    </div>
  )
}

export function Warnings({ warnings }: { warnings: string[] }) {
  if (warnings.length === 0) return null
  return (
    <div className="box warn">
      <strong>Heads up</strong>
      <ul>
        {warnings.map((w, i) => (
          <li key={i}>{w}</li>
        ))}
      </ul>
    </div>
  )
}

export function Loading({ what = 'Loading' }: { what?: string }) {
  return <p className="muted">{what}…</p>
}

export function StatusBadge({ status }: { status: string }) {
  const label = status === 'InReview' ? 'In review' : status
  return <span className={`badge badge-${status.toLowerCase()}`}>{label}</span>
}

export function Page({ title, actions, children }: { title: string; actions?: ReactNode; children: ReactNode }) {
  return (
    <section className="page">
      <header className="page-header">
        <h1>{title}</h1>
        <div className="actions">{actions}</div>
      </header>
      {children}
    </section>
  )
}

export function Timeline({ tasks }: { tasks: ApprovalTask[] }) {
  if (tasks.length === 0) return <p className="muted">Not submitted yet, so there is no approval chain.</p>
  return (
    <ol className="timeline">
      {tasks.map((t) => {
        const who = t.assignedRole ? `Anyone with the ${t.assignedRole} role` : (t.effectiveApprover ?? '—')
        return (
          <li key={t.id} className={`tl-${t.status.toLowerCase()}`}>
            <div className="tl-head">
              <strong>
                Step {t.sequence}: {who}
              </strong>
              <StatusBadge status={t.status} />
            </div>
            {t.delegatedFrom && <div className="muted">Covering for {t.delegatedFrom} (out of office)</div>}
            {t.status === 'Skipped' && <div className="muted">Skipped</div>}
            {t.decidedBy && (
              <div className="muted">
                {t.status} by {t.decidedBy}
                {t.decidedAt ? ` on ${formatDateTime(t.decidedAt)}` : ''}
              </div>
            )}
            {t.comment && <blockquote>{t.comment}</blockquote>}
          </li>
        )
      })}
    </ol>
  )
}
