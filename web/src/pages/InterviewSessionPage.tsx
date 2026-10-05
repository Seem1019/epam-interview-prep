import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, postSse } from '../api'
import type { InterviewSession } from '../api'
import { ErrorBox, FeedbackCard, LevelBadge, Markdown, ScorePill } from '../components/common'
import ReportView from '../components/ReportView'
import { speak, useDictation } from '../hooks/speech'
import { formatSeconds, useStopwatch } from '../hooks/useStopwatch'

type Phase = 'loading' | 'evaluating' | 'asking' | 'answering' | 'ended' | 'reporting' | 'finished' | 'stalled'

const stripCode = (s: string) => s.replace(/```[\s\S]*?```/g, ' (see the code) ')
const wordCount = (s: string) => (s.trim().match(/[\p{L}\p{N}][\p{L}\p{N}'’\-.#+]*/gu) ?? []).length

export default function InterviewSessionPage() {
  const { id } = useParams()
  const [session, setSession] = useState<InterviewSession | null>(null)
  const [phase, setPhase] = useState<Phase>('loading')
  const [streamText, setStreamText] = useState('')
  const [topic, setTopic] = useState<{ title: string; index: number; total: number } | null>(null)
  const [draft, setDraft] = useState('')
  const [usedVoice, setUsedVoice] = useState(false)
  const [started, setStarted] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [autoRead, setAutoRead] = useState(false)
  const bottomRef = useRef<HTMLDivElement>(null)
  const startedTurn = useRef(false)

  const dictation = useDictation(text => {
    setDraft(d => (d ? d.trimEnd() + ' ' : '') + text)
    setUsedVoice(true)
  })
  const lastTurnId = session?.turns.at(-1)?.id
  const seconds = useStopwatch(phase === 'answering' && started, lastTurnId)

  const load = useCallback(async () => {
    const s = await api.get<InterviewSession>(`/api/interviews/${id}`)
    setSession(s)
    const t = s.topics[s.currentTopicIndex]
    if (t) setTopic({ title: t.title, index: s.currentTopicIndex, total: s.topics.length })
    return s
  }, [id])

  const runTurn = useCallback(async (body: { answer?: string; seconds?: number; inputMode?: string }) => {
    setError(null)
    setStreamText('')
    setPhase(body.answer ? 'evaluating' : 'asking')
    let ended = false
    let full = ''
    try {
      await postSse(`/api/interviews/${id}/turn`, body, (event, data) => {
        switch (event) {
          case 'status': setPhase(data.phase); break
          case 'topic': setTopic({ title: data.title, index: data.index, total: data.total }); break
          case 'assessment': if (data.visible) load(); break
          case 'delta': full += data.text; setStreamText(t => t + data.text); break
          case 'done': ended = data.ended; break
          case 'error': throw Object.assign(new Error(data.message), { code: data.code })
        }
      })
      await load()
      setStreamText('')
      setDraft('')
      setUsedVoice(false)
      setStarted(false)
      setPhase(ended ? 'ended' : 'answering')
      if (autoRead && full) speak(stripCode(full))
    } catch (e) {
      setError(e)
      setStreamText('')
      const s = await load().catch(() => null)
      const last = s?.turns.at(-1)
      // Answer not stored yet → let the user resend it (draft is kept); otherwise offer to resume.
      setPhase(last && last.answer === null ? 'answering' : 'stalled')
    }
  }, [id, load, autoRead])

  // Initial load: fetch the opening question when the session is new.
  useEffect(() => {
    load().then(s => {
      if (s.report) return setPhase('finished')
      if (s.endedAt) return setPhase('ended')
      const last = s.turns.at(-1)
      if (!last) {
        if (!startedTurn.current) {
          startedTurn.current = true
          runTurn({})
        }
      } else if (last.answer === null) {
        setPhase('answering')
      } else {
        setPhase('stalled') // answered but the next question never arrived (e.g. network error)
      }
    }).catch(e => { setError(e); setPhase('stalled') })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id])

  useEffect(() => { bottomRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' }) }, [streamText, session?.turns.length, phase])

  function submit(answerOverride?: string) {
    const answer = (answerOverride ?? draft).trim()
    if (!answer) return
    dictation.stop()
    runTurn({ answer, seconds, inputMode: usedVoice && !answerOverride ? 'voice' : 'typed' })
  }

  async function finish() {
    if (phase === 'answering' && !confirm('¿Terminar la entrevista y generar el reporte?')) return
    dictation.stop()
    setPhase('reporting')
    setError(null)
    try {
      await api.post(`/api/interviews/${id}/finish`)
      await load()
      setPhase('finished')
    } catch (e) {
      setError(e)
      setPhase('ended')
    }
  }

  function toggleMic() {
    if (dictation.listening) dictation.stop()
    else {
      setStarted(true)
      dictation.start()
    }
  }

  if (!session) return <div className="page"><p className="muted">Cargando…</p><ErrorBox error={error} /></div>

  const quick = session.mode === 'quick'
  const words = wordCount(draft)
  const timeTone = seconds < 60 ? '' : seconds <= 90 ? 'ok' : seconds <= 120 ? 'warn' : 'bad'
  const busy = phase === 'evaluating' || phase === 'asking' || phase === 'reporting'

  return (
    <div className="page interview">
      <header className="page-head row-between">
        <div>
          <Link to="/simulador" className="muted small">← Simulador</Link>
          <h1>{quick ? `Bloque rápido · ${session.topics[0]?.title}` : 'Entrevista completa'}</h1>
          {topic && !quick && <p className="muted">Bloque {topic.index + 1} de {topic.total}: <strong>{topic.title}</strong></p>}
        </div>
        <div className="row">
          <label className="check"><input type="checkbox" checked={autoRead} onChange={e => setAutoRead(e.target.checked)} /> Leer preguntas en voz alta</label>
          {phase !== 'finished' && session.turns.some(t => t.answer) && (
            <button className="btn" disabled={busy} onClick={finish}>Terminar y generar reporte</button>
          )}
        </div>
      </header>

      {phase === 'finished' && session.report ? <ReportView report={session.report} /> : (
        <>
          <div className="transcript">
            {session.turns.map(t => (
              <div key={t.id} className="exchange">
                <div className="bubble interviewer">
                  <div className="bubble-meta">
                    <span>Interviewer</span>
                    <button className="btn btn-ghost btn-xs" onClick={() => speak(stripCode(t.question))}>Escuchar</button>
                  </div>
                  <Markdown text={t.question} />
                </div>
                {t.answer && (
                  <div className="bubble candidate">
                    <div className="bubble-meta">
                      <span>Tú · {t.wordCount} palabras{t.answerSeconds ? ` · ${formatSeconds(t.answerSeconds)}` : ''}{t.inputMode === 'voice' ? ' · voz' : ''}</span>
                      {quick && t.score !== null && <span className="row"><ScorePill score={t.score} max={10} /><LevelBadge level={t.level} /></span>}
                    </div>
                    <p>{t.answer}</p>
                  </div>
                )}
                {quick && t.assessment && <FeedbackCard assessment={t.assessment} />}
              </div>
            ))}
            {streamText && (
              <div className="bubble interviewer streaming">
                <div className="bubble-meta"><span>Interviewer</span></div>
                <Markdown text={streamText} />
              </div>
            )}
            {phase === 'evaluating' && <div className="thinking">{quick ? 'Evaluando tu respuesta…' : 'El entrevistador toma notas…'}</div>}
            {phase === 'asking' && !streamText && <div className="thinking">Preparando la siguiente pregunta…</div>}
            {phase === 'reporting' && <div className="thinking">Generando el reporte final (puede tardar 1–2 minutos)…</div>}
            <div ref={bottomRef} />
          </div>

          <ErrorBox error={error} />

          {phase === 'stalled' && (
            <div className="row">
              <button className="btn btn-primary" onClick={() => runTurn({})}>Reintentar / continuar</button>
            </div>
          )}

          {phase === 'ended' && (
            <div className="panel">
              <p>La entrevista terminó. Genera el reporte para ver tu nivel por competencia, las preguntas falladas y las respuestas modelo.</p>
              <button className="btn btn-primary" onClick={finish}>Generar reporte</button>
            </div>
          )}

          {phase === 'answering' && (
            <div className="answer-panel">
              <div className="answer-toolbar">
                <button className={`btn ${dictation.listening ? 'btn-recording' : 'btn-primary'}`} onClick={toggleMic} disabled={!dictation.supported}>
                  {dictation.listening ? 'Detener micrófono' : 'Responder por voz'}
                </button>
                <div className={`timer timer-${timeTone}`} title="Meta suave: 60–90 segundos">
                  {formatSeconds(seconds)} <span className="muted small">meta 1:00–1:30</span>
                </div>
                <span className={`small ${words > 150 ? 'warn-text' : 'muted'}`}>{words} palabras{words > 150 ? ' · demasiado larga' : ''}</span>
                {dictation.listening && (
                  <span className={`small ${dictation.hearing ? 'up' : 'muted'}`}>
                    {dictation.hearing ? 'Micrófono activo: habla en inglés' : 'Abriendo micrófono… (acepta el permiso si el navegador lo pide)'}
                  </span>
                )}
                {!dictation.supported && <span className="muted small">Este navegador no soporta dictado: usa Chrome/Edge o Win+H en el cuadro de texto.</span>}
              </div>
              {dictation.error && <div className="notice notice-bad">{dictation.error}</div>}
              <textarea
                value={draft + (dictation.interim ? (draft ? ' ' : '') + dictation.interim : '')}
                onChange={e => { setDraft(e.target.value); setStarted(true) }}
                onKeyDown={e => { if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) submit() }}
                placeholder="Habla o escribe tu respuesta en inglés. Ctrl+Enter para enviar."
                rows={6}
                lang="en"
              />
              <div className="row">
                <button className="btn btn-primary" disabled={!draft.trim()} onClick={() => submit()}>Enviar respuesta</button>
                <button className="btn btn-ghost" onClick={() => submit("I don't know.")}>No lo sé</button>
                <span className="muted small">Estructura: definición → cuándo/por qué → trade-off → ejemplo.</span>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  )
}
