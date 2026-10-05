import { useEffect, useRef, useState } from 'react'

/** Seconds elapsed while `running` is true; resets when `resetKey` changes. */
export function useStopwatch(running: boolean, resetKey: unknown) {
  const [seconds, setSeconds] = useState(0)
  const startRef = useRef<number | null>(null)

  useEffect(() => {
    setSeconds(0)
    startRef.current = null
  }, [resetKey])

  useEffect(() => {
    if (!running) return
    startRef.current ??= Date.now() - seconds * 1000
    const id = setInterval(() => setSeconds(Math.floor((Date.now() - startRef.current!) / 1000)), 250)
    return () => {
      clearInterval(id)
      startRef.current = null // resume continues from the shown value
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [running, resetKey])

  return seconds
}

export const formatSeconds = (s: number) => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`
