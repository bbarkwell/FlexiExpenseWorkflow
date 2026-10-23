const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

export function formatCents(cents: number): string {
  return usd.format(cents / 100)
}

/** Parses "12", "12.5" or "12.50" into cents without floating-point error. Returns null if it isn't a valid amount. */
export function dollarsToCents(input: string): number | null {
  const m = /^\s*(\d+)(?:\.(\d{1,2}))?\s*$/.exec(input)
  if (!m) return null
  return Number(m[1]) * 100 + Number((m[2] ?? '').padEnd(2, '0') || '0')
}

/** Inverse of dollarsToCents, for filling an input: 50000 -> "500", 1999 -> "19.99". */
export function centsToDollars(cents: number): string {
  return cents % 100 === 0 ? String(cents / 100) : (cents / 100).toFixed(2)
}
