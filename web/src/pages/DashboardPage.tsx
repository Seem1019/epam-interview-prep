import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import type { Dashboard, PlanTask } from '../api'
import { ErrorBox, LevelBadge, SetupNotice } from '../components/common'
import { useAppData } from '../App'

const SOURCE_ES: Record<string, string> = { simulacro: 'simulacro', quizzes: 'quizzes', anterior: 'sin datos nuevos' }

function taskLink(t: PlanTask): string | null {
  switch (t.kind) {
    case 'lesson': return '/estudio' + (t.topicKey ? `?topic=${t.topicKey}` : '')
    case 'quiz':
    case 'reviews': return '/evaluaciones'
    case 'quick':
    case 'mock':
    case 'report': return '/simulador'
    case 'pronunciation':
    case 'warmup': return '/pronunciacion'
    case 'profile': return '/perfil'
    default: return null
  }
}

export default function DashboardPage() {
  const { status } = useAppData()
  const [data, setData] = useState<Dashboard | null>(null)
  const [error, setError] = useState<unknown>(null)

  const load = () => api.get<Dashboard>('/api/dashboard').then(setData).catch(setError)
  useEffect(() => { load() }, [])

  async function toggle(t: PlanTask) {
    await api.post('/api/plan/toggle', { key: t.key, done: !t.done })
    load()
  }

  if (!data) return <div className="page"><p className="muted">Cargando…</p><ErrorBox error={error} /></div>

  const ms = new Date(data.interviewAt).getTime() - Date.now()
  const days = Math.max(0, Math.floor(ms / 86_400_000))
  const hours = Math.max(0, Math.floor((ms % 86_400_000) / 3_600_000))
  const interviewDate = new Date(data.interviewAt).toLocaleString('es', { weekday: 'long', day: 'numeric', month: 'long', hour: '2-digit', minute: '2-digit' })

  return (
    <div className="page">
      <header className="page-head">
        <h1>Entrevista EPAM L3 en {days} días y {hours} horas</h1>
        <p className="muted">{interviewDate}. Objetivo: Advanced en lo central; Intermediate sólido en React, JavaScript y GenAI; respuestas de 60–90 s.</p>
      </header>

      {!data.aiConfigured && <SetupNotice setup={status?.setup} />}
      {!data.profileComplete && (
        <div className="notice">Completa tu <Link to="/perfil">perfil</Link> para que el simulador haga preguntas sobre tu experiencia real.</div>
      )}
      {data.dueReviews > 0 && (
        <div className="notice">Tienes {data.dueReviews} preguntas falladas listas para repaso. <Link to="/evaluaciones">Repasar ahora</Link></div>
      )}

      <div className="dash-grid">
        <section className="panel">
          <h2>Competencias</h2>
          <table className="table">
            <thead>
              <tr><th>Competencia</th><th>Anterior</th><th>Actual</th><th>Objetivo</th><th>Quiz</th></tr>
            </thead>
            <tbody>
              {data.competencies.map(c => {
                const gap = ['None', 'Novice', 'Intermediate', 'Advanced', 'Expert']
                const behind = gap.indexOf(c.current) < gap.indexOf(c.target)
                return (
                  <tr key={c.key} className={c.weak ? 'row-weak' : ''}>
                    <td>{c.title}{c.weak && <span className="tag tag-priority">prioridad</span>}</td>
                    <td><LevelBadge level={c.previous} /></td>
                    <td title={`Fuente: ${SOURCE_ES[c.currentSource]}`}>
                      <LevelBadge level={c.current} />
                      {c.currentSource !== 'anterior' && <span className="muted small"> · {SOURCE_ES[c.currentSource]}</span>}
                    </td>
                    <td><LevelBadge level={c.target} />{!behind && <span className="up small"> ok</span>}</td>
                    <td className="muted">{c.quizAverage !== null ? `${c.quizAverage} (${c.attempts})` : '—'}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
          <p className="muted small">"Actual" usa el último simulacro si existe; si no, el nivel estimado por quizzes (≥2 intentos por nivel con promedio ≥70); si no, el nivel anterior.</p>
        </section>

        <section className="panel plan">
          <h2>Plan día por día</h2>
          <p className="muted small">Se recalcula con tus resultados: primero lo más débil, luego subir de Intermediate a Advanced, y un simulacro completo antes del último día.</p>
          {data.plan.map(day => {
            const total = day.tasks.reduce((s, t) => s + t.minutes, 0)
            const done = day.tasks.filter(t => t.done).length
            return (
              <div key={day.date} className="plan-day">
                <div className="plan-day-head">
                  <strong>{day.label}</strong>
                  <span className="muted small">{day.focus} · {Math.round(total / 6) / 10} h · {done}/{day.tasks.length}</span>
                </div>
                <ul>
                  {day.tasks.map(t => {
                    const link = taskLink(t)
                    return (
                      <li key={t.key} className={t.done ? 'done' : ''}>
                        <input type="checkbox" checked={t.done} onChange={() => toggle(t)} aria-label="Hecho" />
                        {link ? <Link to={link}>{t.title}</Link> : <span>{t.title}</span>}
                        <span className="muted small">{t.minutes} min</span>
                      </li>
                    )
                  })}
                </ul>
              </div>
            )
          })}
        </section>
      </div>
    </div>
  )
}
