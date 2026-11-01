import type { Status } from '../api'

// Owns: the colored status dot, label and small latency sparkline.

export const statusMeta: Record<Status, { label: string; dot: string; text: string; ring: string }> = {
  up: { label: 'Online', dot: 'bg-emerald-500', text: 'text-emerald-600 dark:text-emerald-400', ring: 'ring-emerald-500/30' },
  degraded: { label: 'Slow', dot: 'bg-amber-500', text: 'text-amber-600 dark:text-amber-400', ring: 'ring-amber-500/30' },
  down: { label: 'Offline', dot: 'bg-red-500', text: 'text-red-600 dark:text-red-400', ring: 'ring-red-500/30' },
  unknown: { label: 'Checking', dot: 'bg-stone-400', text: 'text-stone-500', ring: 'ring-stone-400/30' },
}

export function StatusDot({ status, size = 'size-2.5' }: { status: Status; size?: string }) {
  const m = statusMeta[status] ?? statusMeta.unknown
  return (
    <span className={`relative inline-flex ${size}`} aria-hidden>
      {status !== 'unknown' && <span className={`status-ping absolute inline-flex h-full w-full rounded-full ${m.dot} opacity-60`} />}
      <span className={`relative inline-flex ${size} rounded-full ${m.dot}`} />
    </span>
  )
}

export function StatusPill({ status }: { status: Status }) {
  const m = statusMeta[status] ?? statusMeta.unknown
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium ring-1 ${m.ring} ${m.text}`}>
      <StatusDot status={status} size="size-2" />
      {m.label}
    </span>
  )
}

export function HistoryBar({ history }: { history: { state: Status; at: string }[] }) {
  const pts = history.slice(-40)
  return (
    <div className="flex h-6 items-end gap-[2px]" aria-label="Recent checks">
      {pts.map((p, i) => (
        <span key={i} title={`${statusMeta[p.state].label} · ${new Date(p.at).toLocaleString()}`}
          className={`w-1.5 flex-1 rounded-sm ${statusMeta[p.state].dot} ${p.state === 'up' ? 'h-full opacity-70' : 'h-full'}`} />
      ))}
    </div>
  )
}
