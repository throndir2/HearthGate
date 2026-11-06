import { AnimatePresence, motion } from 'motion/react'
import { Gamepad2, Play, RotateCw, ShieldAlert, Square, Terminal, Users } from 'lucide-react'
import { timeAgo, type GameCommand, type GameServer } from '../api'
import { ServiceIcon } from './Icon'

// Owns: the game-server card (guest and member views), its server-command buttons and the confirmation dialog for stop/restart.

export const gameState: Record<GameServer['state'], { label: string; cls: string }> = {
  running: { label: 'Online', cls: 'bg-emerald-500/15 text-emerald-600 dark:text-emerald-400' },
  stopped: { label: 'Offline', cls: 'bg-stone-500/15 text-stone-500 dark:text-stone-400' },
  starting: { label: 'Starting', cls: 'bg-sky-500/15 text-sky-600 dark:text-sky-400 animate-pulse' },
  stopping: { label: 'Stopping', cls: 'bg-amber-500/15 text-amber-600 dark:text-amber-400 animate-pulse' },
  unknown: { label: 'Unknown', cls: 'bg-stone-500/10 text-stone-400' },
}

export function GameCard({ g, busy, onAction, onCommand, compact = false }: {
  g: GameServer
  busy?: boolean
  onAction?: (g: GameServer, action: 'start' | 'stop' | 'restart') => void
  onCommand?: (g: GameServer, cmd: GameCommand) => void
  compact?: boolean
}) {
  const s = gameState[g.state] ?? gameState.unknown
  return (
    <div className={`glass h-full rounded-2xl ${compact ? 'p-4' : 'p-5'}`}>
      <div className="flex items-start gap-3">
        <span className={`grid shrink-0 place-items-center rounded-xl bg-accent/15 text-accent ${compact ? 'size-10' : 'size-12'}`}>
          {g.icon ? <ServiceIcon icon={g.icon} name={g.name} /> : <Gamepad2 className="size-6" />}
        </span>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <h3 className={`font-semibold ${compact ? '' : 'text-lg'}`}>{g.name}</h3>
            <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${s.cls}`}>{s.label}</span>
          </div>
          <p className="text-sm text-stone-500">{[g.game, g.version && `v${g.version}`, !compact && g.updatedAt && `checked ${timeAgo(g.updatedAt)}`].filter(Boolean).join(' · ')}</p>
        </div>
      </div>
      {g.connectInfo && <p className="mt-3 text-sm">Connect: <code className="rounded bg-black/5 px-1.5 py-0.5 dark:bg-white/10">{g.connectInfo}</code></p>}
      {g.players != null && (
        <div className="mt-3 text-sm">
          <span className="flex items-center gap-1.5 font-medium"><Users className="size-4" />{g.players} online</span>
          {!compact && g.playerNames && g.playerNames.length > 0 && <p className="mt-1 text-stone-500">{g.playerNames.join(', ')}</p>}
        </div>
      )}
      {!compact && g.canControl && onAction && (
        <div className="mt-4 flex flex-wrap gap-2">
          <button className="btn-primary" disabled={busy || g.state !== 'stopped'} onClick={() => onAction(g, 'start')}><Play className="size-4" />Start</button>
          <button className="btn-outline" disabled={busy || g.state !== 'running'} onClick={() => onAction(g, 'restart')}><RotateCw className="size-4" />Restart</button>
          <button className="btn-outline" disabled={busy || g.state !== 'running'} onClick={() => onAction(g, 'stop')}><Square className="size-4" />Stop</button>
        </div>
      )}
      {!compact && onCommand && g.commands && g.commands.length > 0 && (
        <div className="mt-4 border-t border-black/5 pt-3 dark:border-white/10">
          <p className="mb-2 flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide text-stone-500"><Terminal className="size-3.5" />Server commands</p>
          <div className="flex flex-wrap gap-1.5">
            {g.commands.map(c => (
              <button key={c.id} className="btn-ghost !min-h-8 !px-2.5 !py-1 text-sm ring-1 ring-black/10 dark:ring-white/15"
                disabled={busy || (c.requiresRunning && g.state !== 'running')} onClick={() => onCommand(g, c)}
                title={c.description ?? (c.level === 'admin' ? 'Admin command' : undefined)}>
                {c.level === 'admin' && <ShieldAlert className="size-3.5 text-amber-500" aria-label="Admin command" />}{c.label}
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

export function ConfirmDialog({ open, title, body, confirmLabel, onConfirm, onCancel }: {
  open: boolean; title: string; body: string; confirmLabel: string; onConfirm: () => void; onCancel: () => void
}) {
  return (
    <AnimatePresence>
      {open && (
        <motion.div className="fixed inset-0 z-50 grid place-items-center bg-black/50 p-4 backdrop-blur-sm" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
          onClick={onCancel} role="presentation">
          <motion.div role="alertdialog" aria-modal="true" aria-labelledby="confirm-title" aria-describedby="confirm-body"
            className="glass w-full max-w-sm rounded-2xl bg-white p-6 dark:bg-stone-900" initial={{ scale: 0.95, y: 10 }} animate={{ scale: 1, y: 0 }} exit={{ scale: 0.95, y: 10 }}
            onClick={e => e.stopPropagation()} onKeyDown={e => e.key === 'Escape' && onCancel()}>
            <h2 id="confirm-title" className="text-lg font-semibold">{title}</h2>
            <p id="confirm-body" className="mt-2 text-sm text-stone-500">{body}</p>
            <div className="mt-6 flex justify-end gap-2">
              <button className="btn-ghost" onClick={onCancel} autoFocus>Cancel</button>
              <button className="btn-danger" onClick={onConfirm}>{confirmLabel}</button>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  )
}
