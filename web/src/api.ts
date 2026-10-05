// Typed client for the local API. All AI calls go through the backend; the browser never sees the key.

export type Level = 'None' | 'Novice' | 'Intermediate' | 'Advanced' | 'Expert'
export const LEVELS: Level[] = ['Novice', 'Intermediate', 'Advanced', 'Expert']
export const LEVEL_ES: Record<Level, string> = {
  None: 'Sin evaluar',
  Novice: 'Novice',
  Intermediate: 'Intermediate',
  Advanced: 'Advanced',
  Expert: 'Expert',
}

export interface Status {
  aiConfigured: boolean
  demoMode: boolean
  provider: 'cli' | 'api' | 'demo'
  model: string
  interviewAt: string
  setup: string[]
}

export interface Subtopic { key: string; title: string }
export interface Competency {
  key: string
  title: string
  previous: Level
  target: Level
  weak: boolean
  subtopics: Subtopic[]
}

export interface Concision { verdict: string; commentEs: string; shortVersionEn: string }
export interface Assessment {
  levelDemonstrated: Level
  score: number
  justificationEs: string
  decision: string
  nextFocus: string
  feedbackEs: string
  concision: Concision
  modelAnswerEn: string
  misrecognizedTerms: { heard: string; intended: string }[]
}

export interface Turn {
  id: number
  sequence: number
  topicKey: string
  depth: number
  question: string
  answer: string | null
  answerSeconds: number | null
  inputMode: string | null
  wordCount: number | null
  score: number | null
  level: Level | null
  assessment: Assessment | null
}

export interface InterviewReport {
  generatedAt: string
  mode: string
  minutes: number
  answers: number
  averageWords: number
  tooLong: number
  ai: {
    summary_es: string
    overall_level: Level
    competencies: { key: string; level: Level; evidence_es: string; to_next_level_es: string; comparison_es: string }[]
    communication: { concision_es: string; pronunciation_es: string }
    top_actions_es: string[]
  }
  catalog: Record<string, { previous: Level; target: Level; title: string }>
  failed: {
    turnId: number
    topicKey: string
    topicTitle: string
    question: string
    answer: string
    score: number
    level: Level
    justificationEs: string
    feedbackEs: string
    modelAnswerEn: string
  }[]
}

export interface InterviewSession {
  id: number
  mode: 'quick' | 'full'
  status: string
  startedAt: string
  endedAt: string | null
  plannedMinutes: number
  currentTopicIndex: number
  topics: { key: string; title: string }[]
  currentTopic: string
  turns: Turn[]
  report: InterviewReport | null
}

export interface InterviewSummary {
  id: number
  mode: string
  topics: string
  status: string
  startedAt: string
  endedAt: string | null
  answers: number
  hasReport: boolean
}

export interface QuizQuestion {
  id: number
  topicKey: string
  topicTitle: string
  subtopic: string | null
  level: Level
  type: 'multiple_choice' | 'output' | 'open' | 'code_review'
  prompt: string
  code: string | null
  codeLang: string | null
  options: string[]
  isReview: boolean
  source: string
}

export interface QuizGrade {
  score: number
  verdict: string
  levelDemonstrated: Level
  justificationEs: string
  missingPointsEs: string[]
  concision: Concision
  modelAnswerEn: string
}

export interface QuizResult {
  score: number
  passed: boolean
  correctOption: number | null
  expectedOutput: string | null
  explanationEs: string | null
  modelAnswerEn: string | null
  grade: QuizGrade | null
  nextReviewAt: string | null
}

export interface TopicStats {
  key: string
  title: string
  attempts: number
  averageScore: number | null
  quizLevel: Level
  dueReviews: number
  questionCount: number
}

export interface PronunciationTerm {
  id: number
  term: string
  respelling: string
  ipa: string | null
  tipEs: string | null
  accepted: string
  source: string
  attempts: number
  successes: number
  lastHeard: string | null
}

export interface PlanTask { key: string; kind: string; topicKey: string | null; title: string; minutes: number; done: boolean }
export interface PlanDay { date: string; label: string; focus: string; tasks: PlanTask[] }
export interface DashboardRow {
  key: string
  title: string
  previous: Level
  quizLevel: Level
  interviewLevel: Level
  current: Level
  currentSource: string
  target: Level
  weak: boolean
  quizAverage: number | null
  attempts: number
  dueReviews: number
}
export interface Dashboard {
  interviewAt: string
  now: string
  aiConfigured: boolean
  profileComplete: boolean
  dueReviews: number
  competencies: DashboardRow[]
  plan: PlanDay[]
}

export interface Lesson {
  topicKey: string
  subtopicKey: string
  createdAt: string
  content: {
    title: string
    /** Current format: definition → when/why → trade-off → example. */
    explanation?: {
      definition_es: string
      when_why_es: string
      trade_off_es: string
      example_es: string
      example_code: string
      example_lang: string
    }
    key_concepts?: { name: string; plain_es: string; use_when_es: string }[]
    glossary?: { term: string; meaning_es: string }[]
    /** Old format (lessons generated before the light explanation existed). */
    summary_es?: string
    senior_details_es: string[]
    code_examples: { title: string; lang: string; code: string; explanation_es: string }[]
    common_mistakes_es: string[]
    interview_qa: { question_en: string; answer_en: string }[]
    key_terms: { term: string; respelling: string; ipa: string; tip_es: string }[]
  }
}

export interface LessonIndexTopic {
  key: string
  title: string
  subtopics: { key: string; title: string; generatedAt: string | null }[]
}

export class ApiError extends Error {
  status: number
  code: string | undefined
  setup: string[] | undefined
  constructor(message: string, status: number, code?: string, setup?: string[]) {
    super(message)
    this.status = status
    this.code = code
    this.setup = setup
  }
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const res = await fetch(url, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (res.status === 204) return undefined as T
  const text = await res.text()
  const data = text ? JSON.parse(text) : undefined
  if (!res.ok) {
    throw new ApiError(data?.message ?? data?.title ?? `Error ${res.status}`, res.status, data?.code, data?.setup)
  }
  return data as T
}

export const api = {
  get: <T>(url: string) => request<T>('GET', url),
  post: <T>(url: string, body?: unknown) => request<T>('POST', url, body ?? {}),
  put: <T>(url: string, body: unknown) => request<T>('PUT', url, body),
  del: (url: string) => request<void>('DELETE', url),
}

export type SseHandler = (event: string, data: any) => void

/** POST + Server-Sent Events over fetch (EventSource only supports GET). */
export async function postSse(url: string, body: unknown, onEvent: SseHandler, signal?: AbortSignal) {
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal,
  })
  if (!res.ok || !res.body) {
    const data = await res.json().catch(() => ({}))
    throw new ApiError(data?.message ?? data?.title ?? `Error ${res.status}`, res.status, data?.code, data?.setup)
  }

  const reader = res.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  for (;;) {
    const { value, done } = await reader.read()
    if (done) break
    buffer += decoder.decode(value, { stream: true })
    let sep: number
    while ((sep = buffer.indexOf('\n\n')) >= 0) {
      const raw = buffer.slice(0, sep)
      buffer = buffer.slice(sep + 2)
      let event = 'message'
      let data = ''
      for (const line of raw.split('\n')) {
        if (line.startsWith('event:')) event = line.slice(6).trim()
        else if (line.startsWith('data:')) data += line.slice(5).trim()
      }
      onEvent(event, data ? JSON.parse(data) : null)
    }
  }
}
