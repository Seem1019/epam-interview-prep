import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api, ApiError } from '../api'
import type { Lesson, LessonIndexTopic } from '../api'
import { useAppData } from '../App'
import { CodeBlock, ErrorBox, SetupNotice, SpeakButton } from '../components/common'

export default function StudyPage() {
  const { status } = useAppData()
  const [params, setParams] = useSearchParams()
  const [index, setIndex] = useState<LessonIndexTopic[]>([])
  const [lesson, setLesson] = useState<Lesson | null>(null)
  const [loading, setLoading] = useState<string | null>(null)
  const [error, setError] = useState<unknown>(null)
  const topic = params.get('topic') ?? 'react'
  const sub = params.get('sub')

  const loadIndex = () => api.get<LessonIndexTopic[]>('/api/lessons').then(setIndex).catch(setError)
  useEffect(() => { loadIndex() }, [])

  const current = index.find(t => t.key === topic)
  const subKey = sub ?? current?.subtopics[0]?.key

  useEffect(() => {
    if (!subKey) return
    setLesson(null)
    setError(null)
    api.get<Lesson>(`/api/lessons/${topic}/${subKey}`)
      .then(setLesson)
      .catch(e => { if (!(e instanceof ApiError && e.status === 404)) setError(e) })
  }, [topic, subKey])

  async function generate() {
    if (!subKey) return
    setLoading('Generando la lección con IA (30–90 s)…')
    setError(null)
    try {
      setLesson(await api.post<Lesson>(`/api/lessons/${topic}/${subKey}/generate`))
      loadIndex()
    } catch (e) {
      setError(e)
    } finally {
      setLoading(null)
    }
  }

  const subTitle = current?.subtopics.find(s => s.key === subKey)?.title
  const c = lesson?.content

  return (
    <div className="page study">
      <aside className="study-nav">
        {index.map(t => (
          <details key={t.key} open={t.key === topic}>
            <summary>{t.title} <span className="muted small">{t.subtopics.filter(s => s.generatedAt).length}/{t.subtopics.length}</span></summary>
            <ul>
              {t.subtopics.map(s => (
                <li key={s.key}>
                  <button
                    className={`link-btn ${t.key === topic && s.key === subKey ? 'active' : ''}`}
                    onClick={() => setParams({ topic: t.key, sub: s.key })}
                  >
                    <span className={`dot ${s.generatedAt ? 'dot-ok' : 'dot-off'}`} />{s.title}
                  </button>
                </li>
              ))}
            </ul>
          </details>
        ))}
      </aside>

      <div className="study-body">
        <header className="page-head row-between">
          <div>
            <p className="muted small">{current?.title}</p>
            <h1>{subTitle ?? 'Material de estudio'}</h1>
          </div>
          {lesson && (
            <button className="btn btn-ghost" disabled={!!loading || !status?.aiConfigured} onClick={generate}>Regenerar</button>
          )}
        </header>

        {status && !status.aiConfigured && !lesson && <SetupNotice setup={status.setup} />}
        <ErrorBox error={error} />

        {!lesson && subKey && (
          <div className="panel">
            <p>Esta lección todavía no existe. Se genera una vez con IA y queda guardada.</p>
            <button className="btn btn-primary" disabled={!!loading || !status?.aiConfigured} onClick={generate}>Generar lección</button>
            {loading && <p className="muted">{loading}</p>}
          </div>
        )}
        {loading && lesson && <p className="muted">{loading}</p>}

        {c && (
          <article className="lesson">
            {c.explanation ? (
              <section className="panel explain">
                <div className="explain-step">
                  <h4>Definición</h4>
                  <p className="explain-definition">{c.explanation.definition_es}</p>
                </div>
                <div className="explain-step">
                  <h4>Cuándo y por qué</h4>
                  <p>{c.explanation.when_why_es}</p>
                </div>
                <div className="explain-step">
                  <h4>Trade-off</h4>
                  <p>{c.explanation.trade_off_es}</p>
                </div>
                <div className="explain-step">
                  <h4>Ejemplo</h4>
                  <p>{c.explanation.example_es}</p>
                  {c.explanation.example_code && <CodeBlock code={c.explanation.example_code} lang={c.explanation.example_lang} />}
                </div>
              </section>
            ) : (
              <section className="panel">
                <h2>Explicación</h2>
                <p>{c.summary_es}</p>
                <div className="notice">
                  Esta lección usa el formato anterior (un párrafo denso). Pulsa <strong>Regenerar</strong> para obtenerla con definición → cuándo/por qué → trade-off → ejemplo, conceptos y glosario.
                </div>
              </section>
            )}

            {c.key_concepts && c.key_concepts.length > 0 && (
              <section className="panel">
                <h2>Conceptos clave</h2>
                <table className="table concepts">
                  <thead><tr><th>Concepto</th><th>Qué es</th><th>Cuándo usarlo</th></tr></thead>
                  <tbody>
                    {c.key_concepts.map(k => (
                      <tr key={k.name}>
                        <td><code>{k.name}</code></td>
                        <td>{k.plain_es}</td>
                        <td className="muted">{k.use_when_es}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </section>
            )}

            {c.glossary && c.glossary.length > 0 && (
              <section className="panel">
                <h2>Glosario</h2>
                <dl className="glossary">
                  {c.glossary.map(g => (
                    <div key={g.term}>
                      <dt>{g.term}</dt>
                      <dd>{g.meaning_es}</dd>
                    </div>
                  ))}
                </dl>
              </section>
            )}

            <h2 className="layer-title">Para profundizar</h2>
            <section className="panel">
              <h2>Lo que separa a un senior/lead</h2>
              <ul>{c.senior_details_es.map((d, i) => <li key={i}>{d}</li>)}</ul>
            </section>
            <section className="panel">
              <h2>Ejemplos de código</h2>
              {c.code_examples.map((e, i) => (
                <div key={i} className="example">
                  <h3>{e.title}</h3>
                  <CodeBlock code={e.code} lang={e.lang} />
                  <p>{e.explanation_es}</p>
                </div>
              ))}
            </section>
            <section className="panel">
              <h2>Errores comunes</h2>
              <ul>{c.common_mistakes_es.map((d, i) => <li key={i}>{d}</li>)}</ul>
            </section>
            <section className="panel">
              <h2>Preguntas típicas de entrevista</h2>
              {c.interview_qa.map((qa, i) => (
                <details key={i} className="qa">
                  <summary lang="en">{qa.question_en}</summary>
                  <div className="model">
                    <div className="model-head"><h4>Respuesta modelo</h4><SpeakButton text={qa.answer_en} /></div>
                    <p lang="en">{qa.answer_en}</p>
                  </div>
                </details>
              ))}
            </section>
            <section className="panel">
              <h2>Términos clave</h2>
              <table className="table">
                <thead><tr><th>Término</th><th>Pronunciación</th><th>IPA</th><th>Tip</th><th /></tr></thead>
                <tbody>
                  {c.key_terms.map(t => (
                    <tr key={t.term}>
                      <td lang="en"><strong>{t.term}</strong></td>
                      <td className="respelling">{t.respelling}</td>
                      <td className="muted">/{t.ipa}/</td>
                      <td>{t.tip_es}</td>
                      <td><SpeakButton text={t.term} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <p className="muted small">Estos términos se agregaron a la práctica de Pronunciación.</p>
            </section>
            <p className="muted small">Generada: {new Date(lesson.createdAt).toLocaleString('es')}</p>
          </article>
        )}
      </div>
    </div>
  )
}
