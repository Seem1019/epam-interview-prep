import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api'
import type { InterviewSummary } from '../api'
import { useAppData } from '../App'
import { ErrorBox, SetupNotice } from '../components/common'

export default function InterviewListPage() {
  const { status, catalog } = useAppData()
  const navigate = useNavigate()
  const [mode, setMode] = useState<'quick' | 'full'>('quick')
  const [topic, setTopic] = useState('react')
  const [sessions, setSessions] = useState<InterviewSummary[]>([])
  const [profileEmpty, setProfileEmpty] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api.get<InterviewSummary[]>('/api/interviews').then(setSessions).catch(setError)
    api.get<{ cvText: string }>('/api/profile').then(p => setProfileEmpty(!p.cvText?.trim())).catch(() => {})
  }, [])

  async function start() {
    setBusy(true)
    setError(null)
    try {
      const { id } = await api.post<{ id: number }>('/api/interviews', { mode, topicKey: mode === 'quick' ? topic : null })
      navigate(`/simulador/${id}`)
    } catch (e) {
      setError(e)
      setBusy(false)
    }
  }

  async function remove(id: number) {
    if (!confirm('¿Eliminar esta sesión?')) return
    await api.del(`/api/interviews/${id}`)
    setSessions(s => s.filter(x => x.id !== id))
  }

  const title = (keys: string) => keys.split(',').length > 1 ? 'Todos los bloques' : catalog.find(c => c.key === keys)?.title ?? keys

  return (
    <div className="page">
      <header className="page-head">
        <h1>Simulador de entrevista</h1>
        <p className="muted">Un entrevistador senior de EPAM en inglés. Empieza por lo básico y sube con follow-ups hasta encontrar tu techo.</p>
      </header>

      {status && !status.aiConfigured && <SetupNotice setup={status.setup} />}
      {profileEmpty && (
        <div className="notice">
          Tu perfil está vacío: las preguntas de experiencia serán genéricas y no asumirán nada sobre ti.{' '}
          <Link to="/perfil">Completar perfil</Link>
        </div>
      )}

      <section className="panel">
        <h2>Nueva sesión</h2>
        <div className="mode-choice">
          <label className={`choice ${mode === 'quick' ? 'selected' : ''}`}>
            <input type="radio" name="mode" checked={mode === 'quick'} onChange={() => setMode('quick')} />
            <div>
              <strong>Bloque rápido</strong>
              <span className="muted">Un tema, 15–20 min, feedback después de cada respuesta.</span>
            </div>
          </label>
          <label className={`choice ${mode === 'full' ? 'selected' : ''}`}>
            <input type="radio" name="mode" checked={mode === 'full'} onChange={() => setMode('full')} />
            <div>
              <strong>Entrevista completa</strong>
              <span className="muted">~120 min, todos los bloques en orden EPAM, feedback solo al final.</span>
            </div>
          </label>
        </div>
        {mode === 'quick' && (
          <label className="field">
            <span>Tema</span>
            <select value={topic} onChange={e => setTopic(e.target.value)}>
              {catalog.map(c => <option key={c.key} value={c.key}>{c.title}{c.weak ? ' (prioridad)' : ''}</option>)}
            </select>
          </label>
        )}
        <div className="row">
          <button className="btn btn-primary" disabled={busy || !status?.aiConfigured} onClick={start}>
            {mode === 'quick' ? 'Empezar bloque rápido' : 'Empezar entrevista completa'}
          </button>
          <span className="muted small">Responde por voz (Chrome/Edge) o escribiendo. Meta: 60–90 s por respuesta.</span>
        </div>
        <ErrorBox error={error} />
      </section>

      <section className="panel">
        <h2>Sesiones anteriores</h2>
        {sessions.length === 0 ? <p className="muted">Todavía no hay sesiones.</p> : (
          <table className="table">
            <thead><tr><th>Fecha</th><th>Modo</th><th>Tema</th><th>Respuestas</th><th>Estado</th><th /></tr></thead>
            <tbody>
              {sessions.map(s => (
                <tr key={s.id}>
                  <td>{new Date(s.startedAt).toLocaleString('es')}</td>
                  <td>{s.mode === 'full' ? 'Completa' : 'Rápido'}</td>
                  <td>{title(s.topics)}</td>
                  <td>{s.answers}</td>
                  <td>{s.hasReport ? 'Con reporte' : s.status === 'active' ? 'En curso' : s.status}</td>
                  <td className="actions">
                    <Link className="btn btn-sm" to={`/simulador/${s.id}`}>{s.hasReport ? 'Ver reporte' : 'Abrir'}</Link>
                    <button className="btn btn-ghost btn-sm" onClick={() => remove(s.id)}>Eliminar</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  )
}
