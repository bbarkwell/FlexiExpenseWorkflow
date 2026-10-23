import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api, ApiError } from '../api'
import { ConditionEditor } from '../components/ConditionEditor'
import { ErrorBox, Loading, Page, StatusBadge, Warnings } from '../components/Common'
import { StepsEditor } from '../components/StepsEditor'
import { toRuleForm, toRuleRequest, type RuleForm } from '../conditions'
import { formatDateTime } from '../dates'
import { dollarsToCents, formatCents } from '../money'
import type { Simulation, Validation, Workflow } from '../types'
import { useLoad } from '../useLoad'
import { useMe } from '../UserContext'

export function WorkflowEditor() {
  const { id } = useParams()
  const roles = useLoad(() => api.get<string[]>('/admin/roles'), [])
  const wf = useLoad(() => api.get<Workflow>(`/admin/workflows/${id}`), [id])
  if (wf.loading || roles.loading) return <Loading />
  if (!wf.data) return <ErrorBox error={wf.error ?? new Error('Workflow not found.')} />
  return <Editor key={wf.data.id} workflow={wf.data} roles={roles.data ?? []} />
}

function Editor({ workflow, roles }: { workflow: Workflow; roles: string[] }) {
  const navigate = useNavigate()
  const { users } = useMe()
  const readOnly = workflow.status !== 'Draft'

  const [rules, setRules] = useState<RuleForm[]>(workflow.rules.map(toRuleForm))
  const [dirty, setDirty] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [validation, setValidation] = useState<Validation | null>(null)
  const [effectiveFrom, setEffectiveFrom] = useState('')

  function edit(next: RuleForm[]) {
    setRules(next)
    setDirty(true)
    setValidation(null)
  }
  const patch = (i: number, p: Partial<RuleForm>) => edit(rules.map((r, j) => (j === i ? { ...r, ...p } : r)))
  const moveRule = (i: number, by: number) => {
    const next = [...rules]
    ;[next[i], next[i + by]] = [next[i + by], next[i]]
    edit(next)
  }

  /** Runs an action with busy/error handling; returns whether it succeeded. */
  async function run(action: () => Promise<void>): Promise<boolean> {
    setBusy(true)
    setError(null)
    try {
      await action()
      return true
    } catch (e) {
      setError(e)
      return false
    } finally {
      setBusy(false)
    }
  }

  async function saveDraft() {
    await api.put(`/admin/workflows/${workflow.id}`, { rules: rules.map(toRuleRequest) })
    setDirty(false)
  }

  const save = () => run(saveDraft)

  const validate = () =>
    run(async () => {
      await saveDraft()
      setValidation(await api.post<Validation>(`/admin/workflows/${workflow.id}/validate`))
    })

  const publish = () =>
    run(async () => {
      await saveDraft()
      const from = effectiveFrom ? new Date(effectiveFrom).toISOString() : null
      try {
        await api.post(`/admin/workflows/${workflow.id}/publish`, { effectiveFrom: from })
      } catch (e) {
        if (e instanceof ApiError && e.status === 422) setValidation({ errors: e.errors, warnings: e.warnings })
        throw e
      }
      navigate('/admin/workflows')
    })

  const remove = async () => {
    if (!confirm('Delete this draft?')) return
    await run(async () => {
      await api.del(`/admin/workflows/${workflow.id}`)
      navigate('/admin/workflows')
    })
  }

  return (
    <Page
      title={`${workflow.name} · v${workflow.version}`}
      actions={
        <Link className="button" to="/admin/workflows">
          All workflows
        </Link>
      }
    >
      <p>
        <StatusBadge status={workflow.status} /> {workflow.isCurrent && <span className="chip">In use now</span>}{' '}
        {workflow.effectiveFrom && <span className="muted">Effective {formatDateTime(workflow.effectiveFrom)}</span>}
      </p>
      {readOnly && (
        <div className="box">
          Published versions can't be changed. Use <strong>New version</strong> on the workflows page to edit a copy.
        </div>
      )}
      <p className="muted">
        Rules are checked top to bottom and the first one that applies decides the approval chain. Approvers act in the order listed. If an
        approver is out of office, their delegate acts instead.
      </p>
      <ErrorBox error={error} />

      {rules.map((rule, i) => (
        <article className="card rule" key={i}>
          <div className="card-head">
            <input
              className="rule-name"
              disabled={readOnly}
              value={rule.name}
              maxLength={100}
              placeholder="Rule name, e.g. Over $5,000"
              onChange={(e) => patch(i, { name: e.target.value })}
            />
            {!readOnly && (
              <span className="actions">
                <button aria-label="Move rule up" disabled={i === 0} onClick={() => moveRule(i, -1)}>
                  ↑
                </button>
                <button aria-label="Move rule down" disabled={i === rules.length - 1} onClick={() => moveRule(i, 1)}>
                  ↓
                </button>
                <button aria-label="Delete rule" onClick={() => edit(rules.filter((_, j) => j !== i))}>
                  Delete
                </button>
              </span>
            )}
          </div>
          <h3>Applies when</h3>
          <ConditionEditor draft={rule.cond} disabled={readOnly} onChange={(cond) => patch(i, { cond })} />
          <h3>Then approvals go to</h3>
          <StepsEditor
            steps={rule.steps}
            roles={roles}
            users={users}
            disabled={readOnly}
            onChange={(steps) => patch(i, { steps })}
          />
        </article>
      ))}
      {rules.length === 0 && <p className="muted">No rules yet.</p>}

      {!readOnly && (
        <>
          <div className="actions">
            <button onClick={() => edit([...rules, { name: '', cond: { mode: 'always' }, steps: [{ approverType: 'Manager', approverParam: null }] }])}>
              Add rule
            </button>
          </div>

          <div className="box">
            <div className="actions">
              <button className="primary" disabled={busy || !dirty} onClick={save}>
                {dirty ? 'Save draft' : 'Saved'}
              </button>
              <button disabled={busy} onClick={validate}>
                Check for problems
              </button>
              <button className="danger" disabled={busy} onClick={remove}>
                Delete draft
              </button>
            </div>
            {validation && (
              <>
                {validation.errors.length === 0 ? (
                  <p className="ok">No problems found. This workflow is ready to publish.</p>
                ) : (
                  <div className="box error">
                    <strong>Fix these before publishing</strong>
                    <ul>
                      {validation.errors.map((m, i) => (
                        <li key={i}>{m}</li>
                      ))}
                    </ul>
                  </div>
                )}
                <Warnings warnings={validation.warnings} />
              </>
            )}
          </div>

          <div className="box">
            <h2>Publish</h2>
            <p className="muted">
              New submissions use this version from the date below. Reports already in review are not affected.
            </p>
            <div className="row">
              <label className="field">
                Take effect (leave empty for now)
                <input type="datetime-local" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} />
              </label>
              <button className="primary" disabled={busy} onClick={publish}>
                Publish
              </button>
            </div>
          </div>
        </>
      )}

      <Simulator workflowId={workflow.id} dirty={dirty} save={saveDraft} readOnly={readOnly} />
    </Page>
  )
}

function Simulator({ workflowId, dirty, save, readOnly }: { workflowId: number; dirty: boolean; save: () => Promise<void>; readOnly: boolean }) {
  const { users, me } = useMe()
  // Admins often have no manager, which would make the first simulation fail; start from someone who does.
  const [submitterId, setSubmitterId] = useState(String((users.find((u) => u.manager) ?? me).id))
  const [amount, setAmount] = useState('1000')
  const [categories, setCategories] = useState('')
  const [result, setResult] = useState<Simulation | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  async function simulate() {
    setBusy(true)
    setError(null)
    setResult(null)
    try {
      const cents = dollarsToCents(amount)
      if (!cents) throw new Error('Enter an amount greater than zero, like 750.00.')
      if (dirty && !readOnly) await save()
      setResult(
        await api.post<Simulation>(`/admin/workflows/${workflowId}/simulate`, {
          submitterId: Number(submitterId),
          totalCents: cents,
          categories: categories.split(',').map((c) => c.trim()).filter(Boolean),
        }),
      )
    } catch (e) {
      setError(e)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="box">
      <h2>Try it</h2>
      <p className="muted">See who would approve a report like this, without creating anything.{!readOnly && ' Unsaved changes are saved first.'}</p>
      <div className="row">
        <label className="field">
          Submitted by
          <select value={submitterId} onChange={(e) => setSubmitterId(e.target.value)}>
            {users.map((u) => (
              <option key={u.id} value={u.id}>
                {u.name}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          Total ($)
          <input className="amount" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
        </label>
        <label className="field">
          Categories (optional)
          <input value={categories} placeholder="Travel, Meals" onChange={(e) => setCategories(e.target.value)} />
        </label>
        <button disabled={busy} onClick={simulate}>
          Simulate
        </button>
      </div>
      <ErrorBox error={error} />
      {result && (
        <div>
          <p>
            A {formatCents(dollarsToCents(amount) ?? 0)} report matches <strong>{result.ruleName}</strong>:
          </p>
          <ol className="timeline">
            {result.steps.map((s) => (
              <li key={s.sequence} className={s.skipped ? 'tl-skipped' : ''}>
                <strong>{s.approvers.join(', ') || 'No one'}</strong>
                {s.approverType === 'Role' && <span className="muted"> (any one of them)</span>}
                {s.actingToday && <div className="muted">Out of office today, so {s.actingToday} would approve instead</div>}
                {s.skipped && <div className="muted">Skipped: {s.skipReason}</div>}
              </li>
            ))}
          </ol>
        </div>
      )}
    </div>
  )
}
