# RC S03 — Свежий захват Picket302 (неактивирующий harness)

Дата: 2026-11-10, 20:44–20:48 UTC. Код: worktree `rc-20261110`, коммит
`1f7d195` (дерево чистое, кроме нового каталога evidence), сборка Release
выполнена явно перед снимками (ENV3-01):
`dotnet build src/Picket302.App -c Release`, `dotnet build tools/ui-acceptance -c Release` — 0 ошибок.

Двоичные файлы захвата (in-process harness; формы Picket302.App загружаются
в процесс UiAcceptance.exe из этого worktree):

| Файл | SHA-256 (первые 16) |
|---|---|
| `UiAcceptance.exe` | `2e29a2621f4190ae` |
| `Picket302.App.dll` | `e9326475de7b115f` |

DPI: системный 96 (запрос GDI `DpiX=96` перед прогонами; водитель run-dpi
дополнительно симулирует WM_DPICHANGED 96→120→144→96 — снимки 50–54).
Метод захвата: PrintWindow (PW_RENDERFULLCONTENT) без активации окна;
спулер принтера не затрагивался (before=0/after=0 во всех прогонах).

## 1. Прогоны драйверов (все PASS, exit 0)

| Драйвер | UTC | PID harness | READY (HWND/PID) | Временный профиль | Снимков | Spooler | Verdict |
|---|---|---:|---|---|---:|---|---|
| run-screens | 20:44:09–45 | 3792 | 14224312/3792 | `…\Picket302Acceptance\6e0435db…` (удалён) | 16 | — | PASS |
| run-screens2 | 20:45:26–55 | 87292 | 19401796/87292 | `…79bd4863…` (удалён) | 12 | 0→0 | PASS |
| run-stage5-menu | 20:45:56–20:46:01 | 115460 | 23726430/115460 | `…fc41f7ff…` (удалён) | 4 | — | PASS |
| run-stage5-tabs | 20:46:02–23 | 45956 | 19662264/45956 | `…43f46d16…` (удалён) | 4 | — | PASS |
| run-stage5-preview | 20:46:25–37 | 61908 | 30870338/61908 | `…` (удалён) | 2 | 0→0 | PASS |
| run-stage5-extra | 20:46:38–47 | 63160 | 23988574/63160 | `…64cc9459…` (удалён) | 3 | — | PASS |
| run-noaddress | 20:47:01–12 | 28180 | 10947966/28180 | `…665360e1…` (удалён) | 2 | 0→0 | PASS |
| run-noprinter | 20:47:13–23 | 44468 | 2819816/44468 | `…2136a094…` (удалён) | 1 | 0→0 | PASS |
| run-previewflow | 20:47:25–42 | 47280 | 14027922/47280 | `…06f567fb…` (удалён) | 2 | 0→0 | PASS |
| run-emptybook | 20:47:42–47 | 100856 | 11144574/100856 | `…32e27619…` (удалён) | 1 | — | PASS |
| run-dpi | 20:47:48–20:48:07 | 70880 | 23989212/70880 | `…06ce7fde…` (удалён) | 5 | 0→0 | PASS |

Итог: 45 PNG + 11 журналов в `evidence/runtime-s03/` (~1 МБ).

## 2. Чистота среды

- Production-профиль `%AppData%\Picket302`: sentinel до/после без изменений,
  **FINAL DELTA new=0 changed=0 removed=0 в каждом прогоне** (fail-closed
  проверка библиотеки).
- Временные изолированные профили `Temp\Picket302Acceptance\*` — все удалены
  (каталог пуст после прогонов).
- Orphan-процессы: `tasklist` по `UiAcceptance.exe` и `Picket302.App.exe` —
  отсутствуют; условия «дерево процессов чисто», «нет живых потомков» True.
- Спулер: во всех прогонах до=0/после=0 — печать не выполнялась.
- Фокус пользователя не захвачен (неактивирующий показ окон).

## 3. Карта снимков → поверхность/состояние

| Группа | Файлы PNG | Состояние |
|---|---|---|
| Главное окно, размеры | `main-1280x720.png`, `main-1000x700.png`, `01-адресная-книга-пустая-1280x720.png` | пустая книга; два размера окна |
| Вкладки | `70-tab1-konverty-seeded`, `71-tab2-rassylki-seeded`, `72-tab1-konverty-empty`, `73-tab2-rassylki-empty` | с посевом и пустые (LAYOUTCHECK нулевых кнопок=0) |
| Поиск | `11-address-search-dialog.png`, `75-search-dialog.png` | диалог поиска, запрос «гост» |
| Фильтры | `13-address-filter-menu.png`, `15-quick-filter-menu.png`, `76-filter-menu-dropdown.png` | меню «Вид→Фильтры» и быстрый слой |
| Настройка таблиц | `07-grid-appearance.png`, `07a-table-columns.png`, `07b-table-properties.png`, `16-address-table-menu.png`, `47-grid-appearance-reset.png` | вкладки состава/свойств, контекстное меню, сброс |
| Печать (UI) | `14-print-menu.png`, `74-print-menu-dropdown.png`, `42-print-dialog.png`, `45-no-template-print-dialog.png` | меню печати (F5–F8), диалог печати, нет шаблона |
| Preview | `32-предпросмотр-с-полями.png`, `78-preview-fullsheet.png`, `31-messagebox-формирование-*.png` | предпросмотр F6 с полями и полный лист |
| Состояния ошибок | `09-no-address-warning.png`, `10-no-address-warning-f6.png`, `60-no-printer-warning.png`, `46-paper-size-blocked.png`, `46a-oversize-warning.png` | guard'ы: нет адреса/принтера/шаблона, блокировка формата |
| DPI-симуляция | `50-main-dpi125.png`, `51-counterparty-dpi125.png`, `52-main-dpi150.png`, `53-mailcard-dpi150.png`, `54-main-dpi100.png` | WM_DPICHANGED 120/144/возврат 96 |
| Вторичные поверхности | `05-counterparty-card`, `06-mail-card`, `08/48-blank-editor`, `09-clipart-picker`, `10-distribution-editor`, `12-backup-main`, `77-backup-restore`, `40-sent-empty`, `41-mailcard-longvalues`, `43-sent-with-data` | карточки/редакторы/резервирование/письма |

Манифест соответствия журналов: каждый PNG перечислен в журнале своего
драйвера с размером в байтах; HWND/PID процесса harness — колонка READY
(таблица §1); для окон диалогов журналы содержат WINDOW-DUMP (hwnd/class/
title). Все снимки получены из кода SHA `1f7d195` (worktree без грязных
правок), изолированный профиль, DPI 96 (или симуляция в run-dpi).

## 4. Критерий этапа

Выполнен: главные и вторичные поверхности сняты на текущем коде через
изолированный неактивирующий harness; для каждого PNG известны SHA кода
(`1f7d195` + хеши бинарных файлов выше), DPI, HWND/PID, профиль и исходное
состояние; harness завершился штатно во всех 11 прогонах (exit 0,
verdict PASS), orphan-процессов нет, production fingerprint неизменен
(0/0/0 в каждом прогоне).
