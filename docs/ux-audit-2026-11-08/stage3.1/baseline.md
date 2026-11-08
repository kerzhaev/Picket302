# Baseline stage3.1 (2026-11-08)

## Git

```
Момент фиксации UTC:   2026-11-08 ~17:15 (census) / серия завершена 20:28 UTC
HEAD:                  799f321 (docs: stage3 UX audit series)
Ветка:                 main, ahead of origin/main (aee4ea8) by 3
                       (0dcba16 fix(ui+tests), 1c22c33 feat(acceptance), 799f321 docs)
origin/main:           aee4ea8 — не изменялся
Dirty tracked:         нет
Untracked:             graphify-out/ (предсуществующий, НЕ тронут: 528 файлов,
                       последний mtime 2026-11-06T15:40:07Z)
Worktrees:             1) //192.168.1.117/Projets/Picket302 (main, этот)
                       2) C:/Users/Nachfin/.codex/worktrees/redesign-konvert-ui/Picket302
                          (detached 0dc80026 — ЧУЖОЙ, не тронут)
```

## Процессы на старте

```
Picket302 / KonvertProNet / UiAcceptance / testhost / dotnet:  0
PowerShell-процессы: 4 (47708, 87044, 106544 powershell; 66928 pwsh) — окружение
Чужие процессы не затрагивались.
```

## Производственный профиль %AppData%\Picket302 (на старте)

| Файл | Размер | SHA-256 | LastWriteTimeUtc |
|---|---|---|---|
| picket302.db | 258048 | `16830691d3a46e4d589d763379f38b5348ee6ba6b7f3bc887d537e66e4da03c6` | 2026-11-08T15:51:24Z (внешнее, ENV3-02 stage3) |
| connection.json | 225 | `399006d91ec787c5f4b6185df7477eb8abe8d549640e681a1d2b45162d6972d5` | 2026-11-08T16:17:30Z (внешнее, ENV3-02 stage3) |
| com-automation.log | 521722 | `3db33e054f3e172fdcc52ced9252aba49e72c682e514b05c4fa91b11baa1ba4e` | 2026-11-08T07:17:10Z |
| print_audit.log | 48074 | `215769f4a382be47f7379d57caaec78070a25b0d06a35c3ee13ba2b3d8a46658` | 2026-11-07T22:53:29Z |

Примечание: отсутствие базы ошибкой не считается — состояние зафиксировано
как есть. База на старте ПРИСУТСТВОВАЛА (внешне восстановлена в 15:51 UTC до
начала серии — см. env3-02.md).

## Реестр (HKCU\Software\PEPro\CurrentSender)

```
SenderId: ABSENT на старте (контейнирование пишущей ветки выполнено в серии — env3-02.md)
```

## Temp / locks

```
Picket302Acceptance:              пуст (0 детей)
P302-evidence предыдущих серий:   %TEMP%\p302-evidence-2026-11-08{,-stage3} — не тронуты
Picket302.ComAcceptance.lock:     отсутствует
sentinel:                         отсутствует
Предсуществующие Picket302*-каталоги %TEMP%: ~6500 (заполняются ПАРАЛЛЕЛЬНОЙ
сессией владельца — BackupTests/Restore/Schedule с mtime в момент серии;
ни один из них не создавался и не удалялся этой серией)
```

## Evidence-каталог серии

```
P302_EVIDENCE_ROOT = %TEMP%\p302-evidence-2026-11-08-stage3-1
```
