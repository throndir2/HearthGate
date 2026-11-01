import { motion } from 'motion/react'
import { useServices } from '../hooks'
import { StatusDot, StatusPill, HistoryBar, statusMeta } from '../components/Status'
import { ServiceIcon } from '../components/Icon'
import { timeAgo } from '../api'
import { useSession } from '../session'

export default function StatusPage() {
  const { data, error } = useServices(20000)
  const { can } = useSession()
  const monitored = (data?.services ?? []).filter(s => s.status)
  const overall = data?.overall ?? 'unknown'
  const counts = monitored.reduce<Record<string, number>>((a, s) => ({ ...a, [s.status!]: (a[s.status!] ?? 0) + 1 }), {})

  return (
    <div className="space-y-8">
      <div className="glass flex flex-col items-start gap-4 rounded-3xl p-6 sm:flex-row sm:items-center sm:p-8">
        <span className={`grid size-14 place-items-center rounded-2xl ring-8 ${statusMeta[overall].ring}`}><StatusDot status={overall} size="size-5" /></span>
        <div className="flex-1">
          <h1 className="text-2xl font-bold">System status</h1>
          <p className="text-stone-500">{monitored.length} services checked automatically. This page updates by itself.</p>
        </div>
        <div className="flex gap-4 text-sm">
          {(['up', 'degraded', 'down'] as const).map(s => (
            <div key={s} className="text-center"><div className={`text-2xl font-semibold ${statusMeta[s].text}`}>{counts[s] ?? 0}</div><div className="text-stone-500">{statusMeta[s].label}</div></div>
          ))}
        </div>
      </div>
      {error && <p className="text-red-500">{error}</p>}
      <motion.ul className="glass divide-y divide-black/5 overflow-hidden rounded-2xl dark:divide-white/10" initial="hidden" animate="show"
        variants={{ show: { transition: { staggerChildren: 0.04 } } }}>
        {monitored.map(s => (
          <motion.li key={s.id} variants={{ hidden: { opacity: 0, x: -10 }, show: { opacity: 1, x: 0 } }} className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center">
            <div className="flex flex-1 items-center gap-3">
              <span className="grid size-9 place-items-center rounded-lg bg-accent/10 text-accent"><ServiceIcon icon={s.icon} name={s.name} className="size-5" /></span>
              <div className="min-w-0">
                <div className="font-medium">{s.name}</div>
                <div className="text-xs text-stone-500">Checked {timeAgo(s.lastChecked)}{s.details?.latencyMs != null && ` · ${s.details.latencyMs} ms`}{s.details?.uptime24h != null && ` · ${s.details.uptime24h}% (24 h)`}</div>
              </div>
            </div>
            {s.details && <div className="w-full sm:w-64"><HistoryBar history={s.details.history} /></div>}
            <StatusPill status={s.status!} />
          </motion.li>
        ))}
      </motion.ul>
      {!can('status.details') && <p className="text-center text-sm text-stone-500">Signed-in members with access can see response times and history.</p>}
    </div>
  )
}
