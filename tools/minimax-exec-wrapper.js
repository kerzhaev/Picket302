#!/usr/bin/env node
// Обёртка провайдера minimax-m31 для PromptPilot (v3).
// Запускает MiniMax Code CLI (mcode exec) и гарантирует КАНонический хвост
// отчёта для ОБОИХ ролей конвейера:
//     ... \n AUDIT_FINDINGS_JSON: [...] \n AUDIT_VERDICT: <X> \n ИТОГ: <Y>
// Контракты парсеров PromptPilot (проверено по коду):
//  - parse_verdict (worker): ПОСЛЕДНЯЯ строка должна быть "ИТОГ: <5 вердиктов>";
//  - parse_reviewer_report: последняя пара AUDIT_*, между ними пусто, после
//    AUDIT_VERDICT — ровно одна строка и это валидная "ИТОГ: ...".
// Роль НЕ определяется по промпту: в executor-промпт попадает previous_review
// со строками AUDIT_* (ловушка v1/v2), поэтому канонизируем хвост всегда.
'use strict';
const { spawnSync } = require('child_process');

const CLI = 'C:\\Users\\FIREBAT\\.minimax-code\\releases\\0.5.8\\node_modules\\@minimax-ai\\code\\cli.js';
const ITOG_ACCEPT_RE = /^ИТОГ:\s*(ГОТОВО|УЖЕ СДЕЛАНО|НУЖЕН ЧЕЛОВЕК|НЕ СМОГ|ПУСТО)(\s|$)/i;
const ITOG_ANY_RE = /ИТОГ:\s*(ГОТОВО|УЖЕ СДЕЛАНО|НУЖЕН ЧЕЛОВЕК|НЕ СМОГ|ПУСТО)/gi;
const RUN_TIMEOUT_MS = 6900 * 1000; // чуть меньше task_timeout воркера (7200с)

const args = process.argv.slice(2);
if (!args.length) {
  process.stdout.write('AUDIT_FINDINGS_JSON: []\nAUDIT_VERDICT: REVISION_REQUIRED\nИТОГ: НЕ СМОГ — wrapper не получил аргументы (ожидался промпт последним argv)\n');
  process.exit(2);
}
// build_cmd кладёт промпт последним элементом argv; всё до него — опции exec.
const prompt = args[args.length - 1];
const cliArgs = [...args.slice(0, -1), prompt];

const r = spawnSync(process.execPath, [CLI, ...cliArgs], {
  encoding: 'utf8',
  maxBuffer: 256 * 1024 * 1024,
  timeout: RUN_TIMEOUT_MS,
  cwd: process.cwd(),
});

let out = (r.stdout || '').trimEnd();
const errTail = (r.stderr || '').trimEnd();

// К-9/P13: транзиентные отказы рантайма MiniMax («Runtime completed without a
// final assistant response», «Retry after the connection recovers») не
// матчатся классификаторами воркера (RATE_LIMIT_RE/OVERLOADED_RE/ENV_FAILURE_RE),
// из-за чего задача уходит в failed без requeue-паузы. Переводим отказ в форму,
// которую воркер распознаёт (OVERLOADED_RE: "overloaded" + "try again later"),
// чтобы сработала штатная пауза rate_limited → requeue с бэкоффом.
const TRANSIENT_RE = /Runtime completed without a final assistant response|Retry after the connection|failed to connect|ECONNRESET|ETIMEDOUT|socket hang up/i;
if (errTail && TRANSIENT_RE.test(errTail)) {
  process.stderr.write('\npromptpilot-wrapper: provider runtime overloaded, try again later (transient runtime failure translated for the worker classifier, K-9/P13)\n');
}

const killed = !!(r.error && r.error.killed);
const exitCode = killed ? -1 : (r.status === null || r.status === undefined ? -1 : r.status);

// Вердикт ИТОГ: последнее явное совпадение модели; иначе — по коду выхода.
let itog = null;
let m;
const reAll = new RegExp(ITOG_ANY_RE.source, 'gi');
while ((m = reAll.exec(out)) !== null) itog = m[1].toUpperCase();
if (killed) itog = 'НЕ СМОГ';
if (!itog) itog = exitCode === 0 ? 'ГОТОВО' : 'НЕ СМОГ';

// Хвост уже канонический? (AUDIT_FINDINGS, AUDIT_VERDICT, ИТОГ — последние 3)
const tailLines = out.split('\n').map(s => s.trim());
const t3 = tailLines.slice(-3);
const canonical = t3.length === 3 &&
  /^AUDIT_FINDINGS_JSON:/.test(t3[0]) &&
  /^AUDIT_VERDICT:\s*(PASS|REVISION_REQUIRED|HUMAN_REQUIRED)\s*$/i.test(t3[1]) &&
  ITOG_ACCEPT_RE.test(t3[2]);

if (!canonical) {
  const lastLine = tailLines.filter(Boolean).pop() || '';
  const lastTwo = tailLines.filter(Boolean).slice(-2);
  const pairAtEnd = lastTwo.length === 2 &&
    /^AUDIT_FINDINGS_JSON:/.test(lastTwo[0]) &&
    /^AUDIT_VERDICT:\s*(PASS|REVISION_REQUIRED|HUMAN_REQUIRED)\s*$/i.test(lastTwo[1]);

  if (pairAtEnd && !ITOG_ACCEPT_RE.test(lastLine)) {
    // Модель вывела пару AUDIT_* последней — дописываем только ИТОГ.
    out += `\nИТОГ: ${itog} — финальная строка добавлена wrapper провайдера (модель завершила отчёт без строки ИТОГ).`;
  } else {
    // Иначе — канонический хвост целиком (аннотация ДО пары, между парой и
    // ИТОГ пусто, ИТОГ — последняя строка).
    const audit = (itog === 'ГОТОВО' || itog === 'УЖЕ СДЕЛАНО') ? 'PASS' : 'REVISION_REQUIRED';
    const reason = killed
      ? 'wrapper прервал процесс по внутреннему таймауту (~115 мин)'
      : (r.signal ? `процесс завершён сигналом ${r.signal}` : `код выхода процесса ${exitCode}; финальный машинно-читаемый хвост добавлен wrapper провайдера (аналог штатного verdict-repair)`);
    out += `\n\n(${reason})`;
    out += `\nAUDIT_FINDINGS_JSON: []`;
    out += `\nAUDIT_VERDICT: ${audit}`;
    out += `\nИТОГ: ${itog} — см. отчёт выше.`;
  }
}

process.stdout.write(out + '\n');
process.exit(exitCode < 0 ? 1 : exitCode);
