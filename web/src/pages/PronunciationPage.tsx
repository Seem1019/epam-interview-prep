import { useEffect, useState } from 'react'
import { api } from '../api'
import type { PronunciationTerm } from '../api'
import { ErrorBox } from '../components/common'
import { listenOnce, speak, speechSupported } from '../hooks/speech'

const SOURCE_ES: Record<string, string> = { seed: 'base', lesson: 'lección', simulator: 'simulador', manual: 'manual' }

export default function PronunciationPage() {
  const [terms, setTerms] = useState<PronunciationTerm[]>([])
  const [listeningId, setListeningId] = useState<number | null>(null)
  const [results, setResults] = useState<Record<number, { ok: boolean; heard: string }>>({})
  const [filter, setFilter] = useState<'all' | 'weak' | 'simulator'>('all')
  const [newTerm, setNewTerm] = useState({ term: '', respelling: '' })
  const [error, setError] = useState<unknown>(null)

  const load = () => api.get<PronunciationTerm[]>('/api/pronunciation').then(setTerms).catch(setError)
  useEffect(() => { load() }, [])

  async function practice(t: PronunciationTerm) {
    setListeningId(t.id)
    setError(null)
    try {
      const heard = await listenOnce()
      const r = await api.post<{ recognized: boolean; heard: string; term: PronunciationTerm }>(`/api/pronunciation/${t.id}/attempt`, { heard })
      setResults(x => ({ ...x, [t.id]: { ok: r.recognized, heard: heard || '(nada)' } }))
      setTerms(list => list.map(x => (x.id === t.id ? r.term : x)))
    } catch (e) {
      setError(e)
    } finally {
      setListeningId(null)
    }
  }

  async function add() {
    if (!newTerm.term.trim()) return
    await api.post('/api/pronunciation', { term: newTerm.term, respelling: newTerm.respelling })
    setNewTerm({ term: '', respelling: '' })
    load()
  }

  async function remove(id: number) {
    await api.del(`/api/pronunciation/${id}`)
    setTerms(t => t.filter(x => x.id !== id))
  }

  const shown = terms.filter(t =>
    filter === 'all' ? true :
    filter === 'simulator' ? t.source === 'simulator' :
    t.attempts > 0 && t.successes / t.attempts < 0.7)

  return (
    <div className="page">
      <header className="page-head">
        <h1>Pronunciación</h1>
        <p className="muted">Escucha el término (en-US), dilo tú y la app comprueba si el reconocedor lo entendió como la palabra correcta. Los términos que el simulador no reconoció aparecen arriba.</p>
      </header>

      {!speechSupported && <div className="notice notice-warn">Tu navegador no soporta reconocimiento de voz: puedes escuchar los términos, pero para practicar usa Chrome o Edge.</div>}
      <ErrorBox error={error} />

      <div className="row">
        <div className="segmented">
          <button className={filter === 'all' ? 'active' : ''} onClick={() => setFilter('all')}>Todos ({terms.length})</button>
          <button className={filter === 'simulator' ? 'active' : ''} onClick={() => setFilter('simulator')}>Del simulador</button>
          <button className={filter === 'weak' ? 'active' : ''} onClick={() => setFilter('weak')}>Con fallos</button>
        </div>
        <div className="row add-term">
          <input placeholder="Nuevo término" value={newTerm.term} onChange={e => setNewTerm(n => ({ ...n, term: e.target.value }))} />
          <input placeholder="Respelling (opcional)" value={newTerm.respelling} onChange={e => setNewTerm(n => ({ ...n, respelling: e.target.value }))} />
          <button className="btn btn-sm" onClick={add}>Agregar</button>
        </div>
      </div>

      <table className="table terms">
        <thead><tr><th>Término</th><th>Se dice</th><th>Tip</th><th>Aciertos</th><th>Práctica</th><th /></tr></thead>
        <tbody>
          {shown.map(t => {
            const r = results[t.id]
            return (
              <tr key={t.id}>
                <td lang="en">
                  <strong>{t.term}</strong>
                  {t.source !== 'seed' && <span className="tag">{SOURCE_ES[t.source] ?? t.source}</span>}
                </td>
                <td>
                  <span className="respelling">{t.respelling || '—'}</span>
                  {t.ipa && <span className="muted small"> /{t.ipa}/</span>}
                </td>
                <td className="small">{t.tipEs}{t.lastHeard && t.source === 'simulator' && <span className="muted"> Último: "{t.lastHeard}"</span>}</td>
                <td className="muted">{t.attempts ? `${t.successes}/${t.attempts}` : '—'}</td>
                <td className="actions">
                  <button className="btn btn-ghost btn-sm" onClick={() => speak(t.term, 0.85)}>Escuchar</button>
                  <button className="btn btn-sm" disabled={!speechSupported || listeningId !== null} onClick={() => practice(t)}>
                    {listeningId === t.id ? 'Escuchando…' : 'Decirlo'}
                  </button>
                  {r && <span className={r.ok ? 'up small' : 'down small'}>{r.ok ? 'Reconocido' : `Oí "${r.heard}"`}</span>}
                </td>
                <td><button className="btn btn-ghost btn-xs" onClick={() => remove(t.id)} title="Eliminar">×</button></td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
