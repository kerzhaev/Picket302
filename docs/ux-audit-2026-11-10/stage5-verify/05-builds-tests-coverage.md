# Stage 5 (verify) — Этап 6: сборки, тесты, покрытие

Все команды выполнены в изолированном worktree
`C:\Users\Nachfin\p302-worktrees\stage5-193008` (чистый `b9e2803`,
без чужих незакоммиченных изменений), .NET SDK 8.0.420, x64.

## Сборки

| Команда | Результат |
|---|---|
| `dotnet build Picket302.sln -c Debug` | **0 ошибок / 0 предупреждений** (6.9 с) |
| `dotnet build Picket302.sln -c Release` | **0 ошибок / 0 предупреждений** (4.6 с) |
| `dotnet build tools/ui-acceptance/UiAcceptance.csproj -c Release` | 0 ошибок (2.0 с) |

## Тесты (non-COM набор, фильтр stage3.8–4)

Фильтр (строго, как требует задание — `AcceptanceScenarioTests`
исключаются ЦЕЛИКОМ, не только по имени `ComAutomation`, поскольку
COM-сценарий лежит в `AcceptanceScenarioTests`; физический
`Printing.PrintEngineTests` также исключён):

```text
dotnet test Picket302.sln -c <Debug|Release> --no-build
  --filter "FullyQualifiedName!~Acceptance&FullyQualifiedName!~ComAutomation&FullyQualifiedName!~Printing.PrintEngineTests"
```

| Конфигурация | Результат | Длительность |
|---|---|---|
| Debug | **1370 passed / 0 failed / 0 skipped** | 27 с |
| Release | **1370 passed / 0 failed / 0 skipped** | 32 с |

### Перечень пропущенных/исключённых тестов (явный)

1. `Picket302.Tests.Acceptance.AcceptanceScenarioTests` (весь класс) —
   исторический COM-сценарий **перемещает production-БД**
   `%AppData%\Picket302\picket302.db` и восстанавливает её; запуск без
   доказанной переработки изоляции запрещён границами задания.
   Дополнительный фактор: `picket302.db` в профиле сейчас отсутствует —
   «восстанавливать» нечего, а сценарий создал бы базу в профиле
   пользователя. Замена доказательства: независимые COM-циклы
   (03-com-cold-activation.md) с безопасными методами.
2. `Picket302.Tests.ComAutomation.*` (ComAutomationContractTests,
   ComRegistrationTests, ForegroundWindowServiceTests,
   TypeLibraryTests) — исключены фильтром non-COM набора; частично
   замещены живой приёмкой 5× (регистрация/активация/диспетчеризация/
   отзыв) на реальном сервере.
3. `Picket302.Tests.Printing.PrintEngineTests` — исключены исторически
   (консервативно сохранено соответствие с набором stage4 для
   сопоставимости 1370/1370); фактически device-независимы
   (фейковый `PrinterDeviceInfo`) — кандидат на включение в набор
   будущими этапами (решение владельца не требуется, но здесь
   сохранён консервативный scope).

## Покрытие (измерено, coverlet `XPlat Code Coverage`, Debug non-COM)

```text
TOTAL: line 64.57% (32632/50533)  branch 52.59% (7479/14221)
  Picket302.App             line 50.34%  branch 30.87%
  Picket302.Application     line 74.29%  branch 60.23%
  Picket302.ComAutomation   line  5.35%  branch  3.93%
  Picket302.Domain          line 73.20%  branch 64.41%
  Picket302.Infrastructure  line 73.95%  branch 60.70%
  Picket302.Printing        line 76.92%  branch 63.96%
```

Предыдущее измерение (историческое): ~62.58% строк / ~52.15% ветвей —
текущее чуть выше. Низкое покрытие ComAutomation объясняется
исключением COM-тестов из набора; App — большим объёмом UI-кода
(MainShell ~5335 строк), который покрыт acceptance-драйверами, а не
unit-тестами. `CODE_COVERAGE_MEASURED=true`.

Артефакт: `coverage.cobertura.xml` —
`C:\Users\Nachfin\AppData\Local\Temp\picket302-stage5-20261110-184530\coverage\ea11f03a-…\coverage.cobertura.xml`
(в репозиторий не копируется из-за объёма; сводка приведена выше).

## Скрытая UI-матрица (8 драйверов, мой прогон 20:38:57–20:41:48)

| Драйвер | Exit | Ключевые утверждения |
|---|---:|---|
| run-emptybook | 0 | COUNTS всего=0; delta 0/0/0 |
| run-screens | 0 | 8 PNG (main 1280×720/1000×700 + 6 диалогов) |
| run-screens2 | 0 | S1–S8 (включая F7→«Проверка размера бумаги», GRIDRESET) |
| run-noaddress | 0 | F5/F6 guard «нет адреса»; MAILCOUNT=0; spooler 0 |
| run-noprinter | 0 | guard «нет принтера»; данные не потеряны |
| run-previewflow | 0 | F6-цепочка + PREVIEWSCAN непустой |
| run-printflow | 0 | F5/F6 цепочки до печати; spooler 0 |
| run-dpi | 0 | симуляция 96/120/144/96 (WM_DPICHANGED), LAYOUTCHECK нулевых кнопок=0 |

Во всех: `graceful=True killed=False`, `TreeOk=True`, временные профили
удалены, `production delta new=0 changed=0 removed=0`, foreground
пользователя не менялся. Артефакты: `evidence/runtime/`.
