import {
  BookOpen, Box, Cloud, Code2, Database, Drama, Film, Gamepad2, Gauge, Globe, HardDrive, Headphones, Home, Inbox, Library,
  MessageCircle, Mic, Music, Newspaper, Server, Shield, Sparkles, Tv, Wrench, Zap, Camera, Bot, Calendar, Folder, Activity,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

// Owns: mapping config icon names to icons. Icon value can be a known name, a /icons/ path, or an emoji/text.
const icons: Record<string, LucideIcon> = {
  book: BookOpen, books: BookOpen, box: Box, cloud: Cloud, code: Code2, database: Database, film: Film, movie: Film,
  gamepad: Gamepad2, games: Gamepad2, gauge: Gauge, globe: Globe, disk: HardDrive, headphones: Headphones, home: Home,
  inbox: Inbox, library: Library, chat: MessageCircle, mic: Mic, music: Music, news: Newspaper, server: Server,
  shield: Shield, sparkles: Sparkles, tv: Tv, wrench: Wrench, zap: Zap, camera: Camera, bot: Bot, calendar: Calendar,
  folder: Folder, activity: Activity, drama: Drama, roleplay: Drama,
}

export function ServiceIcon({ icon, name, className = 'size-6' }: { icon?: string | null; name: string; className?: string }) {
  if (icon && icon.startsWith('/icons/')) return <img src={icon} alt="" className={className} loading="lazy" />
  const I = icon ? icons[icon.toLowerCase()] : undefined
  if (I) return <I className={className} aria-hidden />
  const text = icon && icon.length <= 4 ? icon : name.slice(0, 1).toUpperCase()
  return <span className="text-lg font-semibold leading-none" aria-hidden>{text}</span>
}
