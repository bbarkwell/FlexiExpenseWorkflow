import type { ApproverType, Step, UserSummary } from '../types'

const TYPES: { value: ApproverType; label: string }[] = [
  { value: 'Manager', label: "Submitter's manager" },
  { value: 'DepartmentHead', label: "Submitter's department head" },
  { value: 'Role', label: 'Anyone with a role' },
  { value: 'SpecificUser', label: 'A specific person' },
]

interface Props {
  steps: Step[]
  onChange: (steps: Step[]) => void
  roles: string[]
  users: UserSummary[]
  disabled: boolean
}

export function StepsEditor({ steps, onChange, roles, users, disabled }: Props) {
  const set = (i: number, patch: Partial<Step>) => onChange(steps.map((s, j) => (j === i ? { ...s, ...patch } : s)))
  const move = (i: number, by: number) => {
    const next = [...steps]
    ;[next[i], next[i + by]] = [next[i + by], next[i]]
    onChange(next)
  }

  return (
    <ol className="steps">
      {steps.map((s, i) => (
        <li key={i} className="row">
          <span className="step-no">{i + 1}.</span>
          <select
            disabled={disabled}
            value={s.approverType}
            onChange={(e) => {
              const approverType = e.target.value as ApproverType
              set(i, { approverType, approverParam: approverType === 'Role' ? (roles[0] ?? null) : null })
            }}
          >
            {TYPES.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>
          {s.approverType === 'Role' && (
            <select disabled={disabled} value={s.approverParam ?? ''} onChange={(e) => set(i, { approverParam: e.target.value })}>
              {roles.map((r) => (
                <option key={r}>{r}</option>
              ))}
            </select>
          )}
          {s.approverType === 'SpecificUser' && (
            <select disabled={disabled} value={s.approverParam ?? ''} onChange={(e) => set(i, { approverParam: e.target.value })}>
              <option value="">Choose…</option>
              {users.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.name}
                </option>
              ))}
            </select>
          )}
          {!disabled && (
            <span className="actions">
              <button type="button" aria-label="Move step up" disabled={i === 0} onClick={() => move(i, -1)}>
                ↑
              </button>
              <button type="button" aria-label="Move step down" disabled={i === steps.length - 1} onClick={() => move(i, 1)}>
                ↓
              </button>
              <button type="button" aria-label="Remove step" onClick={() => onChange(steps.filter((_, j) => j !== i))}>
                ✕
              </button>
            </span>
          )}
        </li>
      ))}
      {steps.length === 0 && <li className="muted">No approval steps yet.</li>}
      {!disabled && (
        <li>
          <button type="button" onClick={() => onChange([...steps, { approverType: 'Manager', approverParam: null }])}>
            Add approval step
          </button>
        </li>
      )}
    </ol>
  )
}
