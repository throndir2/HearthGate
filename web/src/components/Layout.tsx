import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router'
import { AnimatePresence, motion } from 'motion/react'
import { Flame, LogIn, LogOut, Menu, Moon, Sun, User, X } from 'lucide-react'
import { useSession } from '../session'
import { api } from '../api'

// Owns: page chrome (animated background, header, mobile menu, footer, theme toggle, page transitions).

function Background() {
  return (
    <div className="pointer-events-none fixed inset-0 -z-10 overflow-hidden" aria-hidden>
      <div className="absolute -top-1/3 left-1/2 h-[70vmax] w-[70vmax] -translate-x-1/2 rounded-full bg-accent/20 blur-3xl animate-float" />
      <div className="absolute -bottom-1/3 -left-1/4 h-[55vmax] w-[55vmax] rounded-full bg-rose-500/10 blur-3xl animate-float-slow" />
      <div className="absolute top-1/3 -right-1/4 h-[45vmax] w-[45vmax] rounded-full bg-amber-400/10 blur-3xl animate-float" />
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_1px_1px,rgba(120,113,108,0.15)_1px,transparent_0)] [background-size:28px_28px] [mask-image:linear-gradient(to_bottom,black,transparent_70%)]" />
    </div>
  )
}

function useTheme() {
  const [dark, setDark] = useState(() => localStorage.getItem('hg.theme') !== 'light')
  useEffect(() => {
    document.documentElement.classList.toggle('dark', dark)
    localStorage.setItem('hg.theme', dark ? 'dark' : 'light')
  }, [dark])
  return [dark, setDark] as const
}

export default function Layout() {
  const { site, user, can, refresh } = useSession()
  const [dark, setDark] = useTheme()
  const [open, setOpen] = useState(false)
  const location = useLocation()
  const navigate = useNavigate()
  useEffect(() => setOpen(false), [location.pathname])

  const links = [
    { to: '/', label: 'Home' },
    { to: '/status', label: 'Status' },
    ...(site?.showProjects ? [{ to: '/projects', label: 'Projects' }] : []),
    ...(site?.showGames ? [{ to: '/games', label: 'Games' }] : []),
    ...(can('hosts.view') ? [{ to: '/hosts', label: 'Hosts' }] : []),
    ...(user?.isAdmin ? [{ to: '/admin', label: 'Admin' }] : []),
  ]

  async function logout() {
    await api.post('/api/auth/logout')
    await refresh()
    navigate('/')
  }

  const navClass = ({ isActive }: { isActive: boolean }) =>
    `relative rounded-lg px-3 py-2 text-sm font-medium transition ${isActive ? 'text-stone-900 dark:text-white' : 'text-stone-500 hover:text-stone-900 dark:text-stone-400 dark:hover:text-white'}`

  return (
    <div className="flex min-h-dvh flex-col">
      <Background />
      <a href="#main" className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-50 btn-primary">Skip to content</a>
      <header className="sticky top-0 z-40 px-3 pt-3 sm:px-6">
        <div className="glass mx-auto flex max-w-6xl items-center gap-2 rounded-2xl px-3 py-2">
          <Link to="/" className="flex items-center gap-2 rounded-lg px-2 py-1.5 font-semibold tracking-tight">
            {site?.logo ? <img src={site.logo} alt="" className="size-7" /> : (
              <span className="grid size-8 place-items-center rounded-xl bg-gradient-to-br from-accent to-amber-400 text-white shadow-lg shadow-accent/30">
                <Flame className="size-4.5" />
              </span>
            )}
            <span>{site?.title ?? 'HearthGate'}</span>
          </Link>
          <nav className="ml-4 hidden items-center gap-1 md:flex" aria-label="Main">
            {links.map(l => (
              <NavLink key={l.to} to={l.to} end={l.to === '/'} className={navClass}>
                {({ isActive }) => (<>
                  {isActive && <motion.span layoutId="nav-pill" className="absolute inset-0 -z-10 rounded-lg bg-black/5 dark:bg-white/10" transition={{ type: 'spring', bounce: 0.2, duration: 0.5 }} />}
                  {l.label}
                </>)}
              </NavLink>
            ))}
          </nav>
          <div className="ml-auto flex items-center gap-1">
            <button className="btn-ghost !px-3" onClick={() => setDark(!dark)} aria-label={dark ? 'Use light theme' : 'Use dark theme'}>
              {dark ? <Sun className="size-4" /> : <Moon className="size-4" />}
            </button>
            {user ? (
              <div className="hidden items-center gap-1 md:flex">
                <Link to="/profile" className="btn-ghost"><User className="size-4" />{user.username}</Link>
                <button className="btn-ghost !px-3" onClick={logout} aria-label="Sign out"><LogOut className="size-4" /></button>
              </div>
            ) : (
              <Link to="/signin" className="btn-primary hidden md:inline-flex"><LogIn className="size-4" />Sign in</Link>
            )}
            <button className="btn-ghost !px-3 md:hidden" onClick={() => setOpen(!open)} aria-expanded={open} aria-label="Menu">
              {open ? <X className="size-5" /> : <Menu className="size-5" />}
            </button>
          </div>
        </div>
        <AnimatePresence>
          {open && (
            <motion.nav initial={{ opacity: 0, y: -8, scale: 0.98 }} animate={{ opacity: 1, y: 0, scale: 1 }} exit={{ opacity: 0, y: -8, scale: 0.98 }}
              className="glass mx-auto mt-2 flex max-w-6xl flex-col gap-1 rounded-2xl p-2 md:hidden" aria-label="Mobile">
              {links.map(l => <NavLink key={l.to} to={l.to} end={l.to === '/'} className={navClass}>{l.label}</NavLink>)}
              <div className="my-1 h-px bg-black/5 dark:bg-white/10" />
              {user ? (<>
                <NavLink to="/profile" className={navClass}>Profile ({user.username})</NavLink>
                <button className="rounded-lg px-3 py-2 text-left text-sm font-medium text-stone-500" onClick={logout}>Sign out</button>
              </>) : <Link to="/signin" className="btn-primary">Sign in</Link>}
            </motion.nav>
          )}
        </AnimatePresence>
      </header>

      <main id="main" className="mx-auto w-full max-w-6xl flex-1 px-4 pb-16 pt-8 sm:px-6">
        <AnimatePresence mode="wait">
          <motion.div key={location.pathname} initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -8 }} transition={{ duration: 0.25 }}>
            <Outlet />
          </motion.div>
        </AnimatePresence>
      </main>

      <footer className="mx-auto w-full max-w-6xl px-6 pb-8 text-sm text-stone-500">
        <div className="flex flex-col items-center justify-between gap-3 border-t border-black/5 pt-6 sm:flex-row dark:border-white/10">
          <span>© {new Date().getFullYear()} {site?.title} · Powered by <a className="hover:text-accent" href="https://github.com/throndir2/HearthGate" rel="noopener noreferrer" target="_blank">HearthGate</a></span>
          <div className="flex gap-4">
            {site?.footerLinks.map(l => <a key={l.url} href={l.url} target="_blank" rel="noopener noreferrer" className="hover:text-accent">{l.label}</a>)}
          </div>
        </div>
      </footer>
    </div>
  )
}
