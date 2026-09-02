#!/usr/bin/env node
// PostToolUse (Write|Edit) quality gate — M3L3 per-edit layer.
//
// trigger  : Claude Code PostToolUse
// matcher  : Write|Edit (see .claude/settings.json)
// handler  : this script — reads the hook event JSON on stdin
// signal   : exit 0 = pass, exit 2 = blocking error (stderr goes into the
//            agent's context so it can self-correct on the next turn)
//
// Node instead of bash+jq: jq is not installed on this machine and node is
// guaranteed (the client is a Vite app). Same four-step pattern.
//
// Routing (test-plan.md §2 risk map + §5 quality gates):
//   src/client/**.{ts,tsx,js,jsx}  -> eslint --fix <file>  +  tsc -b   (~3s + ~5s, parallel)
//   src/server/**Scoring**.cs      -> dotnet test (risks #1/#2 area only, ~16s)
//   everything else                -> no-op

import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const REPO = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const CLIENT = path.join(REPO, 'src', 'client');
const MAX_OUTPUT = 8000; // additionalContext is capped at 10k chars
const TASK_TIMEOUT_MS = 120_000;

const LINTABLE = new Set(['.ts', '.tsx', '.js', '.jsx', '.mts', '.cts']);
// Risk areas #1 (scoring correctness) and #2 (per-league rule isolation).
const SERVER_RISK = /(scoring|matchoutcome)/i;

async function readStdin() {
  const chunks = [];
  for await (const chunk of process.stdin) chunks.push(chunk);
  return Buffer.concat(chunks).toString('utf8');
}

function run(name, cmd, args, cwd) {
  return new Promise((resolve) => {
    const child = spawn(cmd, args, { cwd, shell: false, windowsHide: true });
    let out = '';
    const append = (d) => { if (out.length < MAX_OUTPUT * 2) out += d.toString(); };
    child.stdout.on('data', append);
    child.stderr.on('data', append);
    const timer = setTimeout(() => child.kill(), TASK_TIMEOUT_MS);
    child.on('error', (err) => {
      clearTimeout(timer);
      resolve({ name, code: 0, out: `[hook] could not start ${name}: ${err.message}`, skipped: true });
    });
    child.on('close', (code) => {
      clearTimeout(timer);
      resolve({ name, code: code ?? 1, out });
    });
  });
}

function localBin(rel) {
  const p = path.join(CLIENT, 'node_modules', rel);
  return existsSync(p) ? p : null;
}

function plan(rel, abs) {
  const tasks = [];
  const posix = rel.split(path.sep).join('/');

  if (/^src\/client\//.test(posix) && !/\/(node_modules|dist)\//.test(posix)) {
    if (!LINTABLE.has(path.extname(posix))) return tasks;

    const eslint = localBin('eslint/bin/eslint.js');
    if (eslint) tasks.push(['lint', process.execPath, [eslint, '--fix', abs], CLIENT]);

    const tsc = localBin('typescript/bin/tsc');
    if (tsc) tasks.push(['typecheck', process.execPath, [tsc, '-b'], CLIENT]);
    return tasks;
  }

  if (/^src\/server\//.test(posix) && path.extname(posix) === '.cs') {
    if (/\/(bin|obj)\//.test(posix)) return tasks;
    // Scoped tests only for risk-area edits — a full run on every server file
    // would block the agent loop for ~16s per edit.
    if (SERVER_RISK.test(posix)) {
      tasks.push(['tests', 'dotnet', ['test', 'src/server/prediction-league.slnx'], REPO]);
    }
  }

  return tasks;
}

const raw = await readStdin();
let event = {};
try { event = JSON.parse(raw || '{}'); } catch { process.exit(0); }

const filePath =
  event?.tool_input?.file_path ??
  event?.tool_input?.filePath ?? // Copilot/VS Code payload shape
  event?.tool_response?.filePath;

if (!filePath) process.exit(0);

const abs = path.resolve(filePath);
const rel = path.relative(REPO, abs);
if (!rel || rel.startsWith('..')) process.exit(0); // outside this repo

const tasks = plan(rel, abs);
if (tasks.length === 0) process.exit(0);

const results = await Promise.all(tasks.map(([n, c, a, cwd]) => run(n, c, a, cwd)));
const failed = results.filter((r) => r.code !== 0);

if (failed.length === 0) {
  console.log(`[post-edit] ok: ${results.map((r) => r.name).join(', ')} on ${rel}`);
  process.exit(0);
}

const report = failed
  .map((r) => `--- ${r.name} failed (exit ${r.code}) on ${rel} ---\n${r.out.trim()}`)
  .join('\n\n')
  .slice(0, MAX_OUTPUT);

console.error(`Post-edit quality gate failed. Fix this before continuing.\n\n${report}`);
process.exit(2);
