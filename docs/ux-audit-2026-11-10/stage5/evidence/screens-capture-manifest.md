# Stage 5 screens capture manifest

Дата: 2026-11-10
Сценарий: `run-screens.ps1`
Режим: скрытый acceptance harness, `PrintWindow`-first, без смены фокуса
Результат: `SCREENS PASS`, `exit 0`

| Файл | SHA-256 | Размер |
|---|---|---:|
| `current-main-1280x720-printwindow.png` | `42D883F812688B84776394EDA3CE25858361131EA1262BE86C6410EC3332B28F` | 45 980 байт |
| `current-main-1000x700-printwindow.png` | `C4E32C6B6A02F43F0BB37138A612618094C88D1F7E80C12462B6391F7779E0C0` | 43 448 байт |
| `current-counterparty-card-printwindow.png` | `A942BD1F16CEC40BB3A1A26042BDCAD661EF50725A1D7142F63F7064DBE083B4` | 18 334 байт |
| `current-mail-card-printwindow.png` | `27C9A2E3BE9ED6D3120611CB62E12B10F96BB29EF135385AB7A304DCB314347E` | 11 167 байт |
| `current-grid-appearance-printwindow.png` | `45C4A32E77F974BB87E62E9E80F5336287464A558DA652FADD32FC8915737A34` | 10 018 байт |
| `current-table-columns-printwindow.png` | `6C475D428A252B3F171437E6935FF54B9F17F38FAEB686A388953C70FE76B589` | 13 558 байт |
| `current-table-properties-printwindow.png` | `28B85DB5AD21FF12F9CF9277A92F19344D99695747AB41AB39228CEE2F10C663` | 13 344 байт |
| `current-blank-editor-printwindow.png` | `28EAA3B73A145CC8FE527C4B5B9035A3A62013E102CF9027A50E4A2A63FDE337` | 24 078 байт |
| `current-clipart-picker-printwindow.png` | `CCE53C18B4D280C221BC660722AE2C3E93A4AE19822BE5976F20980FEACCD899` | 12 433 байт |
| `current-distribution-editor-printwindow.png` | `9845582D8C8BD2EDAAB7AEEA6739F19D1E586C734968ACFB71C8A41303FCF9D0` | 24 376 байт |
| `current-address-search-dialog-printwindow.png` | `80031BF262BD44721A1ACA6BB8EF211BAFE91FD0C06C75E0FC497B9F895C0ED0` | 7 072 байт |
| `current-backup-main-printwindow-repeatable.png` | `E1E07D7890CC3E6ECC6326E84038B0F16165D38D55B3E7290DEF907246AA1771` | 21 293 байт |
| `current-address-filter-menu-printwindow.png` | `8E2D113935794786B672127481E84E046AE541930BD91E9A7A6F38A54EF29237` | 3 529 байт |
| `current-print-menu-printwindow.png` | `B445FADD0060B5596C255B6299E2E0FC801871295B89EE877A07A273ADC61E34` | 6 170 байт |
| `current-quick-filter-menu-printwindow.png` | `8E2D113935794786B672127481E84E046AE541930BD91E9A7A6F38A54EF29237` | 3 529 байт |
| `current-address-table-menu-printwindow.png` | `B64E5D28E59D0199C387D541CEE72BD08AA8A9DA4074C7D24458376CBEFB34C1` | 9 368 байт |

## Runtime evidence

- Снято 16 поверхностей: два размера главного окна, десять рабочих диалогов
  и четыре настоящих меню.
- Диалоги открываются командами `SHOW search`, `SHOW backup`,
  `SHOW tablecolumns` и `SHOW tableproperties`, а меню фильтров — командами
  `FILTERMENU`, `QUICKFILTERMENU`, `PRINTMENU` и `TABLEMENU`; повторяемый
  драйвер `run-screens.ps1` требует все 16 снимков.
- `graceful=True`, `killed=False`, `TreeOk=True`, `HarnessNotExited=False`.
- Временный профиль удалён; `production delta: new=0 changed=0 removed=0`.
- Sentinel реального профиля совпал; в stdout нет `FAIL`.
- Полный сырой журнал первоначальной серии сохранён в
  `screens-happy-evidence-12-surfaces-2026-11-10.md`; повторный прогон после
  добавления F8 в меню сохранён в `screens-happy-evidence-f8-2026-11-10.md`,
  а прогон после добавления иконок — в
  `screens-happy-evidence-icons-2026-11-10.md`; чистый прогон с кнопкой
  быстрых фильтров — в `screens-happy-evidence-quick-filter-2026-11-10.md`.

## Дельта после проверки F8

- `MainShell` и `UiAcceptance` были пересобраны отдельно; solution-сборка сама
  по себе не обновляет бинарь harness, потому что `UiAcceptance.csproj` не
  входит в solution.
- Итоговый `run-screens.ps1`: 16/16 снимков, `exit 0`,
  `graceful=True`, `killed=False`, `TreeOk=True`, production delta `0/0/0`.
- Новый кадр меню печати визуально содержит четыре пункта `F5/F6/F7/F8`.
- Итоговый кадр меню печати содержит иконки у F5/F6/F7/F8 и настроек
  принтера; все изображения созданы существующей векторной темой приложения.
- После добавления быстрых фильтров новый кадр `Вид → Фильтры` содержит
  `Избранные`, `Все категории`, актуальную системную категорию и ниже
  сохранённые пользовательские фильтры; старый кадр без быстрых пунктов
  заменён, его SHA сохранён только в истории отчёта.
- Отдельный runtime-кадр `current-quick-filter-menu-printwindow.png` снят
  после нажатия реальной кнопки `Фильтры…` над таблицей получателей; он
  показывает тот же рабочий dropdown и подтверждает размещение entry point.
- Финальный runtime-кадр поиска переснят после расширения колонки действий:
  `current-address-search-dialog-printwindow.png` содержит все legacy-подобные
  поля без обрезания кнопки «Найти с начала», а сырой журнал сохранён в
  `screens-happy-evidence-search-2026-11-10.md`.
- Итоговый сырой журнал 16-поверхностного прогона сохранён в
  `screens-happy-evidence-table-settings-2026-11-10.md`; он содержит
  `graceful=True`, `killed=False`, `TreeOk=True`, `LOCK RELEASE` и
  production delta `0/0/0`.
- Отдельный сырой журнал контекстного меню сохранён в
  `screens-happy-evidence-table-menu-2026-11-10.md`; команда `TABLEMENU`
  открыла реальный `ContextMenuStrip`, снимок записан до закрытия меню.
- Первый параллельный прогон был корректно отклонён защитой профиля из-за
  одновременной COM-сессии, дописавшей append-only `com-automation.log`;
  raw-диагностика сохранена в `screens-failure-evidence-concurrent-com-2026-11-10.md`.

## Визуальные observations для review

- Свежий главный кадр содержит одну строку меню и фактические demo-данные;
  он пригоден для сопоставления с `reference/legacy-main.png`.
- Карточка контрагента и настройки таблиц не обрезаны, но их visual language
  заметно современнее legacy-скриншотов: Segoe UI, вкладки, большие поля и
  цветовые swatches вместо старых Delphi-панелей.
- Клипарт-пикер показывает 10 значков и отдельную область предпросмотра;
  это подтверждает исправление P0-ошибки открытия, но не является полным
  сравнением с legacy-макетом логотипа.
- Эти наблюдения не превращаются автоматически в общий процент сходства.
- Список столбцов в текущем кадре виден фактически: присутствуют подписи,
  отметки, выбранная строка и четыре команды справа. Ранее пустой кадр был
  получен от stale `Picket302.App.dll`; после отдельной пересборки App и
  harness SHA DLL совпал, а финальный кадр имеет SHA
  `6C475D428A252B3F171437E6935FF54B9F17F38FAEB686A388953C70FE76B589`.
- Контекстное меню строки в текущем кадре содержит legacy-подобные пункты
  `Поиск`, `История печати конвертов`, `Группы конвертов`, `Создать копию
  строки`, `Скопировать в таблицу «От кого»`, `Копировать ячейку`,
  `Экспорт/импорт таблицы` и `Настройка таблицы`; его SHA
  `B64E5D28E59D0199C387D541CEE72BD08AA8A9DA4074C7D24458376CBEFB34C1`.
