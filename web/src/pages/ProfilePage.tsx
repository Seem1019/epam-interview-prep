import { useEffect, useState } from 'react'
import { api } from '../api'
import { ErrorBox } from '../components/common'

export default function ProfilePage() {
  const [text, setText] = useState('')
  const [savedAt, setSavedAt] = useState<string | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    api.get<{ cvText: string; updatedAt: string }>('/api/profile')
      .then(p => { setText(p.cvText ?? ''); if (p.cvText) setSavedAt(p.updatedAt) })
      .catch(setError)
  }, [])

  async function save() {
    setSaving(true)
    setError(null)
    try {
      const p = await api.put<{ updatedAt: string }>('/api/profile', { cvText: text })
      setSavedAt(p.updatedAt)
    } catch (e) {
      setError(e)
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="page">
      <header className="page-head">
        <h1>Perfil</h1>
        <p className="muted">
          Pega tu CV o un resumen: años de experiencia, empresas, proyectos, stack, responsabilidades de liderazgo y logros medibles.
          El simulador lo usa para preguntas como "Tell me about a project where you…". Si está vacío, nunca inventa nada sobre ti.
        </p>
      </header>
      <section className="panel">
        <textarea
          className="cv"
          rows={22}
          value={text}
          onChange={e => setText(e.target.value)}
          placeholder={'Ejemplo:\n- 7 años como desarrollador .NET full stack.\n- Empresa X (2021–hoy): API de pagos en ASP.NET Core + Azure Service Bus; lideré la migración de .NET Framework 4.8 a .NET 8...\n- Logros: reduje el p95 de 1.2 s a 300 ms...'}
        />
        <div className="row">
          <button className="btn btn-primary" disabled={saving} onClick={save}>Guardar</button>
          {savedAt && <span className="muted small">Guardado {new Date(savedAt).toLocaleString('es')}</span>}
          <span className="muted small">Se guarda solo en tu base local (SQLite) y se envía a Claude como contexto del simulador.</span>
        </div>
        <ErrorBox error={error} />
      </section>
    </div>
  )
}
