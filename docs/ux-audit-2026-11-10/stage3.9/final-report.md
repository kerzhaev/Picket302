# Stage3.9 — неактивирующий режим UI-приёмки

Дата проверки: 2026-11-10  
Статус: `READY_WITH_LIMITATIONS`  
Рабочая ветка: `main`  
Исходный SHA серии: `3be76ef`  
Кодовый коммит серии: `d50be67` (`fix(acceptance): add non-activating screenshot mode`)

## 1. Цель серии

До этой серии драйверы визуальной приёмки использовали `HWND_TOPMOST` и
явные вызовы `BringToFront()`/`Activate()`. Это было неудобно для владельца:
запуск скриншотного или DPI-драйвера мог поднимать окно поверх текущей работы и
перехватывать фокус.

Цель серии — подготовить безопасный opt-in режим для будущего скрытого
запуска UI-приёмки. В этом режиме окно может быть создано для собственного
`BitBlt`/layout-проверок, но драйвер не просит foreground, не делает окно
topmost и не вызывает активацию формы.

## 2. Изменения

### 2.1. Harness

В `tools/ui-acceptance/Program.cs` добавлен явный режим:

```text
PICKET302_ACCEPTANCE_NONACTIVATING=1
```

При его включении для главной формы и рабочих диалогов:

- перед первым `Show()` устанавливается `WS_EX_NOACTIVATE` через
  `GetWindowLongPtrW`/`SetWindowLongPtrW`;
- `ShowInTaskbar` устанавливается в `false`;
- `BringToFront()` и `Activate()` пропускаются;
- обычный режим запуска без переменной окружения не изменён.

В `acceptance-lib.ps1` параметр `$неактивирующий` добавлен в конец
`Start-Acceptance` с безопасным значением `$false`, поэтому существующие
вызывающие сценарии сохраняют прежний контракт. Переменная окружения передаётся
через `ProcessStartInfo.EnvironmentVariables`, совместимый с PowerShell 5.1 и
PowerShell 7.

### 2.2. Драйверы захвата

`run-screens.ps1`, `run-screens2.ps1` и `run-dpi.ps1` теперь передают
неактивирующий флаг и больше не используют `HWND_TOPMOST`.

Операции позиционирования используют нейтральный z-order и флаги:

- `0x0054` = `SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW`;
- `0x0015` = `SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE`.

Это устраняет два источника нежелательного воздействия на пользовательское
окно: поднятие в topmost-z-order и автоматическую активацию через Win32.

### 2.3. Регрессионный static guard

Добавлен `tools/ui-acceptance/test-nonactivating-capture-static.ps1`. Он только
читает исходники и не запускает WinForms, не создаёт профиль и не меняет
файлы. Guard проверяет наличие переменной и native-стиля, передачу флага в
драйверы, отсутствие `[IntPtr](-1)` и использование нейтрального z-order.

## 3. Верификация

| Проверка | Результат |
|---|---:|
| `UiAcceptance.csproj` Release build | `0 ошибок / 0 предупреждений` |
| Static guard в Windows PowerShell 5.1 | `PASS` |
| Static guard в PowerShell 7 | `PASS` |
| Parse modified scripts в PowerShell 5.1 | `PASS` |
| Parse modified scripts в PowerShell 7 | `PASS` |
| `git diff --check` до коммита | `PASS` |
| Runtime UI capture в этой серии | `NOT_RUN_BY_SCOPE` |
| Изменение production-профиля | `не выполнялось` |
| Запуск COM/SCM | `не выполнялся` |
| Запуск spooler/физической печати | `не выполнялся` |

Runtime-драйверы намеренно не запускались в этой серии: статическая проверка
подтверждает контракт кода, но не является доказательством того, что конкретная
сессия Windows никогда не изменит foreground. Это будет отдельный контролируемый
этап с наблюдением активного окна и списком процессов до/после.

Базовые результаты приложения из stage3.8 — `1369/1369` строгих non-COM
тестов в Debug и Release, coverage `66.62% line / 53.84% branch` — в этой
серии не переизмерялись, потому что изменения затрагивают только harness и
PowerShell-драйверы. Они не переобъявляются как новые результаты stage3.9.

## 4. Флаги

```text
SOURCE_VERIFIED=true
BUILD_ACCEPTANCE_HARNESS_RELEASE_VERIFIED=true
NONACTIVATING_CAPTURE_STATIC_VERIFIED=true
POWERSHELL_5_1_STATIC_VERIFIED=true
POWERSHELL_7_STATIC_VERIFIED=true
POWERSHELL_5_1_PARSE_VERIFIED=true
POWERSHELL_7_PARSE_VERIFIED=true
UI_CAPTURE_RUNTIME_VERIFIED=NOT_RUN_BY_SCOPE
REFERENCE_VISUAL_VERIFIED=PARTIAL
COM_VERIFIED=DEFERRED_BY_OWNER
PRINT_VERIFIED=NOT_RUN_BY_SCOPE
OS_DPI_125_150_VERIFIED=NOT_RUN_ON_HOST
CODE_COVERAGE_MEASURED=NOT_REMEASURED_IN_SERIES
```

## 5. Что остаётся по плану

1. После отдельного согласования выполнить один контролируемый runtime-smoke
   нового режима без foreground: зафиксировать активный HWND до/после,
   PID/дерево процессов, отсутствие topmost-изменений и корректное завершение.
2. Только после этого использовать режим для недостающей матрицы скриншотов и
   сравнения с KonvertProNet. Сейчас `REFERENCE_VISUAL_VERIFIED=PARTIAL`:
   полноценный пиксельный референс есть только для главного экрана.
3. Отдельными стендами закрыть ещё не выполненные внешние ворота:
   реальный DPI 125/150%, холодная COM-активация, виртуальная/реальная печать
   в заранее согласованном безопасном сценарии.
4. Не смешивать эти внешние проверки с текущим статическим доказательством и
   не объявлять общий процент визуального сходства измеренным без матрицы
   сопоставимых изображений.

## 6. Git и чистота

```text
Коммит: d50be67 fix(acceptance): add non-activating screenshot mode
Ветка: main
Изменены только файлы acceptance-harness/драйверов и static guard.
graphify-out/ существовал до серии, не изменялся и в коммит не включён.
```
