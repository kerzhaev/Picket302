# RC S14 — Полный COM acceptance (холодные активации)

Дата: 2026-11-12 (прогоны 01:27–01:28 local / 22:27–22:28 UTC). Код
`881f077` (RC), Release x64. Тихое окно; регистрация/снятие штатные,
очистка в `finally`, kill не применялся.

## Приёмка 5× (evidence/com-s14/acceptance-5x-verified.json)

Клиент-проба x64 .NET 8 `P302ComProbe`; сервер — изолированная копия
Release-сборки RC в `Temp\p302-rc-com-<guid>`; чередование путей клиента
create4 / managed. Критерии проверены **encoding-independent** по
ASCII-маркерам журнала (см. D-S14-logenc в S12/S13 — кириллические
PS-regex дали ложные False, содержательная проверка исправлена).

| Цикл | Клиент | PID сервера | cold hr=0 | -Embedding | CoRegister S_OK | CoRevoke | safe calls | graceful exit | ключи сняты | процессов 0 | temp удалён | ALL |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | create4 | 45380 | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | **PASS** |
| 2 | managed | 13552 | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | **PASS** |
| 3 | create4 | 25196 | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | **PASS** |
| 4 | managed | 22176 | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | **PASS** |
| 5 | create4 | 3056 | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | **PASS** |

`ALL_CYCLES_PASS=True`, `DISTINCT_PIDS=5`. Safe calls = `GetAPIVersion`,
`GetProductVersion`, `DatabaseIsLoaded` (hr=[0]; методы БД не создают и не
открывают базу). Профиль: изменялся только append-only `com-automation.log`
(ожидаемо); `connection.json`/`picket302.db` не тронуты. Post-state:
реестр чист (HKCU/HKLM/HKCR — все ключи отсутствуют), процессов нет,
orphan temp-каталогов нет.

## Статус флагов

```text
COM_COLD_VERIFIED=true (5/5 приёмочных + 1 baseline S11, RC 881f077)
COM_FULL_ACCEPTANCE_VERIFIED=PARTIAL
```

`COM_FULL_ACCEPTANCE_VERIFIED` остаётся PARTIAL до выполнения двух
оставшихся подпунктов S14 (перенесены на завтра, чтобы не оставлять
грязного состояния на сетевом шаре в конце сессии):

1. **UNC-путь**: холодная активация с LocalServer32 на
   `\\192.168.1.117\Projets\…` (скрипт `unc-cycle.ps1`, адаптация под RC;
   в stage5-verify подтверждён — `cold-unc-true.json`). Требует создания
   временного каталога на сетевом шаре и его гарантированного удаления.
2. **PrintEnvelope через COM на безопасном виртуальном/тестовом пути** с
   записью ожидаемых данных в изолированную БД (не production). В stage5
   не выполнялся (AcceptanceScenarioTests перемещает production-БД —
   запрещено без переработки изоляции). Нужен изолированный профиль +
   виртуальный принтер; спроектировать безопасный сценарий.

## Точка возобновления (завтра)

- Скрипты готовы: `Temp\p302-rc-com-20261112\{com-cold-run.ps1,
  acceptance-5x.ps1, baseline.ps1, comprobe\out\P302ComProbe.exe}`;
  `unc-cycle.ps1` адаптировать (заменить `p302-stage5-unc-tmp` →
  `p302-rc-unc-tmp`, путь ком-cold-run — RC).
- После UNC + PrintEnvelope: `COM_FULL_ACCEPTANCE_VERIFIED=true`, затем S15
  (поставочная COM-модель: регистрация/удаление на чистой среде, инструкции
  восстановления, выбор HKCU/HKLM под установщик).

Артефакты: evidence/com-s14/{acceptance-5x-verified.json,
acceptance-5x-ps-raw.json (ложные False по кириллице — для прозрачности),
cold-acc1..5.json}.
