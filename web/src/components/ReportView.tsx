import type { InterviewReport, Level } from '../api'
import { LEVELS } from '../api'
import { LevelBadge, Markdown, SpeakButton } from './common'

function delta(prev: Level, now: Level) {
  const p = LEVELS.indexOf(prev)
  const n = LEVELS.indexOf(now)
  if (prev === 'None') return <span className="muted">antes sin evaluar</span>
  if (n > p) return <span className="up">sube</span>
  if (n < p) return <span className="down">baja</span>
  return <span className="muted">igual</span>
}

export default function ReportView({ report }: { report: InterviewReport }) {
  const ai = report.ai
  return (
    <div className="report">
      <section className="panel">
        <div className="report-head">
          <h2>Reporte final</h2>
          <div className="row">
            <span className="muted">Nivel global</span>
            <LevelBadge level={ai.overall_level} />
          </div>
        </div>
        <p>{ai.summary_es}</p>
        <div className="stats-row">
          <div><strong>{report.minutes}</strong><span>minutos</span></div>
          <div><strong>{report.answers}</strong><span>respuestas</span></div>
          <div><strong>{report.averageWords}</strong><span>palabras promedio</span></div>
          <div><strong>{report.tooLong}</strong><span>respuestas &gt;150 palabras</span></div>
        </div>
      </section>

      <section className="panel">
        <h2>Nivel por competencia</h2>
        <table className="table">
          <thead><tr><th>Competencia</th><th>Antes</th><th>Ahora</th><th /><th>Objetivo</th></tr></thead>
          <tbody>
            {ai.competencies.map(c => {
              const cat = report.catalog[c.key]
              return (
                <tr key={c.key}>
                  <td>{cat?.title ?? c.key}</td>
                  <td><LevelBadge level={cat?.previous} /></td>
                  <td><LevelBadge level={c.level} /></td>
                  <td>{cat && delta(cat.previous, c.level)}</td>
                  <td><LevelBadge level={cat?.target} /></td>
                </tr>
              )
            })}
          </tbody>
        </table>
        <div className="evidence-list">
          {ai.competencies.map(c => (
            <details key={c.key}>
              <summary>{report.catalog[c.key]?.title ?? c.key} — evidencia y siguiente nivel</summary>
              <p><strong>Evidencia.</strong> {c.evidence_es}</p>
              <p><strong>Para subir.</strong> {c.to_next_level_es}</p>
              <p className="muted">{c.comparison_es}</p>
            </details>
          ))}
        </div>
      </section>

      <section className="panel">
        <h2>Comunicación</h2>
        <p><strong>Concisión.</strong> {ai.communication.concision_es}</p>
        <p><strong>Pronunciación.</strong> {ai.communication.pronunciation_es}</p>
        <h3>Acciones prioritarias</h3>
        <ol>{ai.top_actions_es.map((a, i) => <li key={i}>{a}</li>)}</ol>
      </section>

      <section className="panel">
        <h2>Preguntas donde fallaste ({report.failed.length})</h2>
        {report.failed.length === 0 && <p className="muted">Ninguna respuesta por debajo de 5/10.</p>}
        {report.failed.map(f => (
          <div key={f.turnId} className="failed">
            <div className="failed-head">
              <span className="tag">{f.topicTitle}</span>
              <span className="muted">{f.score}/10</span>
            </div>
            <div className="failed-q" lang="en"><Markdown text={f.question} /></div>
            <p className="muted"><strong>Tu respuesta:</strong> {f.answer}</p>
            <p>{f.justificationEs}</p>
            <div className="model">
              <div className="model-head"><h4>Respuesta modelo (30–45 s)</h4><SpeakButton text={f.modelAnswerEn} /></div>
              <p lang="en">{f.modelAnswerEn}</p>
            </div>
          </div>
        ))}
      </section>
    </div>
  )
}
