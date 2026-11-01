import { useEffect, useState } from 'react'
import { motion } from 'motion/react'
import { Check, Copy, KeyRound, LogOut, Plus, ShieldOff, Trash2, X } from 'lucide-react'
import { api, timeAgo } from '../api'
import { useSession } from '../session'
import { Message } from '../components/AuthCard'

// Owns: the admin screen (requests, users, groups, audit log, config overview). Server enforces admin access.

type Tab = 'requests' | 'users' | 'groups' | 'audit' | 'config'
interface Req { id: number; username: string; email?: string; message?: string; status: string; createdAt: string; external: boolean }
interface AdminUser { id: number; username: string; email?: string; isAdmin: boolean; disabled: boolean; totpEnabled: boolean; hasPassword: boolean; locked: boolean; createdAt: string; lastLoginAt?: string; groupIds: number[]; links: string[] }
interface Group { id: number; name: string; permissions: string; members: number }
interface Audit { id: number; at: string; action: string; actorName?: string; target?: string; detail?: string; ip?: string; success: boolean }

function useLoad<T>(url: string) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const load = () => api.get<T>(url).then(setData).catch(e => setError(e.message))
  useEffect(() => { load() }, [url]) // eslint-disable-line react-hooks/exhaustive-deps
  return { data, error, load }
}

function LinkBox({ path, onClose }: { path: string; onClose: () => void }) {
  const url = window.location.origin + path
  const [copied, setCopied] = useState(false)
  return (
    <Message kind="ok">
      <p className="font-medium">Send this one-time link to the user. It works for 72 hours.</p>
      <div className="mt-2 flex gap-2">
        <input readOnly className="input !min-h-9 !py-1.5 font-mono text-xs" value={url} onFocus={e => e.target.select()} />
        <button className="btn-outline !min-h-9" onClick={() => { navigator.clipboard.writeText(url); setCopied(true) }}>{copied ? <Check className="size-4" /> : <Copy className="size-4" />}</button>
        <button className="btn-ghost !min-h-9" onClick={onClose} aria-label="Close"><X className="size-4" /></button>
      </div>
    </Message>
  )
}

export default function Admin() {
  const { user } = useSession()
  const [tab, setTab] = useState<Tab>('requests')
  if (!user?.isAdmin) return <p className="text-center text-stone-500">Admins only.</p>
  if (user.adminNeedsTotp) return <Message kind="info">Turn on two-step sign-in on your profile page to use the admin tools.</Message>
  const tabs: [Tab, string][] = [['requests', 'Requests'], ['users', 'Users'], ['groups', 'Groups'], ['audit', 'Audit log'], ['config', 'Config']]
  return (
    <div className="space-y-6">
      <h1 className="text-3xl font-bold tracking-tight">Admin</h1>
      <div className="glass inline-flex flex-wrap gap-1 rounded-2xl p-1" role="tablist">
        {tabs.map(([id, label]) => (
          <button key={id} role="tab" aria-selected={tab === id} onClick={() => setTab(id)}
            className={`relative rounded-xl px-4 py-2 text-sm font-medium min-h-10 ${tab === id ? 'text-white' : 'text-stone-500 hover:text-stone-900 dark:hover:text-white'}`}>
            {tab === id && <motion.span layoutId="admin-tab" className="absolute inset-0 -z-10 rounded-xl bg-accent" />}
            {label}
          </button>
        ))}
      </div>
      {tab === 'requests' && <Requests />}
      {tab === 'users' && <Users />}
      {tab === 'groups' && <Groups />}
      {tab === 'audit' && <AuditLog />}
      {tab === 'config' && <ConfigView />}
    </div>
  )
}

function Requests() {
  const { data, error, load } = useLoad<Req[]>('/api/admin/requests')
  const [link, setLink] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)
  async function decide(r: Req, approve: boolean) {
    setErr(null)
    try {
      const res = await api.post<{ setPasswordPath?: string }>(`/api/admin/requests/${r.id}/${approve ? 'approve' : 'reject'}`)
      if (res.setPasswordPath && !r.external) setLink(res.setPasswordPath)
      load()
    } catch (e) { setErr((e as Error).message) }
  }
  const pending = data?.filter(r => r.status === 'pending') ?? []
  const done = data?.filter(r => r.status !== 'pending') ?? []
  return (
    <div className="space-y-4">
      {(error || err) && <Message kind="error">{error || err}</Message>}
      {link && <LinkBox path={link} onClose={() => setLink(null)} />}
      {pending.length === 0 && <p className="text-stone-500">No open requests.</p>}
      {pending.map(r => (
        <div key={r.id} className="glass flex flex-col gap-3 rounded-2xl p-4 sm:flex-row sm:items-center">
          <div className="flex-1">
            <div className="font-semibold">{r.username} {r.external && <span className="ml-1 rounded bg-sky-500/15 px-1.5 py-0.5 text-xs text-sky-600">external</span>}</div>
            <div className="text-sm text-stone-500">{r.email ?? 'no email'} · {timeAgo(r.createdAt)}</div>
            {r.message && <p className="mt-1 text-sm">“{r.message}”</p>}
          </div>
          <div className="flex gap-2">
            <button className="btn-primary" onClick={() => decide(r, true)}><Check className="size-4" />Approve</button>
            <button className="btn-outline" onClick={() => decide(r, false)}><X className="size-4" />Reject</button>
          </div>
        </div>
      ))}
      {done.length > 0 && <details className="text-sm text-stone-500"><summary className="cursor-pointer">Decided ({done.length})</summary>
        <ul className="mt-2 space-y-1">{done.map(r => <li key={r.id}>{r.username} · {r.status}</li>)}</ul></details>}
    </div>
  )
}

function Users() {
  const { user: me } = useSession()
  const { data, error, load } = useLoad<AdminUser[]>('/api/admin/users')
  const groups = useLoad<{ groups: Group[] }>('/api/admin/groups')
  const [link, setLink] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)
  async function run(fn: () => Promise<unknown>) { setErr(null); try { await fn(); load() } catch (e) { setErr((e as Error).message) } }
  const update = (u: AdminUser, body: object) => run(() => api.put(`/api/admin/users/${u.id}`, body))
  return (
    <div className="space-y-3">
      {(error || err) && <Message kind="error">{error || err}</Message>}
      {link && <LinkBox path={link} onClose={() => setLink(null)} />}
      {data?.map(u => (
        <div key={u.id} className="glass rounded-2xl p-4">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-semibold">{u.username}</span>
            {u.isAdmin && <span className="rounded bg-accent/15 px-1.5 py-0.5 text-xs text-accent">admin</span>}
            {u.disabled && <span className="rounded bg-red-500/15 px-1.5 py-0.5 text-xs text-red-500">disabled</span>}
            {u.locked && <span className="rounded bg-amber-500/15 px-1.5 py-0.5 text-xs text-amber-600">locked</span>}
            {u.totpEnabled && <span className="rounded bg-emerald-500/15 px-1.5 py-0.5 text-xs text-emerald-600">2FA</span>}
            {!u.hasPassword && <span className="rounded bg-stone-500/15 px-1.5 py-0.5 text-xs">no password</span>}
            {u.links.map(l => <span key={l} className="rounded bg-sky-500/15 px-1.5 py-0.5 text-xs text-sky-600">{l}</span>)}
            <span className="ml-auto text-xs text-stone-500">last sign-in {timeAgo(u.lastLoginAt)}</span>
          </div>
          <div className="mt-3 flex flex-wrap gap-1.5">
            {groups.data?.groups.map(g => {
              const on = u.groupIds.includes(g.id)
              return <button key={g.id} onClick={() => update(u, { groupIds: on ? u.groupIds.filter(x => x !== g.id) : [...u.groupIds, g.id] })}
                className={`rounded-full px-3 py-1 text-xs font-medium ring-1 min-h-8 transition ${on ? 'bg-accent text-white ring-accent' : 'ring-black/10 hover:bg-black/5 dark:ring-white/15 dark:hover:bg-white/10'}`}>{g.name}</button>
            })}
          </div>
          <div className="mt-3 flex flex-wrap gap-2 text-sm">
            <button className="btn-ghost !min-h-9" onClick={() => run(async () => setLink((await api.post<{ setPasswordPath: string }>(`/api/admin/users/${u.id}/reset-link`)).setPasswordPath))}><KeyRound className="size-4" />Password link</button>
            <button className="btn-ghost !min-h-9" onClick={() => run(() => api.post(`/api/admin/users/${u.id}/revoke-sessions`))}><LogOut className="size-4" />Sign out everywhere</button>
            {u.id !== me?.id && (<>
              {u.totpEnabled && <button className="btn-ghost !min-h-9" onClick={() => confirm(`Reset 2FA for ${u.username}?`) && run(() => api.post(`/api/admin/users/${u.id}/reset-totp`))}><ShieldOff className="size-4" />Reset 2FA</button>}
              <button className="btn-ghost !min-h-9" onClick={() => update(u, { isAdmin: !u.isAdmin })}>{u.isAdmin ? 'Remove admin' : 'Make admin'}</button>
              <button className="btn-ghost !min-h-9 text-red-500" onClick={() => update(u, { disabled: !u.disabled })}>{u.disabled ? 'Enable' : 'Disable'}</button>
            </>)}
          </div>
        </div>
      ))}
    </div>
  )
}

function Groups() {
  const { data, error, load } = useLoad<{ catalog: string[]; groups: Group[] }>('/api/admin/groups')
  const [name, setName] = useState('')
  const [err, setErr] = useState<string | null>(null)
  async function run(fn: () => Promise<unknown>) { setErr(null); try { await fn(); load() } catch (e) { setErr((e as Error).message) } }
  const toggle = (g: Group, p: string) => {
    const perms = g.permissions.split(' ').filter(Boolean)
    run(() => api.put(`/api/admin/groups/${g.id}`, { permissions: perms.includes(p) ? perms.filter(x => x !== p) : [...perms, p] }))
  }
  return (
    <div className="space-y-3">
      {(error || err) && <Message kind="error">{error || err}</Message>}
      <p className="text-sm text-stone-500">To allow control of one game server only, add a permission like <code>games.control:palworld</code> with the field below each group.</p>
      {data?.groups.map(g => (
        <div key={g.id} className="glass rounded-2xl p-4">
          <div className="flex items-center gap-2">
            <span className="font-semibold">{g.name}</span><span className="text-xs text-stone-500">{g.members} members</span>
            <button className="btn-ghost !min-h-9 ml-auto" onClick={() => confirm(`Delete group ${g.name}?`) && run(() => api.del(`/api/admin/groups/${g.id}`))} aria-label={`Delete ${g.name}`}><Trash2 className="size-4" /></button>
          </div>
          <div className="mt-2 flex flex-wrap gap-1.5">
            {[...new Set([...data.catalog, ...g.permissions.split(' ').filter(Boolean)])].map(p => {
              const on = g.permissions.split(' ').includes(p)
              return <button key={p} onClick={() => toggle(g, p)} className={`rounded-full px-3 py-1 font-mono text-xs ring-1 min-h-8 ${on ? 'bg-accent text-white ring-accent' : 'ring-black/10 dark:ring-white/15'}`}>{p}</button>
            })}
          </div>
          <form className="mt-2 flex gap-2" onSubmit={e => { e.preventDefault(); const v = (e.currentTarget.elements.namedItem('p') as HTMLInputElement); toggle(g, v.value.trim()); v.value = '' }}>
            <input name="p" className="input !min-h-9 !py-1.5 max-w-64 font-mono text-xs" placeholder="games.control:<id>" pattern="games\.control:[A-Za-z0-9_\-]+" />
            <button className="btn-outline !min-h-9"><Plus className="size-4" /></button>
          </form>
        </div>
      ))}
      <form className="flex gap-2" onSubmit={e => { e.preventDefault(); run(() => api.post('/api/admin/groups', { name, permissions: [] })).then(() => setName('')) }}>
        <input className="input max-w-64" placeholder="New group name" value={name} onChange={e => setName(e.target.value)} required maxLength={40} />
        <button className="btn-primary"><Plus className="size-4" />Add group</button>
      </form>
    </div>
  )
}

function AuditLog() {
  const [items, setItems] = useState<Audit[]>([])
  const [error, setError] = useState<string | null>(null)
  const [end, setEnd] = useState(false)
  const more = (before?: number) => api.get<Audit[]>(`/api/admin/audit${before ? `?before=${before}` : ''}`)
    .then(r => { setItems(x => before ? [...x, ...r] : r); setEnd(r.length < 100) }).catch(e => setError(e.message))
  useEffect(() => { more() }, [])
  return (
    <div className="glass overflow-x-auto rounded-2xl">
      {error && <Message kind="error">{error}</Message>}
      <table className="w-full text-left text-sm">
        <thead className="text-xs uppercase text-stone-500"><tr><th className="p-3">When</th><th className="p-3">Action</th><th className="p-3">Who</th><th className="p-3">Target</th><th className="p-3">IP</th></tr></thead>
        <tbody className="divide-y divide-black/5 dark:divide-white/10">
          {items.map(a => (
            <tr key={a.id} className={a.success ? '' : 'text-red-500'}>
              <td className="whitespace-nowrap p-3" title={new Date(a.at).toLocaleString()}>{timeAgo(a.at)}</td>
              <td className="p-3 font-mono text-xs">{a.action}{!a.success && ' ✗'}</td>
              <td className="p-3">{a.actorName ?? '–'}</td>
              <td className="p-3">{a.target}{a.detail && <span className="block text-xs text-stone-500">{a.detail}</span>}</td>
              <td className="p-3 font-mono text-xs">{a.ip}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {!end && items.length > 0 && <button className="btn-ghost m-2" onClick={() => more(items[items.length - 1].id)}>Load more</button>}
    </div>
  )
}

function ConfigView() {
  const { data, error } = useLoad<Record<string, unknown>>('/api/admin/config')
  return (
    <div className="glass rounded-2xl p-4">
      {error && <Message kind="error">{error}</Message>}
      <p className="mb-3 text-sm text-stone-500">Edit <code>hearthgate.yaml</code> on the server. HearthGate reloads it automatically. Secrets stay in environment variables.</p>
      <pre className="max-h-[60vh] overflow-auto rounded-xl bg-black/5 p-3 text-xs dark:bg-black/30">{JSON.stringify(data, null, 2)}</pre>
    </div>
  )
}
