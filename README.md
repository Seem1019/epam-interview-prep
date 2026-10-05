# EPAM Interview Prep (.NET Full-stack L3)

App local para preparar la entrevista técnica de EPAM para .NET Full-stack (nivel L3, senior alto / casi lead). Incluye:

- Un simulador de entrevista con IA y voz.
- Evaluaciones con repetición espaciada.
- Lecciones por tema de la guía oficial.
- Práctica de pronunciación de términos técnicos en inglés.
- Un plan de estudio que se recalcula con tus resultados.

Stack:

- **Backend:** ASP.NET Core 10 (Minimal API) + EF Core 10 + SQLite.
- **Frontend:** React 19 + TypeScript + Vite.
- **IA:** Claude, siempre llamado desde el backend. Puedes usarlo con tu suscripción de Claude (Claude Code) o con una API key de Anthropic.

> Todo corre en tu máquina. Tu perfil, tus respuestas y tu progreso se guardan en una base SQLite local (`src/EpamPrep.Api/epam-prep.db`) que **no** se sube al repo.

---

## 1. Requisitos

| Herramienta | Versión | Para qué |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10 | Backend y tests |
| [Node.js](https://nodejs.org/) | 24 (LTS reciente) | Frontend y script de arranque |
| Git | cualquiera | Clonar el repo |
| Chrome o Edge | reciente | Responder por voz (Firefox no soporta `SpeechRecognition`; ahí respondes escribiendo) |
| Cuenta Claude Pro/Max **o** API key de Anthropic | — | Las funciones con IA (ver paso 3) |

Verifica las versiones:

```bash
dotnet --version   # 10.x
node --version     # v24.x
```

## 2. Clonar

```bash
git clone <url-de-este-repo> epam-interview-prep
cd epam-interview-prep
```

## 3. Configurar la IA (elige una opción)

Sin IA la app igual funciona: quizzes de opción múltiple y "what's the output", pronunciación y plan de estudio. La IA hace falta para el simulador, las lecciones y la calificación de respuestas abiertas.

### Opción A: tu suscripción de Claude (por defecto, sin API key)

El backend ejecuta **Claude Code** en modo headless (`claude -p`) con **tu** cuenta Claude Pro/Max. No pagas por uso: consume los límites de tu plan.

1. Instala Claude Code: <https://docs.claude.com/en/docs/claude-code>.
2. Inicia sesión una vez desde una terminal:

   ```bash
   claude
   ```

   Dentro escribe `/login` y entra con tu cuenta.
3. Listo. `appsettings.json` ya trae `"Provider": "cli"`.

### Opción B: API de Anthropic (pago por uso)

1. Crea tu propia API key en <https://console.anthropic.com/>.
2. En `src/EpamPrep.Api/appsettings.json` cambia `"Provider": "cli"` por `"Provider": "api"`.
3. Guarda la clave **fuera del repo**, con una de estas dos opciones:

   ```bash
   # Opción 1: user-secrets (queda en tu perfil de usuario, nunca en el proyecto)
   dotnet user-secrets set "Anthropic:ApiKey" "<tu-clave>" --project src/EpamPrep.Api
   ```

   ```bash
   # Opción 2: variable de entorno (Windows; abre una terminal nueva después)
   setx ANTHROPIC_API_KEY "<tu-clave>"
   ```

> **Nunca** pongas la clave en `appsettings.json`, en un `.env` versionado ni en el código. El `.gitignore` ya excluye `.env`, `secrets.json` y `appsettings.*.local.json`, pero lo más seguro es user-secrets o la variable de entorno.

## 4. Correr la app

```bash
npm start
```

Abre **http://localhost:5173**.

La primera vez el script:

1. Instala las dependencias del frontend.
2. Compila la API y crea la base SQLite con las migraciones de EF Core.
3. Carga el banco de preguntas y los términos de pronunciación.

Para cerrar la app, usa `Ctrl+C`.

Si solo quieres ver la interfaz sin gastar IA (respuestas simuladas, base separada `epam-prep.demo.db`):

```bash
npm run demo
```

Otros comandos:

```bash
npm test        # 41 tests xUnit con un cliente de IA falso (no gastan API)
npm run build   # compila backend y frontend
```

Puertos: API en `:5180` y frontend en `:5173` (Vite reenvía `/api` a la API). Si alguno está ocupado, el script te avisa.

## 5. Personalízala para ti (importante)

La app viene configurada con el contexto de quien la creó: su fecha de entrevista y los niveles de su entrevista anterior con EPAM. Antes de estudiar, ajusta esto a tu caso:

| Qué | Dónde |
|---|---|
| Tu CV / experiencia (para preguntas tipo "Tell me about a project where…") | Pantalla **Perfil** dentro de la app (se guarda solo en tu base local) |
| Fecha y hora de tu entrevista (cuenta regresiva y plan de estudio) | `src/EpamPrep.Api/appsettings.json` → `Interview:Date` |
| Tus niveles anteriores y objetivos por competencia | `src/EpamPrep.Api/Domain/Catalog.cs` (`Previous`, `Target`) |
| Contexto que lee la IA: fecha, días restantes, feedback anterior, objetivos | `prompts/shared/epam-context.md`: secciones *Situation* y *Candidate's previous EPAM results* |

Si es tu primera entrevista con EPAM, deja los niveles anteriores en `Level.None` y borra la tabla de resultados anteriores en `epam-context.md`.

Los prompts se leen en cada llamada, así que los cambios en `prompts/` aplican sin reiniciar. Los cambios en `appsettings.json` y en `Catalog.cs` sí requieren reiniciar (`Ctrl+C` y `npm start`).

## 6. Cómo estudiar con la app (sugerencia)

1. **Inicio:** revisa la tabla de competencias y el plan del día.
2. **Estudio:** lee las lecciones de tus temas más débiles primero.
3. **Evaluaciones:** haz quizzes cada día. Las preguntas falladas vuelven solas.
4. **Simulador → Bloque rápido:** practica un tema en 15–20 min con feedback tras cada respuesta. Responde en voz alta y en inglés, con una meta de 60–90 segundos.
5. **Pronunciación:** repasa los términos que el simulador no te entendió.
6. **Simulador → Entrevista completa:** haz un simulacro de ~2 horas uno o dos días antes, con el reporte final.

---

## Módulos

| Módulo | Qué hace |
|---|---|
| **Simulador** | Entrevistador senior de EPAM en inglés. *Bloque rápido* (un tema, ~18 min, feedback tras cada respuesta) o *Entrevista completa* (~120 min, 14 bloques, feedback al final). Respuesta por voz (Web Speech API, `en-US`) o escrita, temporizador con meta de 60–90 s. Reporte con nivel por competencia y evidencia, comparación con tu feedback anterior, preguntas falladas y respuestas modelo de 30–45 s. |
| **Evaluaciones** | Opción múltiple, "what's the output", respuesta abierta y code review (estas dos calificadas por IA). Las falladas vuelven con repetición espaciada (SM-2 comprimido: 10 min → 1 día → 3 días → ×ease). Puntaje y nivel estimado por tema. |
| **Estudio** | Lecciones por subtema de la guía, generadas una vez y guardadas (botón para regenerar): explicación, detalles senior, código, errores comunes, preguntas con respuestas modelo y términos clave con pronunciación. |
| **Pronunciación** | Términos que los hispanohablantes suelen pronunciar mal, con respelling, IPA, botón para escuchar (`speechSynthesis`) y para decirlo (reconocimiento). Se agregan solos los términos de las lecciones y los que el simulador no reconoció. |
| **Inicio** | Cuenta regresiva, tabla de competencias (anterior / actual / objetivo) y plan día por día que prioriza React, JavaScript y GenAI, con simulacro completo el sábado. |
| **Perfil** | Tu CV o resumen de experiencia. Si está vacío, el simulador no inventa nada sobre ti. |

La voz funciona en **Chrome o Edge** (Firefox no implementa `SpeechRecognition`; ahí respondes escribiendo).

## Cómo funciona el simulador

Cada respuesta pasa por dos llamadas a Claude:

1. **Evaluador (oculto):** devuelve JSON con esquema fijo (structured outputs): nivel demostrado, nota 0–10, justificación, decisión (`go_deeper` / `clarify` / `next_topic`), feedback, concisión con versión corta y respuesta modelo.
2. **Controlador (C#, determinista):** `InterviewFlow.Decide` combina la recomendación del evaluador con límites de tiempo por bloque y profundidad máxima.
3. **Entrevistador:** recibe una "controller note" con la decisión y genera la siguiente pregunta en streaming (SSE).

Los system prompts usan prompt caching: contexto EPAM + rúbrica → instrucciones del rol → tu perfil, cada bloque con su breakpoint. El historial de la entrevista también se cachea de forma incremental.

## Ajustar los prompts

Todo está en [`prompts/`](prompts/):

- `shared/epam-context.md`: qué es L3, la escala con su rúbrica, el feedback anterior del candidato, la guía oficial y las reglas de idioma. Lo usan todas las llamadas.
- `interviewer.md`, `evaluator.md`, `report.md`, `quiz-grader.md`, `quiz-generator.md`, `lesson.md`: un archivo por rol.
- `schemas/*.json`: esquemas JSON de las salidas estructuradas. Si cambias campos, actualiza `AiResultParser`.

Los archivos se leen en cada llamada: los cambios aplican sin reiniciar.

## Configuración (`src/EpamPrep.Api/appsettings.json`)

- `Anthropic:Model`: modelo (`claude-opus-5-5`).
- `Anthropic:Effort:*`: esfuerzo por rol. El entrevistador va en `low` para responder rápido y el reporte en `high`.
- `Anthropic:UseServerSideFallback`: si el modelo rechaza una solicitud, la API la reintenta en otro modelo (`fallbacks: "default"`). Ponlo en `false` si tu cuenta no tiene acceso a esa beta.
- `Interview:Date`, `FullModeMinutes`, `QuickModeMinutes`, `MaxDepthPerTopic`.

## Tests

```bash
npm test
```

41 tests xUnit con un cliente de IA falso (no gastan API): parseo y saneo de calificaciones, repetición espaciada, controlador de la entrevista, plan de estudio y flujos de servicio sobre SQLite en memoria con las migraciones reales.

## Estructura

```
prompts/                  system prompts y esquemas JSON (editables)
src/EpamPrep.Api/
  Ai/                     IClaudeClient (puerto), adaptador del SDK, cliente demo, parsers
  Domain/                 competencias, subtemas, niveles (catálogo de la guía)
  Data/                   DbContext, entidades, migraciones, seed (preguntas + términos)
  Features/               Interview, Quiz, Lessons, Pronunciation, Dashboard
  Endpoints.cs            Minimal API
tests/EpamPrep.Tests/     xUnit
web/                      React + TS (Vite, proxy /api → :5180)
scripts/start.mjs         arranque de backend + frontend
```
