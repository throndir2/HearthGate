import { useState } from 'react'
import { motion } from 'motion/react'
import { api, type GameServer } from '../api'
import { usePoll } from '../hooks'
import { useSession } from '../session'
import { Message } from '../components/AuthCard'
import { ConfirmDialog, GameCard } from '../components/GameCard'

type Action = 'start' | 'stop' | 'restart'

export default function Games() {
  const { can } = useSession()
  const { data, error, reload } = usePoll<GameServer[]>('/api/games', 10000)
  const [busy, setBusy] = useState<string | null>(null)
  const [msg, setMsg] = useState<[kind: 'ok' | 'error', text: string] | null>(null)
  const [pending, setPending] = useState<{ g: GameServer; action: Action } | null>(null)

  async function run(g: GameServer, action: Action) {
    setPending(null); setBusy(g.id); setMsg(null)
    try { const r = await api.post<{ message: string }>(`/api/games/${g.id}/${action}`); setMsg(['ok', r.message]) }
    catch (e) { setMsg(['error', (e as Error).message]) }
    finally { setBusy(null); reload() }
  }

  function request(g: GameServer, action: Action) {
    if (action === 'start') run(g, action)
    else setPending({ g, action })
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold tracking-tight">Game servers</h1>
        <p className="text-stone-500">{can('games.view') ? 'Status updates every few seconds.' : 'See which game servers are online. Members with access see more details.'}</p>
      </div>
      {error && <Message kind="error">{error}</Message>}
      {msg && <Message kind={msg[0]}>{msg[1]}</Message>}
      <div className="grid gap-4 md:grid-cols-2">
        {data?.map((g, i) => (
          <motion.div key={g.id} initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: i * 0.05 }}>
            <GameCard g={g} busy={busy === g.id} onAction={request} />
          </motion.div>
        ))}
      </div>
      {data?.length === 0 && <p className="text-stone-500">No game servers are configured.</p>}
      <ConfirmDialog
        open={!!pending}
        title={pending ? `${pending.action === 'stop' ? 'Stop' : 'Restart'} ${pending.g.name}?` : ''}
        body="Players on this server are disconnected. The server saves first when it can."
        confirmLabel={pending?.action === 'stop' ? 'Stop server' : 'Restart server'}
        onConfirm={() => pending && run(pending.g, pending.action)}
        onCancel={() => setPending(null)}
      />
    </div>
  )
}
