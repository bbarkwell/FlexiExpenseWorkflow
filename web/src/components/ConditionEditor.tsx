import { FIELDS, kindOf, opsFor, parseCondition, buildCondition, type CondDraft, type FieldName, type Row } from '../conditions'

interface Props {
  draft: CondDraft
  onChange: (draft: CondDraft) => void
  disabled: boolean
}

const blankRow = (): Row => ({ field: 'totalCents', op: 'gte', value: '' })

export function ConditionEditor({ draft, onChange, disabled }: Props) {
  function switchMode(mode: CondDraft['mode']) {
    if (mode === draft.mode) return
    if (mode === 'always') return onChange({ mode: 'always' })
    if (mode === 'rows') {
      // Bring a JSON condition into the form when the form can show it.
      if (draft.mode === 'json') {
        try {
          const parsed = parseCondition(JSON.parse(draft.text))
          if (parsed.mode === 'rows') return onChange(parsed)
        } catch {
          /* fall through to a blank form */
        }
      }
      return onChange({ mode: 'rows', join: 'all', rows: [blankRow()] })
    }
    let text = '{\n  "field": "totalCents",\n  "op": "gte",\n  "value": 0\n}'
    try {
      const built = buildCondition(draft)
      if (built !== null) text = JSON.stringify(built, null, 2)
    } catch {
      /* keep the template */
    }
    onChange({ mode: 'json', text })
  }

  return (
    <div className="condition">
      <div className="seg" role="radiogroup" aria-label="When this rule applies">
        {(
          [
            ['always', 'Always applies'],
            ['rows', 'When…'],
            ['json', 'Advanced (JSON)'],
          ] as const
        ).map(([mode, label]) => (
          <button
            key={mode}
            type="button"
            role="radio"
            aria-checked={draft.mode === mode}
            className={draft.mode === mode ? 'on' : ''}
            disabled={disabled}
            onClick={() => switchMode(mode)}
          >
            {label}
          </button>
        ))}
      </div>

      {draft.mode === 'always' && <p className="muted">Matches every report that no earlier rule caught. Put this rule last.</p>}

      {draft.mode === 'rows' && (
        <>
          {draft.rows.length > 1 && (
            <p>
              Match{' '}
              <select
                disabled={disabled}
                value={draft.join}
                onChange={(e) => onChange({ ...draft, join: e.target.value as 'all' | 'any' })}
              >
                <option value="all">all</option>
                <option value="any">any</option>
              </select>{' '}
              of these:
            </p>
          )}
          {draft.rows.map((row, i) => (
            <div className="row" key={i}>
              <select
                disabled={disabled}
                value={row.field}
                onChange={(e) => {
                  const field = e.target.value as FieldName
                  const op = opsFor(field).some((o) => o.value === row.op) ? row.op : opsFor(field)[0].value
                  setRow(i, { field, op, value: kindOf(field) === kindOf(row.field) ? row.value : '' })
                }}
              >
                {FIELDS.map((f) => (
                  <option key={f.value} value={f.value}>
                    {f.label}
                  </option>
                ))}
              </select>
              <select disabled={disabled} value={row.op} onChange={(e) => setRow(i, { op: e.target.value })}>
                {opsFor(row.field).map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </select>
              <input
                disabled={disabled}
                value={row.value}
                placeholder={row.op === 'in' ? 'comma-separated' : kindOf(row.field) === 'money' ? '500.00' : ''}
                onChange={(e) => setRow(i, { value: e.target.value })}
              />
              {!disabled && draft.rows.length > 1 && (
                <button type="button" aria-label="Remove condition" onClick={() => onChange({ ...draft, rows: draft.rows.filter((_, j) => j !== i) })}>
                  ✕
                </button>
              )}
            </div>
          ))}
          {!disabled && (
            <button type="button" onClick={() => onChange({ ...draft, rows: [...draft.rows, blankRow()] })}>
              Add condition
            </button>
          )}
        </>
      )}

      {draft.mode === 'json' && (
        <textarea
          className="mono"
          disabled={disabled}
          rows={8}
          spellCheck={false}
          value={draft.text}
          onChange={(e) => onChange({ mode: 'json', text: e.target.value })}
        />
      )}
    </div>
  )

  function setRow(i: number, patch: Partial<Row>) {
    if (draft.mode !== 'rows') return
    onChange({ ...draft, rows: draft.rows.map((r, j) => (j === i ? { ...r, ...patch } : r)) })
  }
}
