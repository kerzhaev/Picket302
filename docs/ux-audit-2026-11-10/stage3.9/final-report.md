# Stage3.9 — неактивирующий режим UI-приёмки

Дата проверки: 2026-11-10  
Статус: `READY_WITH_LIMITATIONS`  
Рабочая ветка: `main`  
Исходный SHA серии: `3be76ef`  
Кодовый коммит серии: `b5cb3be` (`fix(acceptance): verify non-activating capture at runtime`)

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

Добавлен `tools/ui-acceptance/test-nonactivating-capture-runtime.ps1`. Runtime
smoke использует временный профиль и проверяет настоящее окно после `READY`:

- `GetForegroundWindow()` до и после запуска;
- `HARNESS_IS_FOREGROUND=False`;
- `WS_EX_NOACTIVATE=True`;
- `WS_EX_TOPMOST=False`;
- окно действительно видимо для собственного `BitBlt`;
- штатный `CLOSE` и bounded exit code `0`.

Smoke успешно выполнен в Windows PowerShell 5.1 и PowerShell 7. В обоих
случаях foreground HWND пользователя не изменился.

### 2.4. Исправленный дефект каталога захвата

Первый прогон `run-screens.ps1` с новым временным `P302_EVIDENCE_ROOT`
честно выявил дефект harness: каталог для PNG создавался только в финале при
записи markdown-evidence, а `Bitmap.Save()` вызывался раньше. GDI+ возвращал
`A generic error occurred`, и сценарий завершался с exit 1.

В `run-screens.ps1` добавлено fail-closed создание `$out` до первого
`BitBlt`. После исправления повторный прогон сохранил 8/8 PNG и завершился с
exit 0.

## 3. Верификация

| Проверка | Результат |
|---|---:|
| `UiAcceptance.csproj` Release build | `0 ошибок / 0 предупреждений` |
| Static guard в Windows PowerShell 5.1 | `PASS` |
| Static guard в PowerShell 7 | `PASS` |
| Parse modified scripts в PowerShell 5.1 | `PASS` |
| Parse modified scripts в PowerShell 7 | `PASS` |
| `git diff --check` до коммита | `PASS` |
| Runtime non-activating smoke в PS 5.1 | `PASS` |
| Runtime non-activating smoke в PS 7 | `PASS` |
| `run-screens.ps1` | `8/8 PNG`, exit `0` |
| `run-screens2.ps1` | `S1–S8 PASS`, exit `0` |
| `run-dpi.ps1` | `D1–D5 PASS`, exit `0` |
| DPI simulation | `96/120/144/96`, layout PASS |
| Foreground после runtime-запусков | harness не стал foreground |
| Spooler в screens2/DPI | `0 → 0` |
| Production profile delta | `new=0 changed=0 removed=0` |
| Runtime UI capture в этой серии | `VERIFIED` |
| Production sentinel | временно создан/удалён драйвером, итоговый fingerprint не изменился |
| Запуск COM/SCM | `не выполнялся` |
| Запуск spooler/физической печати | `не выполнялся` |

Evidence временных прогонов:

```text
screens : %TEMP%\p302-evidence-stage3-9-screens-d7e5a4c0a70e4e3db95339a2d864f177
screens2: %TEMP%\p302-evidence-stage3-9-screens2-46ac90d746b54cf49719403e9733dd19
dpi     : %TEMP%\p302-evidence-stage3-9-dpi-d9e68fbadff24244ba46b0bafec1f543
```

PNG и markdown-evidence оставлены во временных каталогах и не добавлялись в
репозиторий. Это не подменяет самодостаточные повторяемые драйверы и их exit
contracts, которые вошли в исходники.

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
NONACTIVATING_RUNTIME_VERIFIED=true
UI_CAPTURE_RUNTIME_VERIFIED=true
SCREENS_CAPTURE_VERIFIED=true
SCREENS2_CAPTURE_VERIFIED=true
DPI_SIMULATION_VERIFIED=true
GDI_CAPTURE_OUTPUT_ROOT_VERIFIED=true
REFERENCE_VISUAL_VERIFIED=PARTIAL
COM_VERIFIED=DEFERRED_BY_OWNER
PRINT_VERIFIED=NOT_RUN_BY_SCOPE
OS_DPI_125_150_VERIFIED=NOT_RUN_ON_HOST
CODE_COVERAGE_MEASURED=NOT_REMEASURED_IN_SERIES
```

## 5. Что остаётся по плану

1. Использовать уже проверенный неактивирующий режим для оставшихся
   поверхностей, которых нет в `run-screens`/`run-screens2`: предпросмотр,
   F5/F6-цепочки и отдельные пустые состояния, сохраняя правило «не отправлять
   физическое задание в spooler».
2. Сопоставить новые PNG с KonvertProNet. Сейчас
   `REFERENCE_VISUAL_VERIFIED=PARTIAL`: полноценный пиксельный референс есть
   только для главного экрана, поэтому общий процент сходства всё ещё нельзя
   выдавать как измеренный.
3. Отдельными стендами закрыть ещё не выполненные внешние ворота:
   реальный DPI 125/150%, холодная COM-активация, виртуальная/реальная печать
   в заранее согласованном безопасном сценарии.
4. Не смешивать эти внешние проверки с текущим доказательством и
   не объявлять общий процент визуального сходства измеренным без матрицы
   сопоставимых изображений.

## 6. Git и чистота

```text
Коммиты серии:
`d50be67 fix(acceptance): add non-activating screenshot mode`
`b5cb3be fix(acceptance): verify non-activating capture at runtime`
Ветка: main
Изменены только файлы acceptance-harness/драйверов, runtime smoke и static guard.
graphify-out/ существовал до серии, не изменялся и в коммит не включён.
```
