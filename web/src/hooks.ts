import { useEffect, useState } from 'react'
import { api, type Project, type Service, type Status } from './api'

// Owns: data-loading hooks shared by pages (polling where useful).

export function useServices(pollMs = 30000) {
  const [data, setData] = useState<{ overall: Status; services: Service[] } | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    let alive = true
    const load = () => api.get<{ overall: Status; services: Service[] }>('/api/services')
      .then(d => { if (alive) { setData(d); setError(null) } })
      .catch(e => alive && setError(e.message))
    load()
    const t = setInterval(() => { if (document.visibilityState === 'visible') load() }, pollMs)
    return () => { alive = false; clearInterval(t) }
  }, [pollMs])
  return { data, error }
}

export function useProjects(enabled: boolean) {
  const [projects, setProjects] = useState<Project[] | null>(null)
  useEffect(() => {
    if (!enabled) return
    api.get<Project[]>('/api/projects').then(setProjects).catch(() => setProjects([]))
  }, [enabled])
  return projects
}

export function usePoll<T>(url: string | null, pollMs = 15000) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)
  useEffect(() => {
    if (!url) return
    let alive = true
    const load = () => api.get<T>(url).then(d => { if (alive) { setData(d); setError(null) } }).catch(e => alive && setError(e.message))
    load()
    const t = setInterval(() => { if (document.visibilityState === 'visible') load() }, pollMs)
    return () => { alive = false; clearInterval(t) }
  }, [url, pollMs, tick])
  return { data, error, reload: () => setTick(t => t + 1) }
}
