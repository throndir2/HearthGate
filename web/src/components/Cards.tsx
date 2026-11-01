import { motion } from 'motion/react'
import { ArrowUpRight, GitFork, Star } from 'lucide-react'
import { timeAgo, type Project, type Service } from '../api'
import { ServiceIcon } from './Icon'
import { HistoryBar, StatusPill } from './Status'

// Owns: service and project cards with hover/entrance animation.

export const cardVariants = {
  hidden: { opacity: 0, y: 16, scale: 0.97 },
  show: { opacity: 1, y: 0, scale: 1, transition: { type: 'spring' as const, stiffness: 260, damping: 24 } },
}

export function ServiceCard({ s }: { s: Service }) {
  const inner = (
    <>
      <div className="flex items-start gap-3">
        <span className="grid size-11 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-accent/20 to-amber-400/10 text-accent ring-1 ring-accent/20 transition group-hover:scale-110 group-hover:rotate-3">
          <ServiceIcon icon={s.icon} name={s.name} />
        </span>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <h3 className="truncate font-semibold">{s.name}</h3>
            {s.url && <ArrowUpRight className="size-4 shrink-0 text-stone-400 opacity-0 transition group-hover:translate-x-0.5 group-hover:-translate-y-0.5 group-hover:opacity-100" />}
          </div>
          {s.description && <p className="mt-0.5 line-clamp-2 text-sm text-stone-500 dark:text-stone-400">{s.description}</p>}
        </div>
      </div>
      {s.status && (
        <div className="mt-4 flex items-center justify-between gap-2 text-xs text-stone-500">
          <StatusPill status={s.status} />
          <span>{s.details?.latencyMs != null ? `${s.details.latencyMs} ms · ` : ''}{timeAgo(s.lastChecked)}</span>
        </div>
      )}
      {s.details && s.details.history.length > 0 && (
        <div className="mt-3">
          <HistoryBar history={s.details.history} />
          <div className="mt-1.5 flex justify-between text-[11px] text-stone-500">
            <span>{s.details.uptime24h != null ? `${s.details.uptime24h}% uptime (24 h)` : ''}</span>
            {s.details.lastError && <span>{s.details.lastError}</span>}
          </div>
        </div>
      )}
    </>
  )
  const cls = 'glass group block h-full rounded-2xl p-4 transition hover:-translate-y-1 hover:shadow-xl hover:shadow-accent/10 focus-visible:outline-2 focus-visible:outline-accent'
  return (
    <motion.div layout variants={cardVariants} initial="hidden" animate="show" exit={{ opacity: 0, scale: 0.95 }}>
      {s.url ? <a href={s.url} target="_blank" rel="noopener noreferrer" className={cls}>{inner}</a> : <div className={cls}>{inner}</div>}
    </motion.div>
  )
}

const langColors: Record<string, string> = {
  'C#': 'bg-violet-500', TypeScript: 'bg-sky-500', JavaScript: 'bg-yellow-400', Python: 'bg-blue-500', Go: 'bg-cyan-500',
  Rust: 'bg-orange-600', PowerShell: 'bg-blue-400', HTML: 'bg-orange-500', CSS: 'bg-indigo-500', Lua: 'bg-blue-800', 'C++': 'bg-pink-500',
}

export function ProjectCard({ p }: { p: Project }) {
  return (
    <motion.a variants={cardVariants} initial="hidden" animate="show" href={p.url} target="_blank" rel="noopener noreferrer"
      className="glass group flex h-full flex-col rounded-2xl p-5 transition hover:-translate-y-1 hover:shadow-xl hover:shadow-accent/10">
      <div className="flex items-center justify-between gap-2">
        <h3 className="truncate font-semibold group-hover:text-accent">{p.name}</h3>
        <ArrowUpRight className="size-4 shrink-0 text-stone-400 transition group-hover:translate-x-0.5 group-hover:-translate-y-0.5 group-hover:text-accent" />
      </div>
      <p className="mt-2 line-clamp-3 flex-1 text-sm text-stone-500 dark:text-stone-400">{p.description || 'No description yet.'}</p>
      {p.topics.length > 0 && (
        <div className="mt-3 flex flex-wrap gap-1.5">
          {p.topics.slice(0, 4).map(t => <span key={t} className="rounded-full bg-accent/10 px-2 py-0.5 text-[11px] font-medium text-accent">{t}</span>)}
        </div>
      )}
      <div className="mt-4 flex items-center gap-4 text-xs text-stone-500">
        {p.language && <span className="flex items-center gap-1.5"><span className={`size-2.5 rounded-full ${langColors[p.language] ?? 'bg-stone-400'}`} />{p.language}</span>}
        <span className="flex items-center gap-1"><Star className="size-3.5" />{p.stars}</span>
        <span className="flex items-center gap-1"><GitFork className="size-3.5" />{p.forks}</span>
        <span className="ml-auto">{timeAgo(p.updatedAt)}</span>
      </div>
    </motion.a>
  )
}
