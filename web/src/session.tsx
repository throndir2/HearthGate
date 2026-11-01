import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, setCsrf, type SessionUser, type Site } from './api'

// Owns: site settings and the signed-in user for the whole app. Call refresh() after login/logout.

interface SessionState {
  site: Site | null
  user: SessionUser | null
  loading: boolean
  refresh: () => Promise<void>
  can: (permission: string) => boolean
}

const Ctx = createContext<SessionState | null>(null)

export function SessionProvider({ children }: { children: ReactNode }) {
  const [site, setSite] = useState<Site | null>(null)
  const [user, setUser] = useState<SessionUser | null>(null)
  const [loading, setLoading] = useState(true)

  const refresh = useCallback(async () => {
    const s = await api.get<{ csrf: string; user: SessionUser | null }>('/api/session')
    setCsrf(s.csrf)
    setUser(s.user)
  }, [])

  useEffect(() => {
    Promise.all([api.get<Site>('/api/site').then(s => {
      setSite(s)
      document.title = s.title
      document.documentElement.style.setProperty('--accent', s.accentColor)
    }), refresh()]).finally(() => setLoading(false))
  }, [refresh])

  const can = useCallback((p: string) => !!user && (user.permissions.includes('admin') || user.permissions.includes(p)
    || (p.startsWith('games.control:') && user.permissions.includes('games.control:*'))), [user])

  return <Ctx.Provider value={{ site, user, loading, refresh, can }}>{children}</Ctx.Provider>
}

export function useSession() {
  const v = useContext(Ctx)
  if (!v) throw new Error('useSession outside provider')
  return v
}
