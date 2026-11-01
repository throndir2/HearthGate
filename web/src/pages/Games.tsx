import { useState } from 'react'
import { motion } from 'motion/react'
import { Gamepad2, Play, RotateCw, Square, Users } from 'lucide-react'
import { api, timeAgo, type GameServer } from '../api'
import { usePoll } from '../hooks'
import { Message } from '../components/AuthCard'
import { ServiceIcon } from '../components/Icon'

const stateStyle: Record<GameServer['state'], string> = {
  running: 'bg-emerald-500/15 text-emerald-600 dark:text-emerald-400',
  stopped: 'bg-stone-500/15 text-stone-500',
  starting: 'bg-sky-500/15 text-sky-600 dark:text-sky-400 animate-pulse',
  stopping: 'bg-amber-500/15 text-amber-600 dark:text-amber-400 animate-pulse',
  unknown: 'bg-stone-500/10 text-stone-400',
}

export default function Games() {
  const { data, error, reload } = usePoll<GameServer[]>('/api/games', 10000)
  const [busy, setBusy] = useState<string | null>(null)
  const [msg, setMsg] = useState<[kind: 'ok' | 'error', text: string] | null>(null)

  async function act(g: GameServer, action: string) {
    if (action !== 'start' && !confirm(`${action === 'stop' ? 'Stop' : 'Restart'} ${g.name}? Players will be disconnected.`)) return
    setBusy(`${g.id}:${action}`); setMsg(null)
    try { const r = await api.post<{ message: string }>(`/api/games/${g.id}/${action}`); setMsg(['ok', r.message]) }
    catch (e) { setMsg(['error', (e as Error).message]) }
    finally { setBusy(null); reload() }
  }

  return (
    <div className="space-y-6">
      <div><h1 className="text-3xl font-bold tracking-tight">Game servers</h1><p className="text-stone-500">Status updates every few seconds.</p></div>
      {error && <Message kind="error">{error}</Message>}
      {msg && <Message kind={msg[0]}>{msg[1]}</Message>}
      <div className="grid gap-4 md:grid-cols-2">
        {data?.map((g, i) => (
          <motion.div key={g.id} initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: i * 0.05 }} className="glass rounded-2xl p-5">
            <div className="flex items-start gap-3">
              <span className="grid size-12 place-items-center rounded-xl bg-accent/15 text-accent">{g.icon ? <ServiceIcon icon={g.icon} name={g.name} /> : <Gamepad2 className="size-6" />}</span>
              <div className="flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="text-lg font-semibold">{g.name}</h2>
                  <span className={`rounded-full px-2 py-0.5 text-xs font-medium capitalize ${stateStyle[g.state]}`}>{g.state}</span>
                </div>
                <p className="text-sm text-stone-500">{[g.game, g.version && `v${g.version}`, g.updatedAt && `updated ${timeAgo(g.updatedAt)}`].filter(Boolean).join(' · ')}</p>
              </div>
            </div>
            {g.connectInfo && <p className="mt-3 text-sm">Connect: <code className="rounded bg-black/5 px-1.5 py-0.5 dark:bg-white/10">{g.connectInfo}</code></p>}
            {g.players != null && (
              <div className="mt-3 text-sm">
                <span className="flex items-center gap-1.5 font-medium"><Users className="size-4" />{g.players} online</span>
                {g.playerNames && g.playerNames.length > 0 && <p className="mt-1 text-stone-500">{g.playerNames.join(', ')}</p>}
              </div>
            )}
            {g.canControl && (
              <div className="mt-4 flex flex-wrap gap-2">
                <button className="btn-primary" disabled={!!busy || g.state === 'running'} onClick={() => act(g, 'start')}><Play className="size-4" />{busy === `${g.id}:start` ? 'Starting…' : 'Start'}</button>
                <button className="btn-outline" disabled={!!busy || g.state === 'stopped'} onClick={() => act(g, 'restart')}><RotateCw className={`size-4 ${busy === `${g.id}:restart` ? 'animate-spin' : ''}`} />Restart</button>
                <button className="btn-outline" disabled={!!busy || g.state === 'stopped'} onClick={() => act(g, 'stop')}><Square className="size-4" />{busy === `${g.id}:stop` ? 'Stopping…' : 'Stop'}</button>
              </div>
            )}
          </motion.div>
        ))}
      </div>
      {data?.length === 0 && <p className="text-stone-500">No game servers are configured.</p>}
    </div>
  )
}
