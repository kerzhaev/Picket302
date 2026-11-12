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

## UNC-путь (выполнен, evidence/com-s14/cold-unc-true.json)

Холодная активация с LocalServer32 на сетевом шаре
`\\192.168.1.117\Projets\p302-rc-unc-tmp\p302-rc-com-<guid>\…exe`:

| Шаг | Результат |
|---|---|
| Cold `CoCreateInstance` CLSCTX_LOCAL_SERVER(0x4) | **hr=0x00000000 за 125 мс** |
| PID сервера | 90632 |
| `-Embedding` / CoRegister / CoRevoke (ASCII-маркеры) | 1 / 1 / 1, exe=UNC, cwd=system32 |
| Остановка | GRACEFUL_EXIT_0 |
| Очистка | --unregister 0, ключи сняты, процессов 0, temp удалён; корневой каталог `p302-rc-unc-tmp` на шаре удалён (проверено absence) |

## Статус флагов

```text
COM_COLD_VERIFIED=true (5/5 приёмочных + 1 baseline S11 + 1 UNC, RC)
COM_FULL_ACCEPTANCE_VERIFIED=PARTIAL
```

`COM_FULL_ACCEPTANCE_VERIFIED` остаётся PARTIAL до выполнения оставшегося
подпункта S14:

1. ~~UNC-путь~~ — **выполнен** (см. выше).
2. **PrintEnvelope через COM на безопасном виртуальном/тестовом пути** с
   записью ожидаемых данных в изолированную БД (не production). В stage5
   не выполнялся (AcceptanceScenarioTests перемещает production-БД —
   запрещено без переработки изоляции). Нужен изолированный профиль +
   виртуальный принтер; спроектировать безопасный сценарий.

## Инструментальные уточнения (ревью F-W3-3)

`Stop-ServerGraceful` возвращает `GRACEFUL_EXIT_0` по факту `WaitForExit`,
не проверяя код выхода процесса (имя подразумевает 0); компенсировано
строкой `CoRevokeClassObject` в журнале (в коде выполняется после выхода
из цикла сообщений перед return 0). В dispatch-пробе `hr=[0]` —
возвращаемое значение метода, а не HRESULT (успех = отсутствие «Dispatch
call FAILED»). Доказательства валидны; точные имена — в будущих драйверах.

## Точка возобновления

- Скрипты готовы: `Temp\p302-rc-com-20261112\{com-cold-run.ps1,
  acceptance-5x.ps1, baseline.ps1, unc-cycle.ps1, comprobe\out\P302ComProbe.exe}`.
- После PrintEnvelope: `COM_FULL_ACCEPTANCE_VERIFIED=true`, затем S15
  (поставочная COM-модель: регистрация/удаление на чистой среде, инструкции
  восстановления, выбор HKCU/HKLM под установщик).

Артефакты: evidence/com-s14/{acceptance-5x-verified.json,
acceptance-5x-ps-raw.json (ложные False по кириллице — для прозрачности),
cold-acc1..5.json}.
