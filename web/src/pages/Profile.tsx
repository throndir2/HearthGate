import { useEffect, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router'
import QRCode from 'qrcode'
import { KeyRound, Link2, ShieldCheck, Trash2 } from 'lucide-react'
import { api, getCsrf, timeAgo } from '../api'
import { useSession } from '../session'
import { Message } from '../components/AuthCard'

const linkMessages: Record<string, [kind: 'ok' | 'error' | 'info', text: string]> = {
  ok: ['ok', 'The account is linked. You can now sign in with it.'],
  already: ['info', 'This external account is already linked to you.'],
  taken: ['error', 'This external account is linked to a different user.'],
  failed: ['error', 'The link did not work. Try again.'],
}

function Section({ title, icon, children }: { title: string; icon: React.ReactNode; children: React.ReactNode }) {
  return (
    <section className="glass rounded-2xl p-5 sm:p-6">
      <h2 className="mb-4 flex items-center gap-2 text-lg font-semibold"><span className="text-accent">{icon}</span>{title}</h2>
      {children}
    </section>
  )
}

export default function Profile() {
  const { user, site, refresh } = useSession()
  const [params] = useSearchParams()
  const link = params.get('link')
  if (!user) return <p className="text-center text-stone-500">Sign in first.</p>
  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div>
        <h1 className="text-3xl font-bold tracking-tight">{user.username}</h1>
        <p className="text-stone-500">{user.isAdmin ? 'Admin' : 'Member'} · {user.permissions.length ? user.permissions.join(', ') : 'basic access'}</p>
      </div>
      {link && linkMessages[link] && <Message kind={linkMessages[link][0]}>{linkMessages[link][1]}</Message>}
      {user.adminNeedsTotp && <Message kind="info">Turn on two-step sign-in below to use the admin tools.</Message>}
      <PasswordSection hasPassword={user.hasPassword} username={user.username} onDone={refresh} />
      <TotpSection enabled={user.totpEnabled} onDone={refresh} />
      {site?.auth.oidc && <LinksSection provider={site.auth.oidc.displayName} />}
    </div>
  )
}

function PasswordSection({ hasPassword, username, onDone }: { hasPassword: boolean; username: string; onDone: () => Promise<void> }) {
  const [current, setCurrent] = useState('')
  const [password, setPassword] = useState('')
  const [msg, setMsg] = useState<[string, string] | null>(null)
  async function submit(e: FormEvent) {
    e.preventDefault()
    try { await api.post('/api/me/password', { current, password }); setMsg(['ok', 'Password changed. Other sessions are signed out.']); setCurrent(''); setPassword(''); await onDone() }
    catch (err) { setMsg(['error', (err as Error).message]) }
  }
  return (
    <Section title="Password" icon={<KeyRound className="size-5" />}>
      {msg && <Message kind={msg[0] as 'ok' | 'error'}>{msg[1]}</Message>}
      <form onSubmit={submit} className="grid gap-3 sm:grid-cols-2">
        <input type="text" name="username" autoComplete="username" value={username} readOnly hidden />
        {hasPassword && <input type="password" className="input" placeholder="Current password" autoComplete="current-password" value={current} onChange={e => setCurrent(e.target.value)} required />}
        <input type="password" className="input" placeholder="New password (12+ characters)" autoComplete="new-password" minLength={12} value={password} onChange={e => setPassword(e.target.value)} required />
        <button className="btn-primary sm:col-span-2 sm:justify-self-start">{hasPassword ? 'Change password' : 'Set password'}</button>
      </form>
    </Section>
  )
}

function TotpSection({ enabled, onDone }: { enabled: boolean; onDone: () => Promise<void> }) {
  const [setup, setSetup] = useState<{ secret: string; uri: string; qr: string } | null>(null)
  const [code, setCode] = useState('')
  const [msg, setMsg] = useState<[string, string] | null>(null)
  async function start() {
    try {
      const r = await api.post<{ secret: string; uri: string }>('/api/me/totp/setup')
      setSetup({ ...r, qr: await QRCode.toDataURL(r.uri, { margin: 1, width: 200 }) })
    } catch (err) { setMsg(['error', (err as Error).message]) }
  }
  async function confirm(e: FormEvent) {
    e.preventDefault()
    try {
      await api.post(enabled ? '/api/me/totp/disable' : '/api/me/totp/enable', { code })
      setMsg(['ok', enabled ? 'Two-step sign-in is off.' : 'Two-step sign-in is on.']); setSetup(null); setCode(''); await onDone()
    } catch (err) { setMsg(['error', (err as Error).message]) }
  }
  return (
    <Section title="Two-step sign-in (TOTP)" icon={<ShieldCheck className="size-5" />}>
      {msg && <Message kind={msg[0] as 'ok' | 'error'}>{msg[1]}</Message>}
      <p className="mb-4 text-sm text-stone-500">{enabled ? 'On. You need a code from your authenticator app when you sign in.' : 'Off. Use an authenticator app (for example Aegis, 2FAS, Google Authenticator or 1Password).'}</p>
      {!enabled && !setup && <button className="btn-primary" onClick={start}>Set up</button>}
      {setup && (
        <div className="mb-4 flex flex-col items-center gap-4 sm:flex-row sm:items-start">
          <img src={setup.qr} alt="QR code for the authenticator app" className="size-44 rounded-xl bg-white p-2" />
          <div className="text-sm">
            <p>1. Scan the QR code with your app.</p>
            <p className="mt-1">Or type this key: <code className="break-all rounded bg-black/5 px-1.5 py-0.5 dark:bg-white/10">{setup.secret}</code></p>
            <p className="mt-1">2. Type the 6-digit code from the app below.</p>
          </div>
        </div>
      )}
      {(setup || enabled) && (
        <form onSubmit={confirm} className="flex gap-2">
          <input className="input max-w-40 text-center tracking-widest" inputMode="numeric" autoComplete="one-time-code" maxLength={7} placeholder="123456" value={code} onChange={e => setCode(e.target.value)} required />
          <button className={enabled ? 'btn-danger' : 'btn-primary'}>{enabled ? 'Turn off' : 'Turn on'}</button>
        </form>
      )}
    </Section>
  )
}

function LinksSection({ provider }: { provider: string }) {
  const [links, setLinks] = useState<{ id: number; provider: string; displayName?: string; linkedAt: string }[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const load = () => api.get<typeof links>('/api/me/links').then(setLinks).catch(e => setError(e.message))
  useEffect(() => { load() }, [])
  async function unlink(id: number) {
    try { await api.del(`/api/me/links/${id}`); load() } catch (e) { setError((e as Error).message) }
  }
  return (
    <Section title="Linked accounts" icon={<Link2 className="size-5" />}>
      {error && <Message kind="error">{error}</Message>}
      <p className="mb-4 text-sm text-stone-500">Link your {provider} account. Then you can sign in with {provider} or with your password. Your permissions stay the same.</p>
      <ul className="mb-4 space-y-2">
        {links?.map(l => (
          <li key={l.id} className="flex items-center justify-between rounded-xl bg-black/5 px-3 py-2 text-sm dark:bg-white/5">
            <span><b>{l.provider}</b> · {l.displayName} · linked {timeAgo(l.linkedAt)}</span>
            <button className="btn-ghost !px-2 !min-h-9" onClick={() => unlink(l.id)} aria-label={`Unlink ${l.provider}`}><Trash2 className="size-4" /></button>
          </li>
        ))}
      </ul>
      {/* A real form POST with the antiforgery field, because the server redirects to the provider. */}
      <form method="post" action="/api/auth/oidc/link">
        <input type="hidden" name="__RequestVerificationToken" value={getCsrf() ?? ''} />
        <button className="btn-outline">Link {provider}</button>
      </form>
    </Section>
  )
}
