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

## SafeLoadDatabase / PrintEnvelope через COM (guard-сценарий, evidence/com-s14/dbguard.json)

Безопасный сценарий: сервер из изолированной копии, production-БД
отсутствует (и НЕ создаётся). Проба `dbguard` вызывает через IDispatch:
`DatabaseIsLoaded`, `SafeLoadDatabase`, `PrintEnvelope`, `GetWarningType`.

| Вызов | Результат |
|---|---|
| CoCreateInstance LOCAL_SERVER | hr=0x00000000 |
| DatabaseIsLoaded | False |
| SafeLoadDatabase | hr=S_OK, out=**False** (production-БД отсутствует, корректный guard) |
| DatabaseIsLoaded (после) | False (БД НЕ создана) |
| PrintEnvelope | hr=S_OK (печать не выполнена — БД не загружена) |
| GetWarningType | out=0 (см. находку D-S14-warncode ниже) |
| **productionDb before/after** | **False / False — production-БД не создана** |
| profileChanges | только `com-automation.log` (append-only, ожидаемо) |
| Остановка / очистка | GRACEFUL_EXIT, --unregister 0, ключи сняты, процессов 0, temp удалён |

Это доказывает: COM-диспетчеризация SafeLoadDatabase/PrintEnvelope работает,
guard по отсутствующей БД срабатывает, **записи в production-профиль нет**,
печать не запускается. Физическая/виртуальная печать при этом НЕ выполнялась.

### Находка D-S14-warncode (P2, требует расследования)

`GetWarningType` после `PrintEnvelope` с незагруженной БД возвращает **0
(Success)** вместо ожидаемого `DatabaseNotLoaded=1`
(`AutomationWarningCodes.DatabaseNotLoaded=1`). Проверено с задержкой 700 мс
перед запросом — результат не меняется (не тайминг). Вероятная причина:
предупреждение пишется в экземпляр helper-потока (`ComServerHost`
форвардит команды), а `GetWarningType` читает `_warningType` экземпляра
вызывающего. На безопасность не влияет (печать guarded, БД не создана), но
вызывающий не может обнаружить отказ по коду предупреждения — кандидат в
дефекты COM-волны (расследовать propagation предупреждения через
helper-поток). Зафиксировано для ревью/владельца.

### Полный print happy-path — NOT_RUN_EXTERNAL

Фактическая печать через COM (`ВыполнитьПечатьAsync` →
`PrinterManager.GetDefaultPrinter()` → `Printing.PrintSelectionDirectAsync`)
требует принтер по умолчанию. Единственный доступный — **«Microsoft Print
to PDF» (интерактивный: запрашивает путь сохранения)**; unattended-печать
через него небезопасна (модальное окно). Неконфигурируемого виртуального
принтера / PrintToFile-хука, проброшенного через COM, нет. Поэтому полный
happy-path «запись в изолированную БД + вывод в виртуальный принтер» через
COM отнесён к печатной волне (S16–S20), где виртуальная печать
проектируется безопасно; согласуется с `PHYSICAL_PRINT_VERIFIED=NOT_RUN`.

## Статус флагов

```text
COM_COLD_VERIFIED=true (5/5 приёмочных + 1 baseline S11 + 1 UNC, RC)
COM_FULL_ACCEPTANCE_VERIFIED=PARTIAL
```

Разбивка S14 (честно):
- CreateInstance — **доказан** (6 холодных + UNC + dbguard).
- SafeLoadDatabase через COM — **доказан** (guard, без записи в production).
- PrintEnvelope через COM — **guard доказан** (диспетчеризация + защита по
  БД, без печати/записи); полный print happy-path — **NOT_RUN_EXTERNAL**
  (интерактивный виртуальный принтер; печатная волна S16–S20).
- CoRevoke + очистка ключей — **доказаны** во всех циклах.
- Находка D-S14-warncode (propagation кода предупреждения) — открыта.

`COM_FULL_ACCEPTANCE_VERIFIED=true` будет заявлен только после безопасного
print happy-path через COM (печатная волна) и закрытия D-S14-warncode.

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
