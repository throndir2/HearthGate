import { useState } from 'react'
import { AnimatePresence, motion } from 'motion/react'
import { Terminal } from 'lucide-react'
import { api, type GameCommand, type GameCommandResult, type GameServer } from '../api'
import { Message } from './AuthCard'

// Owns: the form for one game-server command (fields from the command's params), sending it and showing the output.
// Needs: the server and the command. The API checks the permission and the values again.

export function GameCommandDialog({ target, onClose }: { target: { g: GameServer; cmd: GameCommand } | null; onClose: () => void }) {
  return (
    <AnimatePresence>
      {target && (
        <motion.div className="fixed inset-0 z-50 grid place-items-center bg-black/50 p-4 backdrop-blur-sm" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
          onClick={onClose} role="presentation">
          <motion.div role="dialog" aria-modal="true" aria-labelledby="cmd-title"
            className="glass w-full max-w-lg rounded-2xl bg-white p-6 dark:bg-stone-900" initial={{ scale: 0.95, y: 10 }} animate={{ scale: 1, y: 0 }} exit={{ scale: 0.95, y: 10 }}
            onClick={e => e.stopPropagation()} onKeyDown={e => e.key === 'Escape' && onClose()}>
            <CommandForm key={`${target.g.id}/${target.cmd.id}`} g={target.g} cmd={target.cmd} onClose={onClose} />
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  )
}

function CommandForm({ g, cmd, onClose }: { g: GameServer; cmd: GameCommand; onClose: () => void }) {
  const [values, setValues] = useState<Record<string, string>>(() => Object.fromEntries(cmd.params.map(p => [p.name, p.default ?? (p.type === 'choice' ? p.options?.[0] ?? '' : '')])))
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<GameCommandResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    setBusy(true); setError(null); setResult(null)
    try { setResult(await api.post<GameCommandResult>(`/api/games/${g.id}/commands/${encodeURIComponent(cmd.id)}`, { params: values })) }
    catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }

  return (
    <form onSubmit={submit}>
      <h2 id="cmd-title" className="flex items-center gap-2 text-lg font-semibold"><Terminal className="size-5 text-accent" />{cmd.label}</h2>
      <p className="mt-1 text-sm text-stone-500">{g.name}{cmd.description ? ` · ${cmd.description}` : ''}</p>
      <div className="mt-4 space-y-3">
        {cmd.params.map((p, i) => (
          <div key={p.name}>
            <label className="label" htmlFor={`cmd-${p.name}`}>{p.label}{!p.required && <span className="font-normal text-stone-500"> (optional)</span>}</label>
            {p.type === 'choice'
              ? <select id={`cmd-${p.name}`} className="input" value={values[p.name]} onChange={e => setValues(v => ({ ...v, [p.name]: e.target.value }))}>
                  {p.options?.map(o => <option key={o} value={o}>{o}</option>)}
                </select>
              : <input id={`cmd-${p.name}`} className="input" autoFocus={i === 0} required={p.required} value={values[p.name]}
                  type={p.type === 'int' ? 'number' : 'text'} min={p.min ?? undefined} max={p.max ?? undefined} step={p.type === 'int' ? 1 : undefined}
                  maxLength={p.maxLength ?? undefined} pattern={p.pattern ?? undefined}
                  onChange={e => setValues(v => ({ ...v, [p.name]: e.target.value }))} />}
          </div>
        ))}
      </div>
      {cmd.confirm && !result && <p className="mt-4 text-sm text-amber-600 dark:text-amber-400">This command changes the server or affects players. Make sure that the values are correct.</p>}
      {error && <div className="mt-4"><Message kind="error">{error}</Message></div>}
      {result && <div className="mt-4"><Message kind={result.ok ? 'ok' : 'error'}>{result.message}</Message></div>}
      {result?.output && <pre className="mt-3 max-h-64 overflow-auto whitespace-pre-wrap rounded-xl bg-black/5 p-3 font-mono text-xs dark:bg-white/10">{result.output}</pre>}
      <div className="mt-6 flex justify-end gap-2">
        <button type="button" className="btn-ghost" onClick={onClose} autoFocus={cmd.params.length === 0}>Close</button>
        <button type="submit" className={cmd.confirm ? 'btn-danger' : 'btn-primary'} disabled={busy}>{busy ? 'Sending…' : 'Run'}</button>
      </div>
    </form>
  )
}
