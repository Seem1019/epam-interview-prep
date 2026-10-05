import { createContext, useContext, useEffect, useState } from 'react'
import { NavLink, Route, Routes } from 'react-router-dom'
import { api } from './api'
import type { Competency, Status } from './api'
import DashboardPage from './pages/DashboardPage'
import InterviewListPage from './pages/InterviewListPage'
import InterviewSessionPage from './pages/InterviewSessionPage'
import QuizPage from './pages/QuizPage'
import StudyPage from './pages/StudyPage'
import PronunciationPage from './pages/PronunciationPage'
import ProfilePage from './pages/ProfilePage'

interface AppData { status: Status | null; catalog: Competency[] }
const AppContext = createContext<AppData>({ status: null, catalog: [] })
export const useAppData = () => useContext(AppContext)

const NAV = [
  { to: '/', label: 'Inicio', end: true },
  { to: '/simulador', label: 'Simulador' },
  { to: '/evaluaciones', label: 'Evaluaciones' },
  { to: '/estudio', label: 'Estudio' },
  { to: '/pronunciacion', label: 'Pronunciación' },
  { to: '/perfil', label: 'Perfil' },
]

function Countdown({ target }: { target: string }) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 30_000)
    return () => clearInterval(id)
  }, [])
  const ms = new Date(target).getTime() - now
  if (ms <= 0) return <span>Hoy es la entrevista</span>
  const days = Math.floor(ms / 86_400_000)
  const hours = Math.floor((ms % 86_400_000) / 3_600_000)
  return <span><strong>{days}d {hours}h</strong> para la entrevista</span>
}

function useTheme() {
  const [theme, setTheme] = useState(() => {
    try { return localStorage.getItem('theme') ?? 'dark' } catch { return 'dark' }
  })
  useEffect(() => {
    document.documentElement.dataset.theme = theme
    try { localStorage.setItem('theme', theme) } catch { /* private mode */ }
  }, [theme])
  return [theme, () => setTheme(t => (t === 'dark' ? 'light' : 'dark'))] as const
}

export default function App() {
  const [data, setData] = useState<AppData>({ status: null, catalog: [] })
  const [theme, toggleTheme] = useTheme()

  useEffect(() => {
    // Retry while the API is still starting (e.g. the browser opened before `dotnet run` finished).
    let cancelled = false
    let timer: ReturnType<typeof setTimeout>
    const load = () =>
      Promise.all([api.get<Status>('/api/status'), api.get<Competency[]>('/api/catalog')])
        .then(([status, catalog]) => { if (!cancelled) setData({ status, catalog }) })
        .catch(() => { if (!cancelled) timer = setTimeout(load, 2000) })
    load()
    return () => { cancelled = true; clearTimeout(timer) }
  }, [])

  const s = data.status
  return (
    <AppContext.Provider value={data}>
      <div className="shell">
        <aside className="sidebar">
          <div className="brand">
            <span className="brand-mark">L3</span>
            <div>
              <div className="brand-title">EPAM Prep</div>
              <div className="brand-sub">.NET Full-stack</div>
            </div>
          </div>
          <nav>
            {NAV.map(n => (
              <NavLink key={n.to} to={n.to} end={n.end} className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}>
                {n.label}
              </NavLink>
            ))}
          </nav>
          <div className="sidebar-foot">
            {s && <div className="countdown"><Countdown target={s.interviewAt} /></div>}
            <div className="ai-status">
              <span className={`dot ${s?.aiConfigured ? (s.demoMode ? 'dot-warn' : 'dot-ok') : 'dot-bad'}`} />
              {s ? (s.aiConfigured ? (s.demoMode ? 'Modo demo (sin IA real)' : s.model) : 'IA sin configurar') : 'Conectando…'}
            </div>
            <button className="btn btn-ghost btn-sm" onClick={toggleTheme}>{theme === 'dark' ? 'Tema claro' : 'Tema oscuro'}</button>
          </div>
        </aside>
        <main className="content">
          <Routes>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/simulador" element={<InterviewListPage />} />
            <Route path="/simulador/:id" element={<InterviewSessionPage />} />
            <Route path="/evaluaciones" element={<QuizPage />} />
            <Route path="/estudio" element={<StudyPage />} />
            <Route path="/pronunciacion" element={<PronunciationPage />} />
            <Route path="/perfil" element={<ProfilePage />} />
          </Routes>
        </main>
      </div>
    </AppContext.Provider>
  )
}
