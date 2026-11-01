import { useState } from 'react'
import { motion } from 'motion/react'
import { Cpu, HardDrive, MemoryStick, Server } from 'lucide-react'
import { bytes, timeAgo, type HostView } from '../api'
import { usePoll } from '../hooks'
import { Message } from '../components/AuthCard'

function Meter({ label, value, icon, detail }: { label: string; value: number | null | undefined; icon: React.ReactNode; detail?: string }) {
  const pct = value == null ? 0 : Math.min(100, Math.max(0, value))
  const color = pct > 90 ? 'bg-red-500' : pct > 75 ? 'bg-amber-500' : 'bg-accent'
  return (
    <div>
      <div className="mb-1 flex items-center justify-between text-xs text-stone-500">
        <span className="flex items-center gap-1.5">{icon}{label}</span>
        <span>{value == null ? '–' : `${pct.toFixed(0)}%`}{detail && ` · ${detail}`}</span>
      </div>
      <div className="h-2 overflow-hidden rounded-full bg-black/5 dark:bg-white/10">
        <motion.div className={`h-full rounded-full ${color}`} initial={{ width: 0 }} animate={{ width: `${pct}%` }} transition={{ type: 'spring', stiffness: 80 }} />
      </div>
    </div>
  )
}

function uptime(s: number) {
  const d = Math.floor(s / 86400), h = Math.floor((s % 86400) / 3600)
  return d > 0 ? `${d} d ${h} h` : `${h} h ${Math.floor((s % 3600) / 60)} min`
}

export default function Hosts() {
  const { data, error } = usePoll<HostView[]>('/api/hosts', 15000)
  const [filter, setFilter] = useState('')
  return (
    <div className="space-y-6">
      <div><h1 className="text-3xl font-bold tracking-tight">Hosts</h1><p className="text-stone-500">Health reported by the HearthGate agent on each host.</p></div>
      {error && <Message kind="error">{error}</Message>}
      {data?.map(h => {
        const s = h.snapshot
        const mem = s?.memoryTotalBytes && s.memoryUsedBytes != null ? (100 * s.memoryUsedBytes) / s.memoryTotalBytes : null
        const containers = (s?.containers ?? []).filter(c => !filter || c.name.includes(filter.toLowerCase()))
        const bad = (s?.containers ?? []).filter(c => c.state !== 'running' || c.health === 'unhealthy').length
        return (
          <section key={h.id} className="glass rounded-2xl p-5">
            <div className="flex flex-wrap items-center gap-3">
              <Server className="size-5 text-accent" />
              <h2 className="text-lg font-semibold">{h.name}</h2>
              <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${h.online ? 'bg-emerald-500/15 text-emerald-600 dark:text-emerald-400' : 'bg-red-500/15 text-red-500'}`}>{h.online ? 'Online' : 'Agent offline'}</span>
              <span className="ml-auto text-xs text-stone-500">{s ? `${s.os} · up ${uptime(s.uptimeSeconds)} · ${timeAgo(h.lastSeen)}` : 'No data yet'}</span>
            </div>
            {s && (
              <div className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                <Meter label="CPU" value={s.cpuPercent} icon={<Cpu className="size-3.5" />} />
                <Meter label="Memory" value={mem} icon={<MemoryStick className="size-3.5" />} detail={`${bytes(s.memoryUsedBytes)} / ${bytes(s.memoryTotalBytes)}`} />
                {s.disks.map(d => <Meter key={d.name} label={d.name} value={100 * (d.totalBytes - d.freeBytes) / d.totalBytes} icon={<HardDrive className="size-3.5" />} detail={`${bytes(d.freeBytes)} free`} />)}
              </div>
            )}
            {s?.dockerError && <p className="mt-3 text-sm text-red-500">{s.dockerError}</p>}
            {s?.containers && (
              <div className="mt-5">
                <div className="mb-2 flex flex-wrap items-center gap-3">
                  <h3 className="font-medium">Containers ({s.containers.length}{bad ? `, ${bad} need attention` : ''})</h3>
                  <input className="input !min-h-9 !py-1.5 ml-auto max-w-48" placeholder="Filter…" value={filter} onChange={e => setFilter(e.target.value)} aria-label="Filter containers" />
                </div>
                <div className="grid gap-1.5 sm:grid-cols-2 lg:grid-cols-3">
                  {containers.map(c => (
                    <div key={c.name} className="flex items-center gap-2 rounded-lg bg-black/[0.03] px-2.5 py-1.5 text-sm dark:bg-white/[0.04]" title={`${c.image}\n${c.status}`}>
                      <span className={`size-2 shrink-0 rounded-full ${c.state !== 'running' ? 'bg-stone-400' : c.health === 'unhealthy' ? 'bg-red-500' : c.health === 'starting' ? 'bg-amber-500' : 'bg-emerald-500'}`} />
                      <span className="truncate">{c.name}</span>
                      <span className="ml-auto shrink-0 text-xs text-stone-500">{c.status.replace(/\s*\(.*\)/, '')}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </section>
        )
      })}
      {data?.length === 0 && <p className="text-stone-500">No hosts are configured.</p>}
    </div>
  )
}
