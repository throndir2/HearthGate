import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { motion } from 'motion/react'
import { CheckCircle2, ShieldCheck } from 'lucide-react'
import { api } from '../api'
import { useSession } from '../session'
import { AuthCard, Message } from '../components/AuthCard'

interface Challenge { salt: string; bits: number; expires: number; signature: string }

function solve(c: Challenge): Promise<string> {
  return new Promise((resolve, reject) => {
    const w = new Worker(new URL('../pow.worker.ts', import.meta.url), { type: 'module' })
    w.onmessage = e => { resolve(e.data as string); w.terminate() }
    w.onerror = e => { reject(e); w.terminate() }
    w.postMessage({ salt: c.salt, bits: c.bits })
  })
}

export default function RequestAccess() {
  const { site } = useSession()
  const [form, setForm] = useState({ username: '', email: '', message: '', website: '' })
  const [state, setState] = useState<'idle' | 'working' | 'sending' | 'done'>('idle')
  const [error, setError] = useState<string | null>(null)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null); setState('working')
    try {
      const pow = await api.get<Challenge>('/api/pow')
      const nonce = await solve(pow)
      setState('sending')
      await api.post('/api/account-requests', { ...form, pow, nonce })
      setState('done')
    } catch (err) {
      setError((err as Error).message); setState('idle')
    }
  }

  if (site && !site.auth.accountRequests) return <AuthCard title="Requests are closed"><p className="text-stone-500">This site does not take new account requests now.</p></AuthCard>

  if (state === 'done') return (
    <AuthCard title="Request sent">
      <motion.div initial={{ scale: 0.5, opacity: 0 }} animate={{ scale: 1, opacity: 1 }} transition={{ type: 'spring', stiffness: 200 }} className="flex justify-center">
        <CheckCircle2 className="size-16 text-emerald-500" />
      </motion.div>
      <p className="mt-4 text-center text-stone-500">Thank you. An admin will check your request. If it is approved, the admin will send you a link to set your password.</p>
      <Link to="/" className="btn-outline mt-6 w-full">Back to home</Link>
    </AuthCard>
  )

  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [k]: e.target.value })
  return (
    <AuthCard title="Request an account" subtitle="An admin checks each request. There is no automatic sign-up.">
      {error && <Message kind="error">{error}</Message>}
      <form onSubmit={submit} className="space-y-4">
        <div><label className="label" htmlFor="ru">Username</label>
          <input id="ru" className="input" autoComplete="username" minLength={3} maxLength={32} pattern="[A-Za-z0-9._\-]+" value={form.username} onChange={set('username')} required />
          <p className="mt-1 text-xs text-stone-500">3 to 32 characters: letters, digits, '.', '_' or '-'.</p></div>
        <div><label className="label" htmlFor="re">Email (optional)</label>
          <input id="re" type="email" className="input" autoComplete="email" maxLength={254} value={form.email} onChange={set('email')} /></div>
        <div><label className="label" htmlFor="rm">Who are you? (optional)</label>
          <textarea id="rm" className="input min-h-24" maxLength={500} value={form.message} onChange={set('message')} placeholder="Tell the admin how you know them." /></div>
        {/* Honeypot: hidden from people, bots fill it. */}
        <div className="absolute -left-[9999px] h-0 w-0 overflow-hidden" aria-hidden>
          <label htmlFor="rw">Website</label><input id="rw" tabIndex={-1} autoComplete="off" value={form.website} onChange={set('website')} />
        </div>
        <button className="btn-primary w-full" disabled={state !== 'idle'}>
          {state === 'working' ? <><ShieldCheck className="size-4 animate-pulse" />Running a security check…</> : state === 'sending' ? 'Sending…' : 'Send request'}
        </button>
        <p className="text-center text-xs text-stone-500">Your browser does a short calculation to stop spam. No tracking, no third-party CAPTCHA.</p>
      </form>
    </AuthCard>
  )
}
