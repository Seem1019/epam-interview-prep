import { useCallback, useEffect, useRef, useState } from 'react'

// Web Speech API types are not in lib.dom for every browser; keep a minimal local shape.
interface RecognitionResult { isFinal: boolean; 0: { transcript: string } }
interface RecognitionEvent { resultIndex: number; results: ArrayLike<RecognitionResult> }
interface Recognition {
  lang: string
  continuous: boolean
  interimResults: boolean
  maxAlternatives: number
  start(): void
  stop(): void
  abort(): void
  onresult: ((e: RecognitionEvent) => void) | null
  onerror: ((e: { error: string }) => void) | null
  onend: (() => void) | null
  onaudiostart: (() => void) | null
}

const WIN_H = 'Alternativa que funciona en cualquier navegador: haz clic en el cuadro de texto y pulsa Win+H (dictado de Windows, idioma inglés).'

/** Spanish, actionable message for each Web Speech API error code. */
export function describeError(code: string): string {
  const isBrave = typeof navigator !== 'undefined' && 'brave' in navigator
  switch (code) {
    case 'not-allowed':
    case 'service-not-allowed':
      return 'El navegador bloqueó el micrófono. Haz clic en el icono del candado junto a la URL → Micrófono → Permitir, y recarga la página. ' + WIN_H
    case 'audio-capture':
      return 'No se detectó ningún micrófono. Revisa que esté conectado y seleccionado en Windows (Configuración → Sonido → Entrada). ' + WIN_H
    case 'network':
      return (isBrave
        ? 'Brave no permite el reconocimiento de voz de Google. Abre la app en Chrome o Edge. '
        : 'El reconocimiento de voz del navegador usa un servicio en línea y no respondió. Revisa tu conexión o usa Chrome/Edge (Brave, Opera y Vivaldi no lo soportan). ') + WIN_H
    case 'language-not-supported':
      return 'El navegador no soporta reconocimiento en inglés (en-US). Usa Chrome o Edge. ' + WIN_H
    default:
      return `Error de reconocimiento de voz (${code}). ` + WIN_H
  }
}

function createRecognition(): Recognition | null {
  const w = window as unknown as { SpeechRecognition?: new () => Recognition; webkitSpeechRecognition?: new () => Recognition }
  const Ctor = w.SpeechRecognition ?? w.webkitSpeechRecognition
  return Ctor ? new Ctor() : null
}

export const speechSupported = typeof window !== 'undefined' &&
  ('SpeechRecognition' in window || 'webkitSpeechRecognition' in window)

/**
 * Continuous en-US dictation. `transcript` accumulates final results; `interim` is the live partial.
 * Chrome stops recognition after silence, so it auto-restarts while `listening` is true.
 */
export function useDictation(onFinal?: (text: string) => void) {
  const [listening, setListening] = useState(false)
  const [interim, setInterim] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [hearing, setHearing] = useState(false) // the browser actually opened the microphone
  const recRef = useRef<Recognition | null>(null)
  const wantRef = useRef(false)
  const onFinalRef = useRef(onFinal)
  onFinalRef.current = onFinal

  const stop = useCallback(() => {
    wantRef.current = false
    recRef.current?.stop()
    setListening(false)
    setInterim('')
  }, [])

  const start = useCallback(() => {
    const rec = createRecognition()
    if (!rec) {
      setError('Este navegador no soporta reconocimiento de voz. Usa Chrome o Edge. ' + WIN_H)
      return
    }
    rec.lang = 'en-US'
    rec.continuous = true
    rec.interimResults = true
    rec.maxAlternatives = 1
    rec.onresult = (e) => {
      let partial = ''
      for (let i = e.resultIndex; i < e.results.length; i++) {
        const r = e.results[i]
        if (r.isFinal) onFinalRef.current?.(r[0].transcript.trim())
        else partial += r[0].transcript
      }
      setInterim(partial)
    }
    rec.onerror = (e) => {
      if (e.error === 'no-speech' || e.error === 'aborted') return
      setError(describeError(e.error))
      wantRef.current = false
      setListening(false)
      setHearing(false)
    }
    rec.onaudiostart = () => setHearing(true)
    rec.onend = () => {
      if (wantRef.current) {
        try { rec.start() } catch { /* already started */ }
      } else {
        setListening(false)
        setHearing(false)
      }
    }
    recRef.current = rec
    wantRef.current = true
    setError(null)
    setHearing(false)
    try {
      rec.start()
      setListening(true)
    } catch (err) {
      wantRef.current = false
      setError(`No se pudo iniciar el micrófono: ${(err as Error).message}`)
    }
  }, [])

  useEffect(() => () => { wantRef.current = false; recRef.current?.abort() }, [])

  return { listening, hearing, interim, error, start, stop, supported: speechSupported }
}

/** One-shot recognition used by the pronunciation drill. Resolves with the best transcript. */
export function listenOnce(timeoutMs = 6000): Promise<string> {
  return new Promise((resolve, reject) => {
    const rec = createRecognition()
    if (!rec) return reject(new Error('Reconocimiento de voz no soportado en este navegador.'))
    rec.lang = 'en-US'
    rec.continuous = false
    rec.interimResults = false
    rec.maxAlternatives = 1
    let heard = ''
    const timer = setTimeout(() => rec.stop(), timeoutMs)
    rec.onresult = (e) => { heard = e.results[0]?.[0].transcript ?? '' }
    rec.onerror = (e) => { clearTimeout(timer); if (e.error !== 'no-speech') reject(new Error(e.error)) }
    rec.onend = () => { clearTimeout(timer); resolve(heard) }
    rec.start()
  })
}

/** Speak English text with an en-US voice (prefers a natural/online voice when available). */
export function speak(text: string, rate = 0.95) {
  if (!('speechSynthesis' in window)) return
  window.speechSynthesis.cancel()
  const u = new SpeechSynthesisUtterance(text)
  u.lang = 'en-US'
  u.rate = rate
  const voices = window.speechSynthesis.getVoices().filter(v => v.lang === 'en-US')
  u.voice = voices.find(v => /natural|online|google/i.test(v.name)) ?? voices[0] ?? null
  window.speechSynthesis.speak(u)
}
