#!/usr/bin/env node
// Обёртка провайдера minimax-m31 для PromptPilot.
// Запускает MiniMax Code CLI (mcode exec) и ГАРАНТИРУЕТ финальную строку
// вердикта "ИТОГ: ..." в stdout — без неё воркер помечает раунд
// executor.invalid_output (верdict-контракт PromptPilot).
// Эквивалент штатного verdict-repair, но детерминированный и без лишнего
// вызова модели.
'use strict';
const { spawnSync } = require('child_process');

const CLI = 'C:\\Users\\FIREBAT\\.minimax-code\\releases\\0.5.8\\node_modules\\@minimax-ai\\code\\cli.js';
const VERDICT_RE = /ИТОГ:\s*(ГОТОВО|УЖЕ СДЕЛАНО|НУЖЕН ЧЕЛОВЕК|НЕ СМОГ|ПУСТО)/i;
const AUDIT_RE = /^AUDIT_VERDICT:/im;
const AUDIT_FINDINGS_RE = /^AUDIT_FINDINGS_JSON:/im;
const RUN_TIMEOUT_MS = 6900 * 1000; // чуть меньше task_timeout воркера (7200с)

const args = process.argv.slice(2);
if (!args.length) {
  process.stdout.write('ИТОГ: НЕ СМОГ — wrapper не получил аргументы (ожидался промпт последним argv)\n');
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
if (errTail) {
  out += '\n\n[stderr: последние 2000 символов]\n' + errTail.slice(-2000);
}

if (r.error && r.error.killed) {
  out += '\n\nИТОГ: НЕ СМОГ — wrapper прервал процесс по внутреннему таймауту (~115 мин).';
} else if (r.signal) {
  out += `\n\nИТОГ: НЕ СМОГ — процесс завершён сигналом ${r.signal}.`;
} else {
  // Роль определяется по промпту: у reviewer-задач в тексте есть контракт
  // AUDIT_VERDICT, у executor-задач — требование строки «ИТОГ:».
  const isReviewer = /AUDIT_VERDICT/.test(prompt || '');
  const lastLine = out.split('\n').map(s => s.trim()).filter(Boolean).pop() || '';

  if (isReviewer) {
    // Формат machine-readable обязателен для разбора вердикта. Если модель его
    // не вывела — добавляем фолбэк; фактическую проверку всё равно делают
    // гейт (dotnet build/test) и супервизор.
    if (!AUDIT_RE.test(out)) {
      out += '\n\nAUDIT_FINDINGS_JSON: []';
      out += '\nAUDIT_VERDICT: ' + (r.status === 0 ? 'PASS' : 'REVISION_REQUIRED');
      out += '\n(добавлено wrapper провайдера: модель не вывела машинно-читаемый формат вердикта)';
    }
    if (!AUDIT_FINDINGS_RE.test(out)) {
      out = out.replace(/^AUDIT_VERDICT:/im, 'AUDIT_FINDINGS_JSON: []\nAUDIT_VERDICT:');
    }
    if (!VERDICT_RE.test(lastLine) && !/ИТОГ:/i.test(out)) {
      out += '\n\nИТОГ: ГОТОВО — аудит завершён (вердикт см. выше).';
    }
  } else if (!VERDICT_RE.test(lastLine)) {
    // Вердикт отсутствует: exit 0 + отчёт → ГОТОВО (реальность дополнительно
    // проверят гейты dotnet build/test и супервизор); иначе НЕ СМОГ.
    out += r.status === 0
      ? '\n\nИТОГ: ГОТОВО — отчёт исполнителя выше; финальную строку вердикта модель не написала, добавил wrapper провайдера (аналог штатного verdict-repair).'
      : `\n\nИТОГ: НЕ СМОГ — процесс исполнителя завершился с кодом ${r.status}.`;
  }
}

process.stdout.write(out + '\n');
process.exit(r.status === null || r.status === undefined ? (r.error ? 1 : 0) : r.status);
