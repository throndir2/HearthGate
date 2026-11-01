import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { KeyRound } from 'lucide-react'
import { api } from '../api'
import { useSession } from '../session'
import { AuthCard, Message } from '../components/AuthCard'

const errors: Record<string, string> = {
  oidc: 'The sign-in with the external provider did not work. Try again.',
  not_linked: 'This external account is not linked to an account here. Sign in with your password, then link it on your profile page.',
  disabled: 'This account is disabled.',
}

export default function SignIn() {
  const { site, refresh } = useSession()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [totp, setTotp] = useState('')
  const [needsTotp, setNeedsTotp] = useState(false)
  const [error, setError] = useState<string | null>(params.get('error') ? errors[params.get('error')!] ?? 'Sign-in failed.' : null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true); setError(null)
    try {
      const r = await api.post<{ needsTotp?: boolean }>('/api/auth/login', { username, password, totp: needsTotp ? totp : null })
      if (r.needsTotp) { setNeedsTotp(true); return }
      await refresh()
      navigate('/')
    } catch (err) {
      setError((err as Error).message)
      if (needsTotp) setTotp('')
    } finally { setBusy(false) }
  }

  return (
    <AuthCard title="Sign in" subtitle="For approved members only.">
      {params.get('notice') === 'requested' && <Message kind="info">Your request was sent. An admin will check it.</Message>}
      {error && <Message kind="error">{error}</Message>}
      {site?.auth.localLogin && (
        <form onSubmit={submit} className="space-y-4">
          {!needsTotp ? (<>
            <div><label className="label" htmlFor="u">Username</label>
              <input id="u" className="input" autoComplete="username" value={username} onChange={e => setUsername(e.target.value)} required autoFocus /></div>
            <div><label className="label" htmlFor="p">Password</label>
              <input id="p" type="password" className="input" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} required /></div>
          </>) : (
            <div><label className="label" htmlFor="t">Code from your authenticator app</label>
              <input id="t" className="input text-center text-lg tracking-[0.4em]" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9 ]*" maxLength={7}
                value={totp} onChange={e => setTotp(e.target.value)} required autoFocus /></div>
          )}
          <button className="btn-primary w-full" disabled={busy}>{busy ? 'Signing in…' : needsTotp ? 'Verify' : 'Sign in'}</button>
        </form>
      )}
      {site?.auth.oidc && (<>
        {site.auth.localLogin && <div className="my-5 flex items-center gap-3 text-xs text-stone-500"><span className="h-px flex-1 bg-black/10 dark:bg-white/10" />or<span className="h-px flex-1 bg-black/10 dark:bg-white/10" /></div>}
        <a href="/api/auth/oidc/login" className="btn-outline w-full"><KeyRound className="size-4" />Sign in with {site.auth.oidc.displayName}</a>
      </>)}
      {site?.auth.accountRequests && <p className="mt-6 text-center text-sm text-stone-500">No account? <Link to="/request-access" className="font-medium text-accent hover:underline">Request one</Link></p>}
    </AuthCard>
  )
}
