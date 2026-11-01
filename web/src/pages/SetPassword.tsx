import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { api } from '../api'
import { AuthCard, Message } from '../components/AuthCard'

export default function SetPassword() {
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (password !== confirm) { setError('The two passwords are not the same.'); return }
    setBusy(true); setError(null)
    try { await api.post('/api/auth/set-password', { token, password }); setDone(true) }
    catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }

  if (done) return <AuthCard title="Password set"><Message kind="ok">Your password is set. You can sign in now.</Message><Link to="/signin" className="btn-primary w-full">Sign in</Link></AuthCard>
  return (
    <AuthCard title="Set your password" subtitle="Use at least 12 characters. A password manager helps.">
      {!token && <Message kind="error">This link has no token. Ask the admin for a new link.</Message>}
      {error && <Message kind="error">{error}</Message>}
      <form onSubmit={submit} className="space-y-4">
        <div><label className="label" htmlFor="np">New password</label>
          <input id="np" type="password" className="input" autoComplete="new-password" minLength={12} value={password} onChange={e => setPassword(e.target.value)} required /></div>
        <div><label className="label" htmlFor="cp">Type it again</label>
          <input id="cp" type="password" className="input" autoComplete="new-password" minLength={12} value={confirm} onChange={e => setConfirm(e.target.value)} required /></div>
        <button className="btn-primary w-full" disabled={busy || !token}>{busy ? 'Saving…' : 'Set password'}</button>
      </form>
    </AuthCard>
  )
}
