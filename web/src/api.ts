const API = import.meta.env.VITE_API_URL as string

// Development stand-in for authentication: the API reads the acting user from this header.
let currentUserId: number | null = null

export function setApiUser(id: number | null) {
  currentUserId = id
}

export class ApiError extends Error {
  status: number
  errors: string[]
  warnings: string[]

  constructor(status: number, message: string, errors: string[] = [], warnings: string[] = []) {
    super(message)
    this.status = status
    this.errors = errors
    this.warnings = warnings
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {}
  if (currentUserId !== null) headers['X-User-Id'] = String(currentUserId)
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  let res: Response
  try {
    res = await fetch(`${API}/api${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
  } catch {
    throw new ApiError(0, `Could not reach the API at ${API}. Is it running?`)
  }

  if (res.ok) {
    return (res.status === 204 ? undefined : await res.json()) as T
  }

  let message = `Request failed (${res.status})`
  let errors: string[] = []
  let warnings: string[] = []
  const text = await res.text()
  try {
    const problem = JSON.parse(text)
    if (typeof problem.detail === 'string') message = problem.detail
    else if (problem.errors && !Array.isArray(problem.errors)) {
      // ASP.NET model validation: { errors: { Field: ["msg"] } }
      errors = Object.values(problem.errors as Record<string, string[]>).flat()
      message = 'Please fix the highlighted problems.'
    }
    if (Array.isArray(problem.errors)) errors = problem.errors
    if (Array.isArray(problem.warnings)) warnings = problem.warnings
  } catch {
    if (text) message = text
  }
  throw new ApiError(res.status, message, errors, warnings)
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  put: <T>(path: string, body: unknown) => request<T>('PUT', path, body),
  del: (path: string) => request<void>('DELETE', path),
}
