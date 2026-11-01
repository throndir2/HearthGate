import { StrictMode, Suspense, lazy } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Link, Route, Routes } from 'react-router'
import { MotionConfig } from 'motion/react'
import './index.css'
import { SessionProvider, useSession } from './session'
import Layout from './components/Layout'
import Home from './pages/Home'
import StatusPage from './pages/StatusPage'
import Projects from './pages/Projects'
import SignIn from './pages/SignIn'
import RequestAccess from './pages/RequestAccess'
import SetPassword from './pages/SetPassword'
const Profile = lazy(() => import('./pages/Profile'))
const Games = lazy(() => import('./pages/Games'))
const Hosts = lazy(() => import('./pages/Hosts'))
const Admin = lazy(() => import('./pages/Admin'))

function NotFound() {
  return (
    <div className="py-24 text-center">
      <p className="text-7xl font-bold text-accent">404</p>
      <p className="mt-2 text-stone-500">This page does not exist.</p>
      <Link to="/" className="btn-primary mt-6">Go home</Link>
    </div>
  )
}

function App() {
  const { loading } = useSession()
  if (loading) return <div className="grid min-h-dvh place-items-center"><div className="size-10 animate-spin rounded-full border-4 border-accent/30 border-t-accent" /></div>
  return (
    <Suspense fallback={<div className="grid min-h-dvh place-items-center"><div className="size-10 animate-spin rounded-full border-4 border-accent/30 border-t-accent" /></div>}>
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Home />} />
        <Route path="status" element={<StatusPage />} />
        <Route path="projects" element={<Projects />} />
        <Route path="signin" element={<SignIn />} />
        <Route path="request-access" element={<RequestAccess />} />
        <Route path="set-password" element={<SetPassword />} />
        <Route path="profile" element={<Profile />} />
        <Route path="games" element={<Games />} />
        <Route path="hosts" element={<Hosts />} />
        <Route path="admin" element={<Admin />} />
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
    </Suspense>
  )
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <MotionConfig reducedMotion="user">
      <BrowserRouter>
        <SessionProvider>
          <App />
        </SessionProvider>
      </BrowserRouter>
    </MotionConfig>
  </StrictMode>,
)
