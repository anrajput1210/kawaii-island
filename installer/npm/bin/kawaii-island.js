#!/usr/bin/env node
// Kawaii Island for Windows, installed with npm (no .NET needed: dist/ holds a self-contained KawaiiIsland.exe).
//
//   npm i -g kawaii-island            installs per-user and starts it (also: npx kawaii-island)
//   kawaii-island [install|start|stop|uninstall [--purge]|status]
//   kawaii-island event --agent "Gemini CLI" --state working --detail "npm test" [--session id] [--cwd dir]
//   kawaii-island ask "Run the migration?" --agent Aider     exit 0 = allow, 1 = deny, 2 = no answer / island off
//
// Everything stays on this PC: the app lives in %LOCALAPPDATA%\Programs\KawaiiIsland, settings in %LOCALAPPDATA%\KawaiiIsland.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync, spawn } = require('node:child_process');

const EXE = 'KawaiiIsland.exe';
const local = process.env.LOCALAPPDATA || path.join(process.env.USERPROFILE || '', 'AppData', 'Local');
const installDir = process.env.KAWAII_INSTALL_DIR || path.join(local, 'Programs', 'KawaiiIsland');
const installedExe = path.join(installDir, EXE);
const bundledExe = path.join(__dirname, '..', 'dist', EXE);
const dataDir = path.join(local, 'KawaiiIsland');
const shortcut = path.join(process.env.APPDATA || '', 'Microsoft', 'Windows', 'Start Menu', 'Programs', 'Kawaii Island.lnk');
const RUN_KEY = 'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run';

const [command = '', ...rest] = process.argv.slice(2);

function quiet(file, args) {
  try { execFileSync(file, args, { stdio: 'ignore', windowsHide: true }); return true; } catch { return false; }
}

const stop = () => quiet('taskkill', ['/IM', EXE, '/F']);

function start() {
  if (!fs.existsSync(installedExe)) return fail('Kawaii Island is not installed. Run: kawaii-island install');
  spawn(installedExe, [], { detached: true, stdio: 'ignore', windowsHide: false }).unref();
  console.log('Kawaii Island is running (look at the top of your screen; Ctrl+Alt+I opens it).');
}

function install({ launch = true } = {}) {
  if (process.platform !== 'win32') return fail('Kawaii Island for npm is Windows-only for now.');
  if (!fs.existsSync(bundledExe)) return fail('This package has no app build (dist/KawaiiIsland.exe). Install it from npm, or run "npm run build" in installer/npm.');
  stop(); // replacing a running exe fails on Windows
  fs.mkdirSync(installDir, { recursive: true });
  fs.copyFileSync(bundledExe, installedExe);
  const ps = s => s.replace(/'/g, "''"); // PowerShell single-quoted string
  quiet('powershell', ['-NoProfile', '-NonInteractive', '-Command',
    `$s = (New-Object -ComObject WScript.Shell).CreateShortcut('${ps(shortcut)}'); $s.TargetPath = '${ps(installedExe)}'; ` +
    `$s.WorkingDirectory = '${ps(installDir)}'; $s.Description = 'Kawaii Island'; $s.Save()`]);
  console.log(`Installed to ${installDir} (Start menu: Kawaii Island). It starts with Windows; turn that off in Settings → General.`);
  if (launch) start();
}

function uninstall() {
  const purge = rest.includes('--purge');
  stop();
  if (fs.existsSync(installedExe)) quiet(installedExe, ['--uninstall-hooks']); // removes our Claude Code hooks, if any
  quiet('reg', ['delete', RUN_KEY, '/v', 'KawaiiIsland', '/f']);
  fs.rmSync(shortcut, { force: true });
  fs.rmSync(installDir, { recursive: true, force: true });
  if (purge) fs.rmSync(dataDir, { recursive: true, force: true });
  console.log(purge ? 'Kawaii Island and its settings were removed.' : `Kawaii Island was removed. Your settings stay in ${dataDir} (use --purge to delete them).`);
}

function status() {
  const running = quiet('powershell', ['-NoProfile', '-Command', `if (-not (Get-Process ${EXE.replace('.exe', '')} -ErrorAction SilentlyContinue)) { exit 1 }`]);
  console.log(`installed: ${fs.existsSync(installedExe) ? installedExe : 'no'}\nrunning:   ${running ? 'yes' : 'no'}\nport:      ${port()}`);
}

// ---------- agent events (any agentic tool can call these from its hooks) ----------

function port() {
  try {
    const cfg = JSON.parse(fs.readFileSync(path.join(dataDir, 'config.json'), 'utf8').replace(/^﻿/, ''));
    return cfg?.modules?.code?.port || 47811;
  } catch { return 47811; }
}

function flags(args) {
  const out = { _: [] };
  for (let i = 0; i < args.length; i++) {
    if (args[i].startsWith('--')) out[args[i].slice(2)] = args[i + 1] && !args[i + 1].startsWith('--') ? args[++i] : 'true';
    else out._.push(args[i]);
  }
  return out;
}

async function post(route, body, timeoutMs) {
  const res = await fetch(`http://127.0.0.1:${port()}/kawaii/${route}`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), signal: AbortSignal.timeout(timeoutMs),
  });
  return res.text();
}

function eventBody(f, state) {
  return { agent: f.agent || 'Agent', state, detail: f.detail || f._.join(' '), session: f.session, cwd: f.cwd || process.cwd(), project: f.project };
}

async function event() {
  const f = flags(rest);
  if (!f.state) return fail('Usage: kawaii-island event --agent "Gemini CLI" --state working|tool|needs_input|done|idle|end [--detail text]');
  try { await post('event', eventBody(f, f.state), 2000); } catch { /* island not running or coding mode off: stay silent */ }
}

async function ask() {
  const f = flags(rest);
  try {
    const answer = (await post('ask', eventBody(f, 'needs_input'), 125000)).trim();
    console.log(answer || 'no answer');
    process.exit(answer === 'allow' ? 0 : answer === 'deny' ? 1 : 2);
  } catch { console.log('no answer'); process.exit(2); }
}

function fail(message) { console.error(message); process.exitCode = 1; }

// ---------- dispatch ----------

switch (command) {
  case 'postinstall':
    // `npm i -g kawaii-island` installs and starts the app; a local (project) install only prints how.
    if (process.env.npm_config_global === 'true' && fs.existsSync(bundledExe)) install();
    else console.log('Run "npx kawaii-island install" to install Kawaii Island.');
    break;
  case '': fs.existsSync(installedExe) ? start() : install(); break;
  case 'install': install(); break;
  case 'start': start(); break;
  case 'stop': console.log(stop() ? 'Stopped.' : 'Kawaii Island was not running.'); break;
  case 'uninstall': uninstall(); break;
  case 'status': status(); break;
  case 'event': event(); break;
  case 'ask': ask(); break;
  case '--version': case '-v': console.log(require('../package.json').version); break;
  default:
    console.log('kawaii-island [install|start|stop|status|uninstall [--purge]]\n' +
                'kawaii-island event --agent NAME --state working|tool|needs_input|done|idle|end [--detail TEXT] [--session ID] [--cwd DIR]\n' +
                'kawaii-island ask "QUESTION" [--agent NAME]   (exit 0 allow, 1 deny, 2 no answer)');
}
