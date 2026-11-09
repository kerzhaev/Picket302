# Stage 4 — fail-closed профиль и достоверный неактивирующий захват

Дата проверки: 2026-11-10<br>
Статус серии: `READY_WITH_LIMITATIONS`<br>
Ветка: `main`<br>
Базовый SHA серии: `417d083840141d18959556a3d7cc7c84d44defb6`<br>
Коммит этой серии: создаётся после финальной проверки

## 1. Цель серии

Серия закрывает два подтверждённых остаточных дефекта после stage3.9:

1. ранний вызов команды подключения к базе мог создать неявный
   `new ConnectionManager()` и тем самым обойти composition root и обратиться к
   производственному `%AppData%\\Picket302`;
2. неактивирующий захват окон сначала использовал `BitBlt` с экранной
   поверхности. Если целевое окно было перекрыто другим окном, PNG мог содержать
   окно, лежащее под целевым HWND, хотя сам harness считался успешно завершённым.

Цель серии — сделать оба пути fail-closed и повторно проверить скрытую UI-матрицу
без изменения фокуса пользователя и без записи в production-профиль.

## 2. Исправления

### 2.1. MainShell: запрет production fallback

В `src/Picket302.App/MainShell.cs`:

- `ОткрытьБазу()` прекращает работу до создания диалога, если composition root
  ещё не инициализирован;
- `СоздатьДиалогПодключения()` больше не имеет fallback на
  `new ConnectionManager()` и `new ServerRegistry()`;
- прямой ранний вызов теперь выдаёт понятный
  `InvalidOperationException` с указанием, что состав приложения ещё не создан;
- обычный production-путь после инициализации состава использует только
  профиль, переданный composition root.

Добавлен регрессионный тест
`tests/Picket302.Tests/App/MainShellConnectionProfileTests.cs`:
`ДиалогПодключения_ДоИнициализации_ОтказываетБезПроизводственногоFallback`.

### 2.2. Capture: PrintWindow до BitBlt

В `tools/ui-acceptance/acceptance-lib.ps1` порядок захвата изменён:

1. `PrintWindow(hwnd, ..., PW_RENDERFULLCONTENT)` — основной путь, который
   запрашивает отрисовку у самого окна и не зависит от того, что лежит под ним
   на экране;
2. `BitBlt` с `GetWindowDC` — только ограниченный fallback при отказе
   `PrintWindow`;
3. оба HDC освобождаются в `finally`, ошибка содержит HWND и Win32-код.

В `tools/ui-acceptance/test-nonactivating-capture-static.ps1` добавлена
регрессия порядка: static guard красный, если `BitBlt` встречается до
`PrintWindow`.

## 3. Верификация кода и тестов

| Проверка | Результат |
|---|---:|
| `dotnet build ./Picket302.sln -c Debug --no-restore` | `0 ошибок / 0 предупреждений` |
| `dotnet build ./Picket302.sln -c Release --no-restore` | `0 ошибок / 0 предупреждений` |
| `MainShellConnectionProfileTests` | `2/2` |
| `FullyQualifiedName~MainShell` | `57/57` |
| `ComAutomation.ComRegistrationTests` | `6/6` |
| Debug non-COM без `AcceptanceScenarioTests`, COM и физического PrintEngine | `1370/1370` |
| Release non-COM без `AcceptanceScenarioTests`, COM и физического PrintEngine | `1370/1370` |
| static guard неактивирующего захвата, Windows PowerShell 5.1 | `PASS` |
| static guard неактивирующего захвата, PowerShell 7 | `PASS` |
| сборка `UiAcceptance.csproj` Release | `0 ошибок / 0 предупреждений` |
| `git diff --check` | `PASS` |

Фильтр non-COM намеренно исключает весь `AcceptanceScenarioTests`, а не только
имя `ComAutomation`: COM acceptance-тест находится в классе `AcceptanceScenarioTests`
и иначе ошибочно попадает в статистику, заявленную как non-COM.

## 4. Скрытая UI-проверка

После исправления `PrintWindow` повторно выполнен полный пакет восьми
неактивирующих драйверов:

- `run-screens.ps1` — `8/8` PNG, exit `0`;
- `run-screens2.ps1` — S1–S8, exit `0`;
- `run-dpi.ps1` — 96/120/144/96, exit `0`;
- `run-emptybook.ps1` — `COUNTS всего=0`, exit `0`;
- `run-noaddress.ps1` — F5/F6, exit `0`;
- `run-noprinter.ps1` — предупреждение об отсутствии принтера, данные не
  потеряны, spooler `0 -> 0`, exit `0`;
- `run-previewflow.ps1` — F6 и preview scan, exit `0`;
- `run-printflow.ps1` — F5/F6 и закрытие preview до spooler, exit `0`.

Общие факты по пакетному прогону:

- foreground пользователя не менялся;
- `WS_EX_NOACTIVATE=true`, окно harness не стало foreground;
- успешный путь не использовал Kill;
- `TreeOk=true`, orphan-процессов нет;
- временные профили удалены;
- production fingerprint: `new=0 changed=0 removed=0`;
- задания в spooler не создавались;
- исправленный основной снимок не содержит дублированной строки меню от окна,
  лежавшего под целевым HWND;
- клипарт-смоук показывает все 10 значков.

Артефакты оставлены только во временных каталогах и не добавлялись в Git:

```text
C:\Users\Nachfin\AppData\Local\Temp\p302-stage4-capture-printwindow-ef1e8e17c32f441da2dd7e0d02ffd9e0
C:\Users\Nachfin\AppData\Local\Temp\p302-stage4-full-hidden-retry-315e4acd84f3490ca2c304873bdc7e9b
```

Созданная для диагностики временная копия COM-сервера удалена после проверки.
Созданный диагностический ключ `Picket302.PingProbe` и все его точные HKCU
подразделы удалены; регистрация Picket302 после завершения экспериментов отсутствует.

## 5. COM-граница: честный результат

Холодная COM-активация в этой сессии не объявляется исправленной.

Доказано раздельно:

- прямой запуск `Picket302.ComAutomation.exe -Embedding` работает: helper HWND
  создан, `CoRegisterClassObject=S_OK`, `CoResumeClassObjects=S_OK`, штатный
  сигнал завершения приводит к `CoRevokeClassObject`, код выхода `0`;
- при уже работающем сервере native `CoCreateInstance`/`Activator` получает
  живую фабрику;
- при холодной активации из HKCU на локальной копии и на UNC-пути вызов
  возвращает `0x80040154` примерно за 43 мс, новый процесс не появляется, свежая
  дельта журнала не содержит `-Embedding` и `CoRegisterClassObject`;
- контрольный машинный `Excel.Application` из HKLM активировался через SCM и
  был штатно закрыт;
- текущий клиент — x64, integrity level — Medium, `EnableLUA=1`, DcomLaunch и
  RpcSs работают.

Следовательно, фабрика, message loop и graceful revoke доказаны, но конкретная
причина, по которой этот Windows-host не выполняет холодный per-user
`LocalServer32` запуск из HKCU, не установлена. Менять registration shape наугад
или переводить продукт в HKLM без решения о правах установки нельзя. Поэтому:

```text
COM_VERIFIED=PARTIAL
COLD_COM_ACTIVATION_VERIFIED=false
DIRECT_COM_SERVER_VERIFIED=true
```

Следующий отдельный COM-этап должен выполняться на согласованном стенде с
Process Monitor/ETW либо с разрешённым перезапуском COM infrastructure и должен
поймать обращение SCM к `HKCU\\Software\\Classes\\CLSID` и причину отказа.

## 6. Визуальный статус относительно KonvertProNet

Структурное сходство главного экрана и предыдущие runtime-снимки подтверждены;
единственный pixel-reference главного окна даёт прежний score `18/20 = 90`.
Но полный процент «70% по приложению» по-прежнему не измерен, потому что в
репозитории есть пиксельный референс только главного экрана, а остальные формы
имеют только структурные legacy-описания и текущие снимки.

```text
UI_VISUAL_VERIFIED=true
REFERENCE_VISUAL_VERIFIED=PARTIAL
REFERENCE_VISUAL_SCORE=90 (только главный экран)
OVERALL_70_PERCENT_MEASURED=false
```

## 7. Что остаётся дальше

1. Сформировать полноценную reference-matrix для всех поверхностей:
   `KonvertProNet reference -> Picket302 capture -> расхождение -> решение`.
   Без этого нельзя честно присвоить 70% всему приложению.
2. Отдельно проверить реальный Windows DPI 125/150%; текущие 96/120/144 —
   управляемая симуляция.
3. Закрыть холодную COM-активацию на стенде, где виден SCM, либо документально
   оформить ограничение окружения и согласовать установочную модель HKLM/HKCU.
4. Провести контролируемую виртуальную печать; физическую печать не объявлять
   подтверждённой без задания на заранее выбранном принтере.
5. После этих gates пересчитать визуальный score и только затем объявлять
   приложение соответствующим целевым 70%.

## 8. Git

Перед коммитом проверены только ожидаемые изменения:

```text
src/Picket302.App/MainShell.cs
tests/Picket302.Tests/App/MainShellConnectionProfileTests.cs
tools/ui-acceptance/acceptance-lib.ps1
tools/ui-acceptance/test-nonactivating-capture-static.ps1
```

`graphify-out/` и неотслеживаемые старые PNG/evidence не включаются в коммит.
После создания отдельного документа этот отчёт и четыре исходных изменения
можно коммитить в `main`; merge не нужен, так как работа уже выполняется на
`main`. Push выполняется только после финального `git diff --check`, теста
чистого состояния и проверки `HEAD == origin/main`.

Флаги серии:

```text
SOURCE_VERIFIED=true
BUILD_DEBUG_VERIFIED=true
BUILD_RELEASE_VERIFIED=true
TEST_DEBUG_NON_COM_VERIFIED=true (1370/1370)
TEST_RELEASE_NON_COM_VERIFIED=true (1370/1370)
UI_HIDDEN_ACCEPTANCE_VERIFIED=true
PROFILE_SAFETY_VERIFIED=true
COM_VERIFIED=PARTIAL
COLD_COM_ACTIVATION_VERIFIED=false
PRINT_VERIFIED=NOT_RUN_BY_SCOPE
OS_DPI_125_150_VERIFIED=NOT_RUN_ON_HOST
CODE_COVERAGE_MEASURED=NOT_REMEASURED_IN_SERIES
```
