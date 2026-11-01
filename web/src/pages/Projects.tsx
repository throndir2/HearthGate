import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import { Search } from 'lucide-react'
import { useProjects } from '../hooks'
import { ProjectCard } from '../components/Cards'
import { SkeletonGrid } from './Home'

export default function Projects() {
  const projects = useProjects(true)
  const [query, setQuery] = useState('')
  const [lang, setLang] = useState<string | null>(null)
  const langs = useMemo(() => [...new Set((projects ?? []).map(p => p.language).filter(Boolean) as string[])].sort(), [projects])
  const shown = (projects ?? []).filter(p => (!lang || p.language === lang) &&
    (!query || `${p.name} ${p.description ?? ''} ${p.topics.join(' ')}`.toLowerCase().includes(query.toLowerCase())))

  return (
    <div className="space-y-8">
      <div>
        <h1 className="text-3xl font-bold tracking-tight">Projects</h1>
        <p className="mt-1 text-stone-500">Public projects on GitHub that do not have their own website.</p>
      </div>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <div className="relative flex-1 sm:max-w-sm">
          <Search className="pointer-events-none absolute left-3.5 top-1/2 size-4 -translate-y-1/2 text-stone-400" />
          <input type="search" value={query} onChange={e => setQuery(e.target.value)} placeholder="Search projects…" aria-label="Search projects" className="input !pl-10" />
        </div>
        <div className="flex flex-wrap gap-1.5">
          {[null, ...langs].map(l => (
            <button key={l ?? 'all'} onClick={() => setLang(l)} aria-pressed={lang === l}
              className={`rounded-full px-3 py-1.5 text-sm min-h-9 transition ${lang === l ? 'bg-accent text-white' : 'hover:bg-black/5 dark:hover:bg-white/10'}`}>{l ?? 'All'}</button>
          ))}
        </div>
      </div>
      {!projects ? <SkeletonGrid /> : (
        <motion.div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3" initial="hidden" animate="show" variants={{ show: { transition: { staggerChildren: 0.05 } } }}>
          {shown.map(p => <ProjectCard key={p.name} p={p} />)}
        </motion.div>
      )}
      {projects && shown.length === 0 && <p className="py-10 text-center text-stone-500">No projects found.</p>}
    </div>
  )
}
