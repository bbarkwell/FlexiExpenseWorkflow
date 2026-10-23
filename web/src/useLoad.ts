import { useCallback, useEffect, useState } from 'react'
import { ApiError } from './api'

interface LoadState<T> {
  data: T | undefined
  error: ApiError | null
  loading: boolean
}

/** Runs `load` on mount and whenever `deps` change. `reload()` refetches while keeping the old data on screen. */
export function useLoad<T>(load: () => Promise<T>, deps: unknown[]) {
  const [state, setState] = useState<LoadState<T>>({ data: undefined, error: null, loading: true })
  const [tick, setTick] = useState(0)

  useEffect(() => {
    let cancelled = false
    load().then(
      (data) => !cancelled && setState({ data, error: null, loading: false }),
      (e) => !cancelled && setState((s) => ({ ...s, error: e as ApiError, loading: false })),
    )
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick])

  const reload = useCallback(() => setTick((t) => t + 1), [])
  return { ...state, reload }
}
