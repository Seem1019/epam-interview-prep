// One command to run everything: `npm start`.
// Starts the ASP.NET Core API (:5180), waits until it answers, then the Vite dev server (:5173, proxies /api).
import { spawn, spawnSync } from 'node:child_process'
import { existsSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const web = join(root, 'web')
const demo = process.argv.includes('--demo')
const isWin = process.platform === 'win32'

if (!existsSync(join(web, 'node_modules'))) {
  console.log('Instalando dependencias del frontend (solo la primera vez)…')
  const r = spawnSync('npm install', { cwd: web, stdio: 'inherit', shell: true })
  if (r.status !== 0) process.exit(r.status ?? 1)
}

// If the front-end is up, the app is already running in another terminal and may be in use: leave it alone.
try {
  await fetch('http://localhost:5173/')
  console.error('La app ya está corriendo (puerto 5173) en otra terminal: ábrela en http://localhost:5173 o ciérrala con Ctrl+C antes de volver a arrancar.')
  process.exit(1)
} catch { /* not running: expected */ }

// A previous run closed without Ctrl+C can leave an orphaned API alive: it locks EpamPrep.Api.exe (build fails)
// and answers on :5180 (we would wrongly think the new one is ready). Only then is it safe to close it.
const stale = isWin
  ? spawnSync('taskkill', ['/IM', 'EpamPrep.Api.exe', '/F', '/T'], { encoding: 'utf8' })
  : spawnSync('pkill', ['-f', 'EpamPrep.Api'], { encoding: 'utf8' })
if (stale.status === 0) console.log('Se cerró una instancia anterior de la API que seguía abierta.')

async function apiUp() {
  try { return (await fetch('http://localhost:5180/api/status')).ok } catch { return false }
}
if (await apiUp()) {
  console.error('El puerto 5180 está ocupado por otro proceso. Ciérralo y vuelve a ejecutar npm start.')
  process.exit(1)
}

const env = { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development' }
if (demo) env.Anthropic__DemoMode = 'true'

const children = []
let stopping = false

function stop(code = 0) {
  if (stopping) return
  stopping = true
  for (const c of children) {
    if (c.exitCode !== null) continue
    if (isWin) spawnSync('taskkill', ['/pid', String(c.pid), '/T', '/F'], { stdio: 'ignore' })
    else c.kill('SIGTERM')
  }
  process.exit(code)
}

function track(child) {
  children.push(child)
  child.on('exit', code => stop(code ?? 0))
  return child
}

process.on('SIGINT', () => stop(0))
process.on('SIGTERM', () => stop(0))

console.log('Compilando y arrancando la API…')
track(spawn('dotnet', ['run', '--project', 'src/EpamPrep.Api', '--launch-profile', 'http'], { cwd: root, env, stdio: 'inherit' }))

const deadline = Date.now() + 180_000
for (;;) {
  if (await apiUp()) break
  if (stopping) process.exit(1)
  if (Date.now() > deadline) {
    console.error('La API no respondió en 3 minutos. Revisa los errores de arriba.')
    stop(1)
  }
  await new Promise(r => setTimeout(r, 1000))
}

track(spawn('npm run dev', { cwd: web, env, stdio: 'inherit', shell: true }))
console.log(`\n  EPAM Prep${demo ? ' (modo demo, sin IA real)' : ''} → http://localhost:5173\n`)
