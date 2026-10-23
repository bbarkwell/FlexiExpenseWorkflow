import { describe, expect, it } from 'vitest'
import { buildCondition, parseCondition, toRuleForm, toRuleRequest } from './conditions'
import { centsToDollars, dollarsToCents } from './money'

describe('money', () => {
  it('converts dollars to cents without floating-point error', () => {
    expect(dollarsToCents('500')).toBe(50000)
    expect(dollarsToCents('19.99')).toBe(1999)
    expect(dollarsToCents('0.1')).toBe(10)
    expect(dollarsToCents('4999.99')).toBe(499999)
    expect(dollarsToCents(' 5,000 ')).toBeNull()
    expect(dollarsToCents('12.345')).toBeNull()
    expect(dollarsToCents('-1')).toBeNull()
    expect(dollarsToCents('')).toBeNull()
  })

  it('round-trips through the input format', () => {
    for (const cents of [0, 1, 99, 1999, 50000, 500001]) {
      expect(dollarsToCents(centsToDollars(cents))).toBe(cents)
    }
  })
})

describe('conditions', () => {
  const middleBand = {
    all: [
      { field: 'totalCents', op: 'gte', value: 50000 },
      { field: 'totalCents', op: 'lte', value: 500000 },
    ],
  }

  it('shows the seeded $500-$5,000 rule as dollars', () => {
    expect(parseCondition(middleBand)).toEqual({
      mode: 'rows',
      join: 'all',
      rows: [
        { field: 'totalCents', op: 'gte', value: '500' },
        { field: 'totalCents', op: 'lte', value: '5000' },
      ],
    })
  })

  it('round-trips the editor form back to the exact stored JSON', () => {
    expect(buildCondition(parseCondition(middleBand))).toEqual(middleBand)
  })

  it('treats no condition as always applies', () => {
    expect(parseCondition(null)).toEqual({ mode: 'always' })
    expect(buildCondition({ mode: 'always' })).toBeNull()
  })

  it('reads a single comparison and "is one of" lists', () => {
    const draft = parseCondition({ field: 'department', op: 'in', value: ['Sales', 'Ops'] })
    expect(draft).toEqual({ mode: 'rows', join: 'all', rows: [{ field: 'department', op: 'in', value: 'Sales, Ops' }] })
    expect(buildCondition(draft)).toEqual({ all: [{ field: 'department', op: 'in', value: ['Sales', 'Ops'] }] })
  })

  it('falls back to JSON for nested trees the editor cannot show', () => {
    const nested = { all: [{ any: [{ field: 'department', op: 'eq', value: 'Sales' }] }] }
    expect(parseCondition(nested).mode).toBe('json')
    expect(buildCondition(parseCondition(nested))).toEqual(nested)
  })

  it('falls back to JSON for unknown fields or operators', () => {
    expect(parseCondition({ field: 'mood', op: 'eq', value: 'x' }).mode).toBe('json')
    expect(parseCondition({ field: 'totalCents', op: 'contains', value: 1 }).mode).toBe('json')
  })

  it('reports readable errors for bad input', () => {
    const row = (value: string) => ({ mode: 'rows' as const, join: 'all' as const, rows: [{ field: 'totalCents' as const, op: 'gt', value }] })
    expect(() => buildCondition(row('abc'))).toThrow(/valid dollar amount/)
    expect(() => buildCondition(row(''))).toThrow(/enter a value/)
    expect(() => buildCondition({ mode: 'rows', join: 'all', rows: [] })).toThrow(/Add a condition/)
    expect(() => buildCondition({ mode: 'json', text: '{nope' })).toThrow(/not valid/)
    expect(() => buildCondition({ mode: 'rows', join: 'all', rows: [{ field: 'lineCount', op: 'gt', value: '1.5' }] })).toThrow(/whole number/)
  })

  it('names the rule when a request cannot be built', () => {
    const form = toRuleForm({ name: 'Big', condition: null, steps: [] })
    form.cond = { mode: 'json', text: 'x' }
    expect(() => toRuleRequest(form, 2)).toThrow(/Rule 3 \("Big"\)/)
  })
})
