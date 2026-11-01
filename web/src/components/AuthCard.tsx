import type { ReactNode } from 'react'
import { motion } from 'motion/react'

// Owns: the centered card used by sign-in, request and set-password pages, plus form messages.

export function AuthCard({ title, subtitle, children }: { title: string; subtitle?: string; children: ReactNode }) {
  return (
    <motion.div initial={{ opacity: 0, y: 20, scale: 0.98 }} animate={{ opacity: 1, y: 0, scale: 1 }} transition={{ type: 'spring', stiffness: 200, damping: 22 }}
      className="glass mx-auto mt-6 max-w-md rounded-3xl p-6 sm:mt-12 sm:p-8">
      <h1 className="text-2xl font-bold tracking-tight">{title}</h1>
      {subtitle && <p className="mt-1 text-sm text-stone-500">{subtitle}</p>}
      <div className="mt-6">{children}</div>
    </motion.div>
  )
}

export function Message({ kind, children }: { kind: 'error' | 'ok' | 'info'; children: ReactNode }) {
  const cls = kind === 'error' ? 'bg-red-500/10 text-red-600 ring-red-500/20 dark:text-red-400'
    : kind === 'ok' ? 'bg-emerald-500/10 text-emerald-700 ring-emerald-500/20 dark:text-emerald-400'
      : 'bg-sky-500/10 text-sky-700 ring-sky-500/20 dark:text-sky-300'
  return <motion.div role={kind === 'error' ? 'alert' : 'status'} initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: 'auto' }}
    className={`mb-4 rounded-xl px-3.5 py-2.5 text-sm ring-1 ${cls}`}>{children}</motion.div>
}
