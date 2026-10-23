import { centsToDollars, dollarsToCents } from './money'
import type { Rule, Step } from './types'

export type FieldName = 'totalCents' | 'lineCount' | 'department' | 'submitterRole' | 'category'
export type FieldKind = 'money' | 'number' | 'text'

export const FIELDS: { value: FieldName; label: string; kind: FieldKind }[] = [
  { value: 'totalCents', label: 'Report total ($)', kind: 'money' },
  { value: 'lineCount', label: 'Number of lines', kind: 'number' },
  { value: 'department', label: "Submitter's department", kind: 'text' },
  { value: 'submitterRole', label: "Submitter's role", kind: 'text' },
  { value: 'category', label: 'Expense category', kind: 'text' },
]

export const NUMERIC_OPS = [
  { value: 'lt', label: 'is less than' },
  { value: 'lte', label: 'is at most' },
  { value: 'eq', label: 'is exactly' },
  { value: 'neq', label: 'is not' },
  { value: 'gte', label: 'is at least' },
  { value: 'gt', label: 'is more than' },
  { value: 'in', label: 'is one of' },
]

export const TEXT_OPS = [
  { value: 'eq', label: 'is' },
  { value: 'neq', label: 'is not' },
  { value: 'in', label: 'is one of' },
]

export function kindOf(field: FieldName): FieldKind {
  return FIELDS.find((f) => f.value === field)!.kind
}

export function opsFor(field: FieldName) {
  return kindOf(field) === 'text' ? TEXT_OPS : NUMERIC_OPS
}

/** What the user is typing: dollars for money, comma-separated values for "is one of". */
export interface Row {
  field: FieldName
  op: string
  value: string
}

export type CondDraft =
  | { mode: 'always' }
  | { mode: 'rows'; join: 'all' | 'any'; rows: Row[] }
  | { mode: 'json'; text: string }

export interface RuleForm {
  name: string
  cond: CondDraft
  steps: Step[]
}

const isObj = (x: unknown): x is Record<string, unknown> => typeof x === 'object' && x !== null && !Array.isArray(x)

function rowFromComparison(node: unknown): Row | null {
  if (!isObj(node) || typeof node.field !== 'string' || typeof node.op !== 'string') return null
  const field = FIELDS.find((f) => f.value === node.field)
  if (!field || !opsFor(field.value).some((o) => o.value === node.op)) return null

  const raw = node.value
  const items = node.op === 'in' ? raw : [raw]
  if (!Array.isArray(items) || items.length === 0) return null
  const parts: string[] = []
  for (const item of items) {
    if (field.kind === 'text' && typeof item === 'string') parts.push(item)
    else if (field.kind === 'money' && typeof item === 'number' && Number.isInteger(item)) parts.push(centsToDollars(item))
    else if (field.kind === 'number' && typeof item === 'number' && Number.isInteger(item)) parts.push(String(item))
    else return null
  }
  return { field: field.value, op: node.op, value: parts.join(', ') }
}

/** Reads a stored condition into the friendly editor; anything the editor can't represent falls back to JSON. */
export function parseCondition(condition: unknown): CondDraft {
  if (condition === null || condition === undefined) return { mode: 'always' }
  const asJson = (): CondDraft => ({ mode: 'json', text: JSON.stringify(condition, null, 2) })

  if (isObj(condition)) {
    for (const join of ['all', 'any'] as const) {
      const children = condition[join]
      if (Array.isArray(children)) {
        const rows = children.map(rowFromComparison)
        return rows.length > 0 && rows.every((r) => r !== null) ? { mode: 'rows', join, rows: rows as Row[] } : asJson()
      }
    }
    const single = rowFromComparison(condition)
    if (single) return { mode: 'rows', join: 'all', rows: [single] }
  }
  return asJson()
}

function comparisonFromRow(row: Row, index: number): unknown {
  const kind = kindOf(row.field)
  const where = `Condition ${index + 1}`
  const parts = (row.op === 'in' ? row.value.split(',') : [row.value]).map((p) => p.trim())
  if (parts.some((p) => p === '')) throw new Error(`${where}: enter a value.`)

  const values = parts.map((p) => {
    if (kind === 'text') return p
    if (kind === 'money') {
      const cents = dollarsToCents(p)
      if (cents === null) throw new Error(`${where}: "${p}" isn't a valid dollar amount.`)
      return cents
    }
    if (!/^\d+$/.test(p)) throw new Error(`${where}: "${p}" isn't a whole number.`)
    return Number(p)
  })
  return { field: row.field, op: row.op, value: row.op === 'in' ? values : values[0] }
}

/** Throws an Error with a user-readable message if the draft isn't valid. */
export function buildCondition(draft: CondDraft): unknown | null {
  switch (draft.mode) {
    case 'always':
      return null
    case 'json':
      try {
        return JSON.parse(draft.text)
      } catch {
        throw new Error('The condition JSON is not valid.')
      }
    case 'rows': {
      if (draft.rows.length === 0) throw new Error('Add a condition, or choose "Always applies".')
      return { [draft.join]: draft.rows.map(comparisonFromRow) }
    }
  }
}

export function toRuleForm(rule: Rule): RuleForm {
  return {
    name: rule.name,
    cond: parseCondition(rule.condition),
    steps: rule.steps.map((s) => ({ approverType: s.approverType, approverParam: s.approverParam })),
  }
}

export function toRuleRequest(form: RuleForm, index: number) {
  try {
    return { name: form.name.trim(), condition: buildCondition(form.cond), steps: form.steps }
  } catch (e) {
    throw new Error(`Rule ${index + 1} ("${form.name || 'unnamed'}"): ${(e as Error).message}`)
  }
}
