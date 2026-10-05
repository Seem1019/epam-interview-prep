import { useEffect, useState } from 'react'
import { api } from '../api'
import type { QuizQuestion, QuizResult, TopicStats } from '../api'
import { LEVELS } from '../api'
import { useAppData } from '../App'
import { CodeBlock, ErrorBox, FeedbackCard, LevelBadge, SetupNotice, SpeakButton } from '../components/common'
import { useDictation } from '../hooks/speech'

const TYPE_ES: Record<string, string> = {
  multiple_choice: 'Opción múltiple',
  output: "What's the output",
  open: 'Respuesta abierta',
  code_review: 'Code review',
}

interface Answered { q: QuizQuestion; result: QuizResult; chosen: number | null }

export default function QuizPage() {
  const { status, catalog } = useAppData()
  const [stats, setStats] = useState<TopicStats[]>([])
  const [topic, setTopic] = useState('')
  const [level, setLevel] = useState('')
  const [count, setCount] = useState(8)
  const [questions, setQuestions] = useState<QuizQuestion[] | null>(null)
  const [index, setIndex] = useState(0)
  const [chosen, setChosen] = useState<number | null>(null)
  const [text, setText] = useState('')
  const [result, setResult] = useState<QuizResult | null>(null)
  const [history, setHistory] = useState<Answered[]>([])
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [info, setInfo] = useState<string | null>(null)
  const dictation = useDictation(t => setText(d => (d ? d.trimEnd() + ' ' : '') + t))

  const loadStats = () => api.get<TopicStats[]>('/api/quiz/stats').then(setStats).catch(setError)
  useEffect(() => { loadStats() }, [])

  async function start(reviewsOnly = false) {
    setBusy(reviewsOnly ? 'Cargando repasos…' : 'Preparando preguntas…')
    setError(null)
    setInfo(null)
    try {
      const r = await api.post<{ questions: QuizQuestion[] }>('/api/quiz/start', {
        topicKey: topic || null, level: level || null, count, reviewsOnly,
      })
      if (r.questions.length === 0) {
        setInfo(reviewsOnly ? 'No hay repasos pendientes ahora mismo.' : 'No hay preguntas con ese filtro. Genera nuevas con IA.')
        return
      }
      setQuestions(r.questions)
      setIndex(0)
      setHistory([])
      resetAnswer()
    } catch (e) {
      setError(e)
    } finally {
      setBusy(null)
    }
  }

  async function generate() {
    if (!topic) return setInfo('Elige un tema para generar preguntas.')
    setBusy('Generando preguntas con IA (30–60 s)…')
    setError(null)
    try {
      const r = await api.post<{ created: number }>('/api/quiz/generate', { topicKey: topic, level: level || 'Advanced', count: 5 })
      setInfo(`${r.created} preguntas nuevas agregadas.`)
      loadStats()
    } catch (e) {
      setError(e)
    } finally {
      setBusy(null)
    }
  }

  function resetAnswer() {
    setChosen(null)
    setText('')
    setResult(null)
    dictation.stop()
  }

  async function check() {
    const q = questions![index]
    setBusy(q.options.length ? null : 'Calificando con IA…')
    setError(null)
    dictation.stop()
    try {
      const r = await api.post<QuizResult>('/api/quiz/answer', {
        questionId: q.id, optionIndex: chosen, answer: text, wasReview: q.isReview,
      })
      setResult(r)
      setHistory(h => [...h, { q, result: r, chosen }])
    } catch (e) {
      setError(e)
    } finally {
      setBusy(null)
    }
  }

  function next() {
    if (!questions) return
    if (index + 1 >= questions.length) {
      setQuestions(null)
      loadStats()
      return
    }
    setIndex(i => i + 1)
    resetAnswer()
  }

  const totalDue = stats.reduce((s, x) => s + x.dueReviews, 0)

  // ---------- runner ----------
  if (questions) {
    const q = questions[index]
    const needsText = q.options.length === 0
    const needsAi = q.type === 'open' || q.type === 'code_review'
    return (
      <div className="page">
        <header className="page-head row-between">
          <div>
            <h1>Evaluación</h1>
            <p className="muted">Pregunta {index + 1} de {questions.length}</p>
          </div>
          <button className="btn btn-ghost" onClick={() => { setQuestions(null); loadStats() }}>Salir</button>
        </header>

        <section className="panel question">
          <div className="row tags">
            <span className="tag">{q.topicTitle}</span>
            <LevelBadge level={q.level} />
            <span className="tag">{TYPE_ES[q.type]}</span>
            {q.isReview && <span className="tag tag-review">Repaso</span>}
            {q.subtopic && <span className="muted small">{q.subtopic}</span>}
          </div>
          <p className="prompt" lang="en">{q.prompt}</p>
          {q.code && <CodeBlock code={q.code} lang={q.codeLang} />}

          {q.options.length > 0 && (
            <div className="options">
              {q.options.map((o, i) => {
                const state = result
                  ? i === result.correctOption ? 'correct' : i === chosen ? 'wrong' : ''
                  : i === chosen ? 'selected' : ''
                return (
                  <label key={i} className={`option ${state}`}>
                    <input type="radio" name="opt" disabled={!!result} checked={chosen === i} onChange={() => setChosen(i)} />
                    <span lang="en">{o}</span>
                  </label>
                )
              })}
            </div>
          )}

          {needsText && (
            <>
              {needsAi && dictation.supported && (
                <div className="row">
                  <button className={`btn btn-sm ${dictation.listening ? 'btn-recording' : ''}`} onClick={() => dictation.listening ? dictation.stop() : dictation.start()} disabled={!!result}>
                    {dictation.listening ? 'Detener micrófono' : 'Dictar en inglés'}
                  </button>
                </div>
              )}
              <textarea
                rows={needsAi ? 7 : 2}
                value={text + (dictation.interim ? ' ' + dictation.interim : '')}
                onChange={e => setText(e.target.value)}
                disabled={!!result}
                placeholder={needsAi ? 'Responde como lo harías en la entrevista (60–90 s).' : 'Escribe la salida exacta.'}
                lang="en"
              />
              {needsAi && !status?.aiConfigured && <SetupNotice compact setup={status?.setup} />}
            </>
          )}

          {!result && (
            <div className="row">
              <button className="btn btn-primary" disabled={busy !== null || (q.options.length > 0 ? chosen === null : !text.trim())} onClick={check}>
                {needsAi ? 'Enviar para calificar' : 'Comprobar'}
              </button>
              {busy && <span className="muted">{busy}</span>}
            </div>
          )}
          <ErrorBox error={error} />
        </section>

        {result && (
          <section className={`panel result ${result.passed ? 'result-ok' : 'result-bad'}`}>
            <h3>{result.passed ? 'Correcto' : needsAi ? 'Por debajo del nivel' : 'Incorrecto'} <span className="muted">· {result.score}/100</span></h3>
            {result.expectedOutput && <p>Salida esperada: <code>{result.expectedOutput}</code></p>}
            {result.grade ? <FeedbackCard grade={result.grade} /> : (
              <>
                {result.explanationEs && <p>{result.explanationEs}</p>}
                {result.modelAnswerEn && (
                  <div className="model">
                    <div className="model-head"><h4>Respuesta modelo (30–45 s)</h4><SpeakButton text={result.modelAnswerEn} /></div>
                    <p lang="en">{result.modelAnswerEn}</p>
                  </div>
                )}
              </>
            )}
            {result.nextReviewAt && (
              <p className="muted small">Vuelve a repaso: {new Date(result.nextReviewAt).toLocaleString('es')}</p>
            )}
            <button className="btn btn-primary" onClick={next}>{index + 1 >= questions.length ? 'Terminar' : 'Siguiente'}</button>
          </section>
        )}
      </div>
    )
  }

  // ---------- setup ----------
  const lastRun = history.length > 0 ? Math.round(history.reduce((s, h) => s + h.result.score, 0) / history.length) : null
  return (
    <div className="page">
      <header className="page-head">
        <h1>Evaluaciones</h1>
        <p className="muted">Opción múltiple, "what's the output", respuesta abierta y code review calificados por IA. Las falladas vuelven con repetición espaciada.</p>
      </header>

      {lastRun !== null && <div className="notice">Última ronda: {history.filter(h => h.result.passed).length}/{history.length} aprobadas · promedio {lastRun}/100.</div>}

      <section className="panel">
        <div className="form-grid">
          <label className="field">
            <span>Tema</span>
            <select value={topic} onChange={e => setTopic(e.target.value)}>
              <option value="">Todos</option>
              {catalog.map(c => <option key={c.key} value={c.key}>{c.title}</option>)}
            </select>
          </label>
          <label className="field">
            <span>Nivel</span>
            <select value={level} onChange={e => setLevel(e.target.value)}>
              <option value="">Todos</option>
              {LEVELS.map(l => <option key={l} value={l}>{l}</option>)}
            </select>
          </label>
          <label className="field">
            <span>Preguntas</span>
            <input type="number" min={1} max={20} value={count} onChange={e => setCount(Number(e.target.value))} />
          </label>
        </div>
        <div className="row">
          <button className="btn btn-primary" disabled={busy !== null} onClick={() => start(false)}>Empezar</button>
          <button className="btn" disabled={busy !== null || totalDue === 0} onClick={() => start(true)}>Repasos pendientes ({totalDue})</button>
          <button className="btn btn-ghost" disabled={busy !== null || !status?.aiConfigured} onClick={generate}>Generar 5 preguntas con IA</button>
          {busy && <span className="muted">{busy}</span>}
        </div>
        {info && <p className="muted">{info}</p>}
        <ErrorBox error={error} />
      </section>

      <section className="panel">
        <h2>Puntaje por tema</h2>
        <table className="table">
          <thead><tr><th>Tema</th><th>Preguntas</th><th>Intentos</th><th>Promedio</th><th>Nivel (quizzes)</th><th>Repasos</th></tr></thead>
          <tbody>
            {stats.map(s => (
              <tr key={s.key} className="clickable" onClick={() => setTopic(s.key)}>
                <td>{s.title}</td>
                <td>{s.questionCount}</td>
                <td>{s.attempts}</td>
                <td>{s.averageScore ?? '—'}</td>
                <td><LevelBadge level={s.quizLevel} /></td>
                <td>{s.dueReviews || '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  )
}
