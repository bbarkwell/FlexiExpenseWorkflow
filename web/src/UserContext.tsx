import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, setApiUser } from './api'
import type { UserSummary } from './types'

interface UserContextValue {
  users: UserSummary[]
  me: UserSummary
  isAdmin: boolean
  switchUser: (id: number) => void
}

const UserContext = createContext<UserContextValue | null>(null)
const STORAGE_KEY = 'expenseworkflow.userId'

function readStoredId(): number | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? Number(raw) : null
  } catch {
    return null
  }
}

/**
 * Development stand-in for sign-in: loads the org chart and lets you act as any user.
 * Replace with real authentication later; everything else reads the user from useMe().
 */
export function UserProvider({ children }: { children: ReactNode }) {
  const [users, setUsers] = useState<UserSummary[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [meId, setMeId] = useState<number | null>(readStoredId)

  useEffect(() => {
    api.get<UserSummary[]>('/users').then(setUsers, (e: Error) => setError(e.message))
  }, [])

  const me = users?.find((u) => u.id === meId) ?? users?.[0]
  const value = useMemo<UserContextValue | null>(() => {
    if (!users || !me) return null
    return {
      users,
      me,
      isAdmin: me.roles.includes('Admin'),
      switchUser: (id) => {
        try {
          localStorage.setItem(STORAGE_KEY, String(id))
        } catch {
          /* private mode: the choice just won't persist */
        }
        setMeId(id)
      },
    }
  }, [users, me])

  // Set before children render so their first requests already carry the user.
  setApiUser(me?.id ?? null)

  if (error) return <p className="error page">Could not load the org chart: {error}</p>
  if (!value) return <p className="muted page">Loading…</p>
  return <UserContext.Provider value={value}>{children}</UserContext.Provider>
}

export function useMe(): UserContextValue {
  const ctx = useContext(UserContext)
  if (!ctx) throw new Error('useMe must be used inside UserProvider')
  return ctx
}
