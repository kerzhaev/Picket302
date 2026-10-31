#!/usr/bin/env node
// Обёртка провайдера goose (GLM-5.3-flash и др.) для PromptPilot.
// Зачем: контракт закрывающего вердикта (ИТОГ:) включается только для
// pipeline-маршрутов (P9, worker.py:1924), поэтому goose-отчёты workflow-задач
// могут оставаться без машинно-читаемого хвоста -> executor.invalid_output.
// Эта обёртка даёт тот же канонический хвост, что minimax-exec-wrapper.js v3.4:
//     ... \n AUDIT_FINDINGS_JSON: [...] \n AUDIT_VERDICT: <X> \n ИТОГ: <Y>
// Транспорт: провайдер имеет prompt_stdin=true и шаблон, оканчивающийся на
// '-' (конвенция U17/goose); промпт воркер пишет в STDIN, argv его не содержит
// (P18: промпты >32767 символов роняют CreateProcess).
'use strict';
const { spawnSync } = require('child_process');
const fs = require('fs');

const GOOSE = process.env.PP_GOOSE_EXE || 'C:\\Users\\FIREBAT\\goose\\goose.exe';
const ITOG_ACCEPT_RE = /^ИТОГ:\s*(ГОТОВО|УЖЕ СДЕЛАНО|НУЖЕН ЧЕЛОВЕК|НЕ СМОГ|ПУСТО)(\s|$)/i;
const ITOG_ANY_RE = /ИТОГ:\s*(ГОТОВО|УЖЕ СДЕЛАНО|НУЖЕН ЧЕЛОВЕК|НЕ СМОГ|ПУСТО)/gi;
const RUN_TIMEOUT_MS = 6900 * 1000; // чуть меньше task_timeout воркера (7200с)

const args = process.argv.slice(2);
// Режим stdin: воркер пишет промпт в stdin (prompt_stdin=true); плейсхолдер
// '-' из шаблона сюда доходит, но промптом не является.
let stdinData = '';
try { stdinData = fs.readFileSync(0, 'utf8'); } catch (_) { stdinData = ''; }
let prompt = stdinData;
if (!prompt.trim() && args.length) {
  // Fallback: старый argv-режим (промпт последним argv).
  prompt = args[args.length - 1];
}
if (!prompt.trim()) {
  process.stdout.write('AUDIT_FINDINGS_JSON: []\nAUDIT_VERDICT: REVISION_REQUIRED\nИТОГ: НЕ СМОГ — wrapper goose не получил промпт (ни в stdin, ни в argv)\n');
  process.exit(2);
}

// К-9/P13: транзиентные отказы провайдера/рантайма — переводим в форму,
// которую распознают классификаторы воркера (rate_limited -> requeue-пауза).
const TRANSIENT_RE = /rate limit|429|quota|high demand|overloaded|try again later|failed to connect|ECONNRESET|ETIMEDOUT|socket hang up|Request failed/i;

const r = spawnSync(GOOSE, ['run', '-i', '-'], {
  encoding: 'utf8',
  maxBuffer: 256 * 1024 * 1024,
  timeout: RUN_TIMEOUT_MS,
  cwd: process.cwd(),
  input: prompt,
});

let out = (r.stdout || '').trimEnd();
const errTail = (r.stderr || '').trimEnd();

if (errTail && TRANSIENT_RE.test(errTail)) {
  process.stderr.write('\npromptpilot-goose-wrapper: provider runtime overloaded, try again later (transient failure translated for the worker classifier, K-9/P13)\n');
}

const killed = !!(r.error && r.error.killed);
const exitCode = killed ? -1 : (r.status === null || r.status === undefined ? -1 : r.status);

let itog = null;
let m;
const reAll = new RegExp(ITOG_ANY_RE.source, 'gi');
while ((m = reAll.exec(out)) !== null) itog = m[1].toUpperCase();
if (killed) itog = 'НЕ СМОГ';
if (!itog) itog = exitCode === 0 ? 'ГОТОВО' : 'НЕ СМОГ';

// P14 (К-10): нормализуем payload находок (строка -> {"report": ...}).
function normalizeFindingsPayload(text) {
  const lines = text.split('\n');
  for (let i = lines.length - 1; i >= 0; i--) {
    const t = lines[i].trim();
    if (/^AUDIT_FINDINGS_JSON:/.test(t)) {
      const rawText = t.slice('AUDIT_FINDINGS_JSON:'.length).trim();
      try {
        const arr = JSON.parse(rawText);
        if (Array.isArray(arr)) {
          const fixed = arr.map(f => {
            if (f && typeof f === 'object' && !Array.isArray(f) &&
                (!f.payload || typeof f.payload !== 'object' || Array.isArray(f.payload))) {
              return Object.assign({}, f, { payload: { report: String(f.payload ?? '') } });
            }
            return f;
          });
          lines[i] = 'AUDIT_FINDINGS_JSON: ' + JSON.stringify(fixed);
        }
      } catch (_) { /* не трогаем */ }
      break;
    }
  }
  return lines.join('\n');
}
out = normalizeFindingsPayload(out);

const tailLines = out.split('\n').map(s => s.trim());
const t3 = tailLines.slice(-3);
const canonical = t3.length === 3 &&
  /^AUDIT_FINDINGS_JSON:/.test(t3[0]) &&
  /^AUDIT_VERDICT:\s*(PASS|REVISION_REQUIRED|HUMAN_REQUIRED)\s*$/i.test(t3[1]) &&
  ITOG_ACCEPT_RE.test(t3[2]);

if (!canonical) {
  const nonEmpty = tailLines.filter(Boolean);
  const lastLine = nonEmpty.pop() || '';
  const lastTwo = nonEmpty.slice(-2);
  const pairAtEnd = lastTwo.length === 2 &&
    /^AUDIT_FINDINGS_JSON:/.test(lastTwo[0]) &&
    /^AUDIT_VERDICT:\s*(PASS|REVISION_REQUIRED|HUMAN_REQUIRED)\s*$/i.test(lastTwo[1]);

  if (pairAtEnd && !ITOG_ACCEPT_RE.test(lastLine)) {
    out += `\nИТОГ: ${itog} — финальная строка добавлена goose-wrapper провайдера (модель завершила отчёт без строки ИТОГ).`;
  } else {
    const audit = (itog === 'ГОТОВО' || itog === 'УЖЕ СДЕЛАНО') ? 'PASS' : 'REVISION_REQUIRED';
    const reason = killed
      ? 'goose-wrapper прервал процесс по внутреннему таймауту (~115 мин)'
      : (r.signal ? `процесс завершён сигналом ${r.signal}` : `код выхода процесса ${exitCode}; финальный машинно-читаемый хвост добавлен goose-wrapper провайдера (аналог штатного verdict-repair)`);
    out += `\n\n(${reason})`;
    out += `\nAUDIT_FINDINGS_JSON: []`;
    out += `\nAUDIT_VERDICT: ${audit}`;
    out += `\nИТОГ: ${itog} — см. отчёт выше.`;
  }
}

process.stdout.write(out + '\n');
process.exit(exitCode < 0 ? 1 : exitCode);
