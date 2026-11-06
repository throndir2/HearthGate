// Owns: all HTTP calls to the HearthGate API. Adds the CSRF token to state-changing requests.

let csrfToken: string | null = null
export function setCsrf(token: string | null) { csrfToken = token }
export function getCsrf() { return csrfToken }

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) { super(message); this.status = status }
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (method !== 'GET' && csrfToken) headers['X-CSRF-TOKEN'] = csrfToken
  const res = await fetch(url, { method, headers, credentials: 'same-origin', body: body === undefined ? undefined : JSON.stringify(body) })
  const text = await res.text()
  const data = text ? JSON.parse(text) : null
  if (!res.ok) {
    const message = data?.error ?? (res.status === 401 ? 'Sign in first.' : res.status === 403 ? 'You do not have access to this.' : `Request failed (${res.status}).`)
    throw new ApiError(res.status, message)
  }
  return data as T
}

export const api = {
  get: <T,>(url: string) => request<T>('GET', url),
  post: <T,>(url: string, body?: unknown) => request<T>('POST', url, body ?? {}),
  put: <T,>(url: string, body?: unknown) => request<T>('PUT', url, body ?? {}),
  del: <T,>(url: string) => request<T>('DELETE', url),
}

// ---- Types ----
export type Status = 'up' | 'degraded' | 'down' | 'unknown'

export interface Site {
  title: string
  tagline: string
  accentColor: string
  logo?: string | null
  footerLinks: { label: string; url: string }[]
  categories: { id: string; name: string; icon?: string | null }[]
  auth: { localLogin: boolean; accountRequests: boolean; oidc: { displayName: string } | null }
  showProjects: boolean
  showGames: boolean
}

export interface SessionUser {
  id: number
  username: string
  isAdmin: boolean
  adminNeedsTotp: boolean
  totpEnabled: boolean
  hasPassword: boolean
  permissions: string[]
}

export interface Service {
  id: string
  name: string
  description?: string | null
  icon?: string | null
  category?: string | null
  url?: string | null
  status?: Status | null
  lastChecked?: string | null
  details?: {
    latencyMs?: number | null
    lastError?: string | null
    lastChange?: string | null
    uptime24h?: number | null
    history: { at: string; state: Status; latencyMs?: number | null }[]
  } | null
}

export interface Project {
  name: string
  description?: string | null
  language?: string | null
  stars: number
  forks: number
  updatedAt: string
  topics: string[]
  url: string
}

export interface GameServer {
  id: string
  name: string
  game?: string | null
  icon?: string | null
  connectInfo?: string | null
  state: 'running' | 'stopped' | 'starting' | 'stopping' | 'unknown'
  players?: number | null
  playerNames?: string[] | null
  version?: string | null
  updatedAt?: string | null
  canControl: boolean
}

export interface ContainerStats {
  cpuPercent?: number | null
  memoryBytes: number
  memoryLimitBytes?: number | null
  netRxBytesPerSec?: number | null
  netTxBytesPerSec?: number | null
  diskReadBytesPerSec?: number | null
  diskWriteBytesPerSec?: number | null
}

export interface ContainerView { name: string; image: string; state: string; status: string; health?: string | null; ports?: string[] | null; project?: string | null; stats?: ContainerStats | null }

export interface HostView {
  id: string
  name: string
  hasAgent: boolean
  online: boolean
  lastSeen?: string | null
  address?: string | null
  network?: {
    reachable?: boolean | null
    pingMs?: number | null
    pingError?: string | null
    ports: { port: number; open: boolean }[]
    checkedAt: string
    since?: string | null
  } | null
  snapshot?: {
    os: string
    cpuPercent?: number | null
    memoryTotalBytes?: number | null
    memoryUsedBytes?: number | null
    uptimeSeconds: number
    disks: { name: string; totalBytes: number; freeBytes: number }[]
    containers?: ContainerView[] | null
    dockerError?: string | null
  } | null
}

export function timeAgo(iso?: string | null) {
  if (!iso) return 'never'
  const s = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000)
  if (s < 60) return 'just now'
  if (s < 3600) return `${Math.floor(s / 60)} min ago`
  if (s < 86400) return `${Math.floor(s / 3600)} h ago`
  return `${Math.floor(s / 86400)} d ago`
}

export function bytes(n?: number | null) {
  if (n == null) return '–'
  const u = ['B', 'KB', 'MB', 'GB', 'TB']
  let i = 0
  while (n >= 1024 && i < u.length - 1) { n /= 1024; i++ }
  return `${n.toFixed(i > 2 ? 1 : 0)} ${u[i]}`
}
