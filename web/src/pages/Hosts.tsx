import { useState } from 'react'
import { motion } from 'motion/react'
import { Boxes, Cpu, ExternalLink, HardDrive, MemoryStick, Network, Server, TriangleAlert } from 'lucide-react'
import { bytes, timeAgo, type ContainerView, type HostView } from '../api'
import { usePoll } from '../hooks'
import { Message } from '../components/AuthCard'

// Owns: the Hosts page (hosts.view): network state, agent health and Docker containers of each configured host.

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

const good = 'bg-emerald-500/15 text-emerald-600 dark:text-emerald-400'
const bad = 'bg-red-500/15 text-red-500'
const neutral = 'bg-black/5 text-stone-500 dark:bg-white/10'

function Badge({ kind, children, title }: { kind: string; children: React.ReactNode; title?: string }) {
  return <span title={title} className={`rounded-full px-2 py-0.5 text-xs font-medium ${kind}`}>{children}</span>
}

const needsAttention = (c: ContainerView) => c.state !== 'running' || c.health === 'unhealthy'
// "Exited (0)" containers are usually one-shot jobs; only a non-zero exit is a problem.
const isProblem = (c: ContainerView) => c.health === 'unhealthy' || c.state === 'restarting' || c.state === 'dead' || (c.state === 'exited' && !/Exited \(0\)/.test(c.status))

// Resource use. CPU is the share of the whole host; memory is the share of the container limit (the host or VM memory if no limit).
const cpuOf = (c: ContainerView) => c.stats?.cpuPercent ?? -1
const memOf = (c: ContainerView) => c.stats?.memoryBytes ?? -1
const memPct = (c: ContainerView) => c.stats?.memoryLimitBytes ? (100 * c.stats.memoryBytes) / c.stats.memoryLimitBytes : null
const ioOf = (c: ContainerView) => (c.stats?.netRxBytesPerSec ?? 0) + (c.stats?.netTxBytesPerSec ?? 0) + (c.stats?.diskReadBytesPerSec ?? 0) + (c.stats?.diskWriteBytesPerSec ?? 0)
const isHog = (c: ContainerView) => cpuOf(c) >= 50 || (memPct(c) ?? 0) >= 25
const rate = (n?: number | null) => n == null ? '–' : `${bytes(n)}/s`
const pct = (n?: number | null) => n == null ? '–' : `${n < 1 ? n.toFixed(2) : n < 10 ? n.toFixed(1) : n.toFixed(0)}%`

type SortKey = 'name' | 'cpu' | 'memory' | 'io'
const sorters: Record<SortKey, (a: ContainerView, b: ContainerView) => number> = {
  name: (a, b) => a.name.localeCompare(b.name),
  cpu: (a, b) => cpuOf(b) - cpuOf(a),
  memory: (a, b) => memOf(b) - memOf(a),
  io: (a, b) => ioOf(b) - ioOf(a),
}

function statsTitle(c: ContainerView) {
  const s = c.stats
  if (!s) return ''
  return `\nCPU ${pct(s.cpuPercent)} of the host\nMemory ${bytes(s.memoryBytes)}${s.memoryLimitBytes ? ` of ${bytes(s.memoryLimitBytes)} (${pct(memPct(c))})` : ''}`
    + `\nNetwork in ${rate(s.netRxBytesPerSec)}, out ${rate(s.netTxBytesPerSec)}\nDisk read ${rate(s.diskReadBytesPerSec)}, write ${rate(s.diskWriteBytesPerSec)}`
}

function Busiest({ title, rows, value, bar }: { title: string; rows: { c: ContainerView; host: string }[]; value: (c: ContainerView) => string; bar: (c: ContainerView) => number }) {
  return (
    <div className="glass rounded-2xl p-4">
      <h3 className="mb-3 text-sm font-medium text-stone-500">{title}</h3>
      {rows.length === 0 && <p className="text-sm text-stone-500">No data yet. Statistics show after the second poll (about 20 s).</p>}
      <div className="space-y-2.5">
        {rows.map(({ c, host }) => (
          <div key={host + c.name} title={`${c.image}${statsTitle(c)}`}>
            <div className="flex items-baseline gap-2 text-sm">
              <span className="truncate font-medium">{c.name}</span>
              <span className="truncate text-xs text-stone-500">{host}</span>
              <span className={`ml-auto shrink-0 font-mono text-xs ${isHog(c) ? 'text-amber-500' : 'text-stone-500'}`}>{value(c)}</span>
            </div>
            <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-black/5 dark:bg-white/10">
              <motion.div className={`h-full rounded-full ${isHog(c) ? 'bg-amber-500' : 'bg-accent'}`}
                initial={{ width: 0 }} animate={{ width: `${Math.max(1, Math.min(100, bar(c)))}%` }} transition={{ type: 'spring', stiffness: 80 }} />
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

function networkBadge(h: HostView) {
  const n = h.network
  if (!h.address) return null
  if (!n) return <Badge kind={neutral}>Checking network…</Badge>
  if (n.reachable === true) return <Badge kind={good} title={`Since ${timeAgo(n.since)}`}>Reachable{n.pingMs != null ? ` · ${n.pingMs} ms` : ''}</Badge>
  if (n.reachable === false) return <Badge kind={bad} title={`Since ${timeAgo(n.since)}`}>Unreachable</Badge>
  return <Badge kind={neutral}>{n.pingError ?? 'Unknown'}</Badge>
}

function agentBadge(h: HostView) {
  if (!h.hasAgent) return <Badge kind={neutral}>No agent</Badge>
  return h.online ? <Badge kind={good}>Agent online</Badge> : <Badge kind={bad}>Agent offline</Badge>
}

function hostIsDown(h: HostView) {
  return h.network?.reachable === false || (h.hasAgent && !h.online)
}

// Published TCP port (for example "8080->80/tcp") to a link on the host's link address.
function portLink(base: string, p: string) {
  if (!p.endsWith('/tcp')) return null
  const port = p.split(/[-/]/)[0]
  const url = new URL(base)
  if (port === '443' || port === '8443') url.protocol = 'https:'
  url.port = port
  return url.origin + '/'
}

export default function Hosts() {
  const { data, error } = usePoll<HostView[]>('/api/hosts', 15000)
  const [filter, setFilter] = useState('')
  const [problemsOnly, setProblemsOnly] = useState(false)
  const [sort, setSort] = useState<SortKey>('name')

  const all = data ?? []
  const withHost = all.flatMap(h => (h.snapshot?.containers ?? []).filter(c => c.stats).map(c => ({ c, host: h.name })))
  const topCpu = [...withHost].filter(x => x.c.stats?.cpuPercent != null).sort((a, b) => cpuOf(b.c) - cpuOf(a.c)).slice(0, 5)
  const topMem = [...withHost].sort((a, b) => memOf(b.c) - memOf(a.c)).slice(0, 5)
  const maxCpu = Math.max(1, ...topCpu.map(x => cpuOf(x.c)))
  const checked = all.filter(h => h.network?.reachable != null)
  const reachable = checked.filter(h => h.network?.reachable).length
  const agents = all.filter(h => h.hasAgent)
  const containers = all.flatMap(h => h.snapshot?.containers ?? [])
  const running = containers.filter(c => c.state === 'running').length
  const problems = containers.filter(isProblem).length
  const f = filter.trim().toLowerCase()
  const matches = (c: ContainerView) => (!problemsOnly || isProblem(c))
    && (!f || c.name.toLowerCase().includes(f) || c.image.toLowerCase().includes(f) || (c.project ?? '').toLowerCase().includes(f) || (c.ports ?? []).some(p => p.includes(f)))

  const stats = [
    { label: 'Servers reachable', value: checked.length ? `${reachable} / ${checked.length}` : '–', warn: reachable < checked.length, icon: <Network className="size-4" /> },
    { label: 'Agents online', value: agents.length ? `${agents.filter(h => h.online).length} / ${agents.length}` : '–', warn: agents.some(h => !h.online), icon: <Server className="size-4" /> },
    { label: 'Containers running', value: containers.length ? `${running} / ${containers.length}` : '–', warn: false, icon: <Boxes className="size-4" /> },
    { label: 'Containers with problems', value: String(problems), warn: problems > 0, icon: <TriangleAlert className="size-4" /> },
  ]

  return (
    <div className="space-y-6">
      <div><h1 className="text-3xl font-bold tracking-tight">Hosts</h1><p className="text-stone-500">Network state, health and Docker containers of each server.</p></div>
      {error && <Message kind="error">{error}</Message>}

      {data && data.length > 0 && (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          {stats.map(s => (
            <div key={s.label} className="glass rounded-2xl p-4">
              <div className="flex items-center gap-2 text-xs text-stone-500">{s.icon}{s.label}</div>
              <div className={`mt-1 text-2xl font-semibold ${s.warn ? 'text-red-500' : ''}`}>{s.value}</div>
            </div>
          ))}
        </div>
      )}

      {withHost.length > 0 && (
        <div className="grid gap-3 md:grid-cols-2">
          <Busiest title="Busiest containers: CPU (share of the host)" rows={topCpu} value={c => pct(c.stats?.cpuPercent)}
            bar={c => (100 * Math.max(0, cpuOf(c))) / maxCpu} />
          <Busiest title="Busiest containers: memory" rows={topMem} value={c => `${bytes(c.stats?.memoryBytes)}${memPct(c) != null ? ` · ${pct(memPct(c))}` : ''}`}
            bar={c => memPct(c) ?? 0} />
        </div>
      )}

      {data && containers.length > 0 && (
        <div className="flex flex-wrap items-center gap-3">
          <input className="input !min-h-9 !py-1.5 max-w-64" placeholder="Filter containers…" value={filter} onChange={e => setFilter(e.target.value)} aria-label="Filter containers" />
          <select className="input !min-h-9 !w-auto !py-1.5" value={sort} onChange={e => setSort(e.target.value as SortKey)} aria-label="Sort containers">
            <option value="name">Sort by name</option>
            <option value="cpu">Sort by CPU</option>
            <option value="memory">Sort by memory</option>
            <option value="io">Sort by network and disk</option>
          </select>
          <label className="flex items-center gap-2 text-sm text-stone-500">
            <input type="checkbox" checked={problemsOnly} onChange={e => setProblemsOnly(e.target.checked)} />Show only problems
          </label>
        </div>
      )}

      {data?.map(h => {
        const s = h.snapshot
        const mem = s?.memoryTotalBytes && s.memoryUsedBytes != null ? (100 * s.memoryUsedBytes) / s.memoryTotalBytes : null
        const list = (s?.containers ?? []).filter(matches).sort(sorters[sort])
        const attention = (s?.containers ?? []).filter(needsAttention).length
        const hostProblems = (s?.containers ?? []).filter(isProblem).length
        return (
          <section key={h.id} className={`glass rounded-2xl p-5 ${hostIsDown(h) ? 'ring-1 ring-red-500/40' : ''}`}>
            <div className="flex flex-wrap items-center gap-2">
              <Server className="size-5 text-accent" />
              <h2 className="mr-1 text-lg font-semibold">{h.name}</h2>
              {networkBadge(h)}
              {agentBadge(h)}
              <span className="ml-auto text-xs text-stone-500">
                {[h.address, s ? `${s.os} · up ${uptime(s.uptimeSeconds)}` : null, h.hasAgent ? `agent ${timeAgo(h.lastSeen)}` : null].filter(Boolean).join(' · ')}
              </span>
            </div>
            {(h.linkBase || h.links.length > 0) && (
              <div className="mt-3 flex flex-wrap items-center gap-1.5">
                {h.linkBase && (
                  <a href={h.linkBase} target="_blank" rel="noreferrer" className={`flex items-center gap-1 rounded-md px-2 py-0.5 font-mono text-xs hover:underline ${neutral}`} title="Link address of this host">
                    <ExternalLink className="size-3" />{new URL(h.linkBase).host}
                  </a>
                )}
                {h.links.map(l => (
                  <a key={l.label + l.url} href={l.url} target="_blank" rel="noreferrer" className={`flex items-center gap-1 rounded-md px-2 py-0.5 text-xs hover:underline ${neutral}`} title={l.url}>
                    <ExternalLink className="size-3" />{l.label}
                  </a>
                ))}
              </div>
            )}
            {h.network && h.network.ports.length > 0 && (
              <div className="mt-3 flex flex-wrap gap-1.5">
                {h.network.ports.map(p => (
                  <span key={p.port} className={`rounded-md px-2 py-0.5 font-mono text-xs ${p.open ? good : bad}`} title={p.open ? 'Port open' : 'Port closed'}>
                    TCP {p.port} {p.open ? 'open' : 'closed'}
                  </span>
                ))}
              </div>
            )}
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
                <h3 className="mb-2 font-medium">
                  Docker containers ({s.containers.filter(c => c.state === 'running').length} running of {s.containers.length}
                  {hostProblems ? `, ${hostProblems} with problems` : attention ? `, ${attention} stopped` : ''})
                </h3>
                <div className="grid gap-1.5 md:grid-cols-2">
                  {list.map(c => (
                    <div key={c.name} className="flex items-center gap-2 rounded-lg bg-black/[0.03] px-2.5 py-1.5 text-sm dark:bg-white/[0.04]" title={`${c.image}${c.project ? `\nCompose project: ${c.project}` : ''}\n${c.status}${statsTitle(c)}`}>
                      <span className={`size-2 shrink-0 rounded-full ${isProblem(c) ? 'bg-red-500' : c.state !== 'running' ? 'bg-stone-400' : c.health === 'starting' ? 'bg-amber-500' : 'bg-emerald-500'}`} />
                      <span className="truncate">{c.name}</span>
                      {c.ports && c.ports.length > 0 && (
                        <span className="hidden truncate font-mono text-xs text-stone-500 sm:inline">
                          {c.ports.map((p, i) => {
                            const label = p.replace('/tcp', '')
                            const href = h.linkBase && c.state === 'running' ? portLink(h.linkBase, p) : null
                            return <span key={p}>{i > 0 && ', '}{href
                              ? <a href={href} target="_blank" rel="noreferrer" className="text-accent hover:underline" title={`Open ${href}`}>{label}</a>
                              : label}</span>
                          })}
                        </span>
                      )}
                      {c.stats && (
                        <span className={`ml-auto shrink-0 font-mono text-xs ${isHog(c) ? 'text-amber-500' : 'text-stone-500'}`}>
                          {sort === 'io' ? `${rate(ioOf(c))} I/O` : `${pct(c.stats.cpuPercent)} · ${bytes(c.stats.memoryBytes)}`}
                        </span>
                      )}
                      <span className={`${c.stats ? '' : 'ml-auto'} shrink-0 text-xs ${isProblem(c) ? 'text-red-500' : 'text-stone-500'}`}>{c.status.replace(/\s*\((healthy|health: starting)\)/, '')}</span>
                    </div>
                  ))}
                  {list.length === 0 && <p className="text-sm text-stone-500">No containers match the filter.</p>}
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
