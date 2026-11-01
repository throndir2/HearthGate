import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { AnimatePresence, motion } from 'motion/react'
import { ArrowRight, Search } from 'lucide-react'
import { useSession } from '../session'
import { usePoll, useProjects, useServices } from '../hooks'
import { ProjectCard, ServiceCard } from '../components/Cards'
import { GameCard } from '../components/GameCard'
import { StatusDot, statusMeta } from '../components/Status'
import type { GameServer, Status } from '../api'

const overallText: Record<Status, string> = {
  up: 'All systems are running',
  degraded: 'Some services are slow',
  down: 'Some services are offline',
  unknown: 'Checking services…',
}

export default function Home() {
  const { site, user } = useSession()
  const { data, error } = useServices()
  const projects = useProjects(!!site?.showProjects)
  const games = usePoll<GameServer[]>(site?.showGames ? '/api/games' : null, 30000).data
  const [query, setQuery] = useState('')
  const [category, setCategory] = useState<string | null>(null)

  const services = useMemo(() => (data?.services ?? []).filter(s =>
    (!category || s.category === category) &&
    (!query || `${s.name} ${s.description ?? ''}`.toLowerCase().includes(query.toLowerCase()))), [data, category, query])

  const usedCategories = (site?.categories ?? []).filter(c => data?.services.some(s => s.category === c.id))
  const overall = data?.overall ?? 'unknown'

  return (
    <div className="space-y-16">
      <section className="pt-6 text-center sm:pt-12">
        <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.05 }}
          className={`glass mx-auto mb-6 inline-flex items-center gap-2 rounded-full px-3.5 py-1.5 text-sm ${statusMeta[overall].text}`}>
          <StatusDot status={overall} />
          <Link to="/status" className="font-medium hover:underline">{overallText[overall]}</Link>
        </motion.div>
        <motion.h1 initial={{ opacity: 0, y: 16 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.1, type: 'spring', stiffness: 120 }}
          className="bg-gradient-to-br from-stone-900 via-stone-700 to-accent bg-clip-text text-4xl font-bold tracking-tight text-transparent sm:text-6xl dark:from-white dark:via-stone-200 dark:to-accent">
          {user ? `Welcome back, ${user.username}` : site?.title}
        </motion.h1>
        <motion.p initial={{ opacity: 0, y: 16 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.2 }}
          className="mx-auto mt-4 max-w-xl text-lg text-stone-500 dark:text-stone-400">
          {site?.tagline}
        </motion.p>
        <motion.div initial={{ opacity: 0, y: 16 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.3 }} className="relative mx-auto mt-8 max-w-md">
          <Search className="pointer-events-none absolute left-3.5 top-1/2 size-4 -translate-y-1/2 text-stone-400" />
          <input type="search" value={query} onChange={e => setQuery(e.target.value)} placeholder="Find a service…" aria-label="Find a service" className="input !rounded-2xl !pl-10 !py-3" />
        </motion.div>
      </section>

      <section aria-labelledby="services-title">
        <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
          <h2 id="services-title" className="text-xl font-semibold">Services</h2>
          {usedCategories.length > 1 && (
            <div className="flex flex-wrap gap-1.5" role="group" aria-label="Filter by category">
              {[{ id: null as string | null, name: 'All' }, ...usedCategories].map(c => (
                <button key={c.id ?? 'all'} onClick={() => setCategory(c.id)} aria-pressed={category === c.id}
                  className={`relative rounded-full px-3.5 py-1.5 text-sm font-medium transition min-h-9 ${category === c.id ? 'text-white' : 'text-stone-600 hover:bg-black/5 dark:text-stone-300 dark:hover:bg-white/10'}`}>
                  {category === c.id && <motion.span layoutId="cat-pill" className="absolute inset-0 -z-10 rounded-full bg-accent shadow-lg shadow-accent/30" />}
                  {c.name}
                </button>
              ))}
            </div>
          )}
        </div>
        {error && <p className="text-sm text-red-500">{error}</p>}
        {!data && !error && <SkeletonGrid />}
        <motion.div layout className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3" initial="hidden" animate="show" variants={{ show: { transition: { staggerChildren: 0.05 } } }}>
          <AnimatePresence mode="popLayout">
            {services.map(s => <ServiceCard key={s.id} s={s} />)}
          </AnimatePresence>
        </motion.div>
        {data && services.length === 0 && <p className="py-10 text-center text-stone-500">No services match your search.</p>}
      </section>

      {(games?.length ?? 0) > 0 && (
        <section aria-labelledby="games-title">
          <div className="mb-5 flex items-end justify-between gap-3">
            <h2 id="games-title" className="text-xl font-semibold">Game servers</h2>
            <Link to="/games" className="btn-ghost">Details <ArrowRight className="size-4" /></Link>
          </div>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {games!.map(g => <Link key={g.id} to="/games" className="block transition hover:-translate-y-1"><GameCard g={g} compact /></Link>)}
          </div>
        </section>
      )}

      {site?.showProjects && (projects?.length ?? 0) > 0 && (
        <section aria-labelledby="projects-title">
          <div className="mb-5 flex items-end justify-between gap-3">
            <div>
              <h2 id="projects-title" className="text-xl font-semibold">Open-source projects</h2>
              <p className="text-sm text-stone-500">Things I build in public.</p>
            </div>
            <Link to="/projects" className="btn-ghost">All projects <ArrowRight className="size-4" /></Link>
          </div>
          <motion.div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3" initial="hidden" whileInView="show" viewport={{ once: true, margin: '-80px' }}
            variants={{ show: { transition: { staggerChildren: 0.06 } } }}>
            {projects!.slice(0, 6).map(p => <ProjectCard key={p.name} p={p} />)}
          </motion.div>
        </section>
      )}

      {!user && site?.auth.accountRequests && (
        <motion.section initial={{ opacity: 0, y: 20 }} whileInView={{ opacity: 1, y: 0 }} viewport={{ once: true }}
          className="glass relative overflow-hidden rounded-3xl p-8 text-center sm:p-12">
          <div className="absolute -right-20 -top-20 size-60 rounded-full bg-accent/20 blur-3xl" aria-hidden />
          <h2 className="text-2xl font-semibold">Friend or family?</h2>
          <p className="mx-auto mt-2 max-w-md text-stone-500">Ask for an account to see more details and use extra tools. An admin checks each request.</p>
          <div className="mt-6 flex flex-wrap justify-center gap-3">
            <Link to="/request-access" className="btn-primary">Request an account</Link>
            <Link to="/signin" className="btn-outline">Sign in</Link>
          </div>
        </motion.section>
      )}
    </div>
  )
}

export function SkeletonGrid({ n = 6 }: { n?: number }) {
  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      {Array.from({ length: n }).map((_, i) => <div key={i} className="glass h-28 animate-pulse rounded-2xl" />)}
    </div>
  )
}
