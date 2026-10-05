import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import rehypeHighlight from 'rehype-highlight'
import type { Assessment, Level, QuizGrade } from '../api'
import { LEVEL_ES } from '../api'
import { speak } from '../hooks/speech'

export function Markdown({ text }: { text: string }) {
  return (
    <div className="md">
      <ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[[rehypeHighlight, { detect: true, ignoreMissing: true }]]}>
        {text}
      </ReactMarkdown>
    </div>
  )
}

export function CodeBlock({ code, lang }: { code: string; lang?: string | null }) {
  return <Markdown text={'```' + (lang ?? '') + '\n' + code + '\n```'} />
}

export function LevelBadge({ level }: { level: Level | null | undefined }) {
  const l = level ?? 'None'
  return <span className={`level level-${l.toLowerCase()}`}>{LEVEL_ES[l]}</span>
}

export function ScorePill({ score, max }: { score: number; max: number }) {
  const ratio = score / max
  const tone = ratio >= 0.7 ? 'ok' : ratio >= 0.5 ? 'warn' : 'bad'
  return <span className={`score score-${tone}`}>{score}/{max}</span>
}

export function SetupNotice({ setup, compact, title }: { setup?: string[]; compact?: boolean; title?: string }) {
  const lines = setup ?? [
    'Inicia sesión una vez con tu cuenta Claude: claude   y luego   /login',
    'Reinicia la app con: npm start',
  ]
  return (
    <div className="notice notice-warn">
      <strong>{title ?? 'La IA no está disponible.'}</strong>
      {!compact && <p>El simulador, la calificación de respuestas abiertas, las lecciones y la generación de preguntas necesitan Claude. Las preguntas de opción múltiple y "what's the output", la pronunciación y el plan funcionan sin IA.</p>}
      <pre className="setup">{lines.join('\n')}</pre>
    </div>
  )
}

export function SpeakButton({ text, label = 'Escuchar' }: { text: string; label?: string }) {
  return <button type="button" className="btn btn-ghost btn-sm" onClick={() => speak(text)}>{label}</button>
}

const CONCISION_ES: Record<string, string> = {
  concise: 'Concisa',
  acceptable: 'Aceptable',
  too_long: 'Demasiado larga',
  too_short: 'Demasiado corta',
}

/** Coaching card shared by the quick-mode simulator and graded quiz answers. */
export function FeedbackCard({ assessment, grade }: { assessment?: Assessment; grade?: QuizGrade }) {
  const a = assessment
  const g = grade
  const concision = a?.concision ?? g?.concision
  const model = a?.modelAnswerEn ?? g?.modelAnswerEn ?? ''
  return (
    <div className="feedback">
      <div className="feedback-head">
        {a && <ScorePill score={a.score} max={10} />}
        {g && <ScorePill score={g.score} max={100} />}
        <LevelBadge level={a?.levelDemonstrated ?? g?.levelDemonstrated} />
        {concision && <span className={`tag tag-${concision.verdict}`}>{CONCISION_ES[concision.verdict] ?? concision.verdict}</span>}
      </div>
      <p className="justification">{a?.justificationEs ?? g?.justificationEs}</p>
      {a?.feedbackEs && <p>{a.feedbackEs}</p>}
      {g && g.missingPointsEs.length > 0 && (
        <>
          <h4>Faltó</h4>
          <ul>{g.missingPointsEs.map((m, i) => <li key={i}>{m}</li>)}</ul>
        </>
      )}
      {concision?.commentEs && <p className="muted">{concision.commentEs}</p>}
      {concision?.shortVersionEn && (
        <div className="model">
          <div className="model-head"><h4>Tu respuesta, versión corta</h4><SpeakButton text={concision.shortVersionEn} /></div>
          <p lang="en">{concision.shortVersionEn}</p>
        </div>
      )}
      {model && (
        <div className="model">
          <div className="model-head"><h4>Respuesta modelo (30–45 s)</h4><SpeakButton text={model} /></div>
          <p lang="en">{model}</p>
        </div>
      )}
      {a && a.misrecognizedTerms.length > 0 && (
        <p className="muted">
          Términos que el reconocedor no entendió (agregados a Pronunciación):{' '}
          {a.misrecognizedTerms.map(m => `"${m.heard}" → ${m.intended}`).join(', ')}
        </p>
      )}
    </div>
  )
}

export function ErrorBox({ error }: { error: unknown }) {
  if (!error) return null
  const e = error as { message?: string; code?: string; setup?: string[] }
  if (e.code === 'ai_not_configured') return <SetupNotice setup={e.setup} title={e.message} />
  if (e.code === 'ai_login_required') {
    return (
      <SetupNotice
        compact
        title="Claude Code no tiene sesión iniciada."
        setup={['En una terminal:  claude', 'Dentro de Claude Code:  /login   (con tu cuenta Claude Pro)', 'Luego vuelve aquí y reintenta (no hace falta reiniciar la app).']}
      />
    )
  }
  return <div className="notice notice-bad">{e.message ?? String(error)}</div>
}
