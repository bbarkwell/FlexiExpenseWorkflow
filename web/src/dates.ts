/** The API stores UTC times and may or may not include the trailing Z. */
function parseUtc(value: string): Date {
  return new Date(/[zZ]|[+-]\d\d:\d\d$/.test(value) ? value : value + 'Z')
}

export const formatDate = (value: string) => parseUtc(value).toLocaleDateString()
export const formatDateTime = (value: string) => parseUtc(value).toLocaleString()
