# Phase 1 — Design contract (stage 3, 2026-11-08)

This contract is **not** a new invention. It codifies the system that the code
already implements in `src/Picket302.App/UI/Components/EnvelopeUiTheme.cs`
(`EnvelopeUiTheme`), and it is checked against the only genuine pixel reference
available on this host: the legacy AMS ENVELOPS main window
(`docs/ux-audit-2026-11-03/референс-2026-11-05/01-главное-адресная-книга.png`).

**Rule of precedence (per §7 of the brief):** the project's reference wins over
personal preference. Where this contract deviates from the legacy reference, the
deviation is listed explicitly in §8.

---

## 1. Purpose of the interface

Picket302 is a **desktop operator application** for a Russian mail-rooms
workflow: address book → envelope preparation → mass mailings → printing →
record of dispatched items. It is used by an operator all day, on a 1000×700
monitor, in Russian, with long legal names and long addresses.

The interface is therefore a **workbench**, not a product landing page.

## 2. Visual character

| Rule | Statement | Enforced by |
|---|---|---|
| C1 | Working, calm, applied. Dense but breathable. | `EnvelopeUiTheme` tokens only |
| C2 | No decorative SaaS styling — no gradients, no floating cards, no drop shadows. | Flat `Panel`/`TableLayoutPanel` surfaces, `BorderStyle.FixedSingle` |
| C3 | No excessive corner rounding — WinForms square-ish controls are kept. | System buttons |
| C4 | **No ALL CAPS in ordinary captions.** | Captions are sentence case: «Печать текущего конверта», «Предпросмотр и печать», «Добавить». Uppercase is reserved for table column headers and technical codes (ИНН, БИК, ZIP). |
| C5 | No visual noise: no gratuitous colour, no gradients, no decorative icons. | Palette limited to 6 tokens + 3 semantic accents |
| C6 | Clear hierarchy of print actions. | Primary vs. secondary button styling (§3.4) |

## 3. Palette (6 core tokens — exact hex)

Taken verbatim from `EnvelopeUiTheme` so the contract and the code cannot drift.

| Role | Token | Hex | Usage |
|---|---|---|---|
| Background (workspace) | `WorkspaceBackground` | `#EEEEEE` | Form background, empty canvas around panels |
| Surface (cards, fields) | `CardBackground` | `#FFFFFF` | `GroupBox`, `TextBox`, `Button` face, grid background |
| Primary text | `Text` | `#202020` | All body text |
| Secondary text | `MutedText` | `#565656` | Field labels, status line, empty-state hint |
| Accent (primary action) | `Primary` | `#006699` | Primary buttons, focus ring, selected state |
| Warning / error | `Danger` | `#DC2626` | Destructive actions, invalid-field highlight |

Supporting tokens (also from the theme, not new colours):

| Token | Hex | Meaning |
|---|---|---|
| `PanelBackground` | `#E8E8E8` | Raised panel / action-bar face |
| `Border` | `#A6A6A6` | Thin control borders |
| `StrongBorder` | `#808080` | Focus/separator emphasis |
| `Selection` | `#E8F1FC` | Selected grid row |
| `SenderAccent` | `#B40000` | Sender group heading (legacy role preserved) |
| `RecipientAccent` | `#0052A0` | Recipient group heading (legacy role preserved) |
| `Accent` (amber) | `#BE7300` | Warnings, secondary accent |
| `Success` | `#15803D` | Completed state |

**Semantic colour rule:** red is reserved for destructive actions and errors;
it is never decorative. Sender/recipient red/blue is the *legacy reference's*
role coding (§8.1) and is kept deliberately.

## 4. Typography

| Rule | Value | Source |
|---|---|---|
| T1 Family | `Segoe UI` (single family, no mixing) | `EnvelopeUiTheme.ШрифтСемейство` |
| T2 Body size | **9 pt** | `EnvelopeUiTheme.РазмерШрифта` |
| T3 Compact size (buttons, tool strips, grid headers, status line) | **8.5 pt** | `EnvelopeUiTheme.РазмерКомпактногоШрифта` |
| T4 Headings | 9 pt **Bold** (group/column headings) — no larger decorative sizes | `EnvelopeUiTheme.ШрифтЗаголовка` |
| T5 Button label | 8.5 pt, `Padding(8,4,8,4)` (primary: `Padding(12,4,12,4)`) | `ОформитьОсновнуюКнопку` |
| T6 Minimum legible caption | **8.5 pt** — nothing below | theme floor; `ОформитьКонтрол` raises ≤8.5 pt to 9 pt |
| T7 Long Russian words | Wrap (`AutoSize`, `AutoSizeMode.GrowAndShrink`), never ellipsised away. Labels are `AutoSize=true`; fields are single-line by domain (postal address has 6 discrete lines) | |
| T8 Postal index & technical fields | Monospace **not** used; instead fixed-width input columns so digits align visually; index fields render as **text**, never silently substituted | `PostIndexRenderer` |

**No ALL CAPS rule (C4) vs. T8:** uppercase survives only where the data itself
is uppercase (ИНН/БИК/КПП/ОГРН field labels, ZIP codes, table column headers).

## 5. Layout principles

| ID | Rule |
|---|---|
| L1 | **Stretching:** grids (`DataGridView`), preview canvas, tables of custom fields → `Dock = Fill`. |
| L2 | **Minimum size:** main window `MinimumSize = 1000×700` (`MainShell.cs:204`); card dialog `ClientSize 760×560`; distribution editor `760×480`; clipart picker `480×360`. |
| L3 | **Must wrap:** every action-bar button row — `ToolStrip` with `AutoSize = true` items and `WrapContents = true` (logo group wraps to a second row rather than clipping). |
| L4 | **Must scroll:** the counterparty card's requisites/addresses column lives inside an `AutoScroll` host (`FormCounterpartyEdit.ПостроитьРеквизиты`, `AutoScrollMinSize = (0,760)`); the distribution editor's list and the blank editor's field table scroll. |
| L5 | **`SplitContainer` is allowed** for the main window (senders list ⇄ recipients list) and for the preview (canvas ⇄ controls). It is **not** used for a single stack of unrelated panels. |
| L6 | **Fixed sizes are forbidden** for anything whose content is dynamic: no `Height = N` on a control that holds a variable-length label, no `Width = N` on a button with a text caption. |
| L7 | **Behaviour at 1000×700:** the main window must remain fully operable — no clipped primary action, no hidden button. Secondary actions may move to a second row of the action bar. |
| L8 | **Behaviour at 1280×720:** the reference geometry; no scrollbars where the reference shows none. |
| L9 | **Bottom-docked panels inside an `AutoScroll` host scroll away** — a bottom-pinned panel must be a sibling *outside* the scroll host (this exact bug was found and fixed in the counterparty card on 2026-11-08). |
| L10 | **DPI:** all metrics are in points/`AutoSize`; fixed pixel heights are limited to chrome (rows, headers) and are read from the theme, not hardcoded per form. |

## 6. Accessibility

| ID | Rule |
|---|---|
| A1 | Every interactive control has an `AccessibleName`; non-obvious ones also have `AccessibleDescription` (e.g. «Печать текущего конверта» → «Печатает конверт по текущим адресам редакторов. Горячая клавиша F5.»). |
| A2 | **Visible focus** — `Focus = Primary` ring colour; focus is never suppressed. |
| A3 | **Keyboard navigation** — every dialog sets `AcceptButton`/`CancelButton`; F5/F6/F7 hotkeys are preserved and never removed. |
| A4 | `ToolTip` on icon-bearing controls; but an icon never carries meaning alone — the button keeps its text caption. |
| A5 | No meaning by colour alone: errors pair the red field highlight **with** a message; sender/recipient coding is duplicated by the group heading text. |
| A6 | Error messages are readable sentences naming the object and the remedy («У получателя «…» не задан основной адрес.», «Пропущены без основного адреса (N): …»). |
| A7 | Every modal dialog can be closed: `CancelButton`/`Отмена`, `DialogResult.Cancel`, and a window close path. |

## 7. ASCII wireframes

### 7.1 Main window (address book)

```
+------------------------------------------------------------------------------+
| Файл  Правка  Вид  Данные  Сервис  Печать  Рассылки  Справка                |
+------------------------------------------------------------------------------+
| Адресная книга | Конверты | Рассылки | Отправленные                       |
+------------------------------------------------------------------------------+
| Отправитель            | Получатель             | Вид логотипа                  |
| Индекс: [________][_]  | Индекс: [________][_]  | ( ) С логотипом               |
| Строка 1: [_________]  | Строка 1: [_________]  | ( ) Без логотипа              |
| Строка 2: [_________]  | ...                     | Адрес на конверте:            |
| Строка 3: [_________]  | Строка 6: [_________]  | (o) Отправителя ( ) Получателя|
| Строка 4: [_________]  |                         | [Клипарт...] [Убрать логотип]  |
| Строка 5: [_________]  |                         | Примечание к адресу:           |
| Строка 6: [_________]  |                         | [__________________________]  |
+------------------------------------------------------------------------------+
| От кого                  | Кому                                                 |
| [+Добавить][Изм.][Удал.] | [+Добавить][Изм.][Удал.][Печать конверта][Превью и  |
| Поиск по названию:        | [печать][Открыть в Excel][Сохранить вид][Вид табл.]|
| [_______________________] | Индекс | Получатель | Город | Улица | Группа |
| Индекс | Наименование     | ----------------------------------------------------------|
| 101000 | ООО «Ромашка»     | 125009 | ООО «Ромашка» | Москва | 1 | ...|
+------------------------------------------------------------------------------+
| Всего: 6  Выбрано: 0  Выведено: 0  Кончил: 0  База: picket302.db            |
+------------------------------------------------------------------------------+
```

### 7.2 Address book (empty state)

```
| От кого                | Кому                                              |
| [+Добавить][Изм.][Удал]| [+Добавить][Изм.][Удал.][Печать конверта][…]     |
| Поиск по названию:      |                                                  |
| [___________________]  |        ┌──────────────────────────────┐          |
| ┌───────────────────┐   |        │  Адресная книга пуста.          │          |
| │  Записей нет.      │   |        │  Добавьте первого получателя     │          |
| │  Добавьте первого  │   |        │  кнопкой «Добавить».             │          |
| │  получателя.       │   |        └──────────────────────────────┘          |
| └───────────────────┘   |                                                  |
```

Empty grids are **hidden** and replaced by `EmptyStateHint` (mutually exclusive
visibility), so there is no meaningless scrollbar and no false row.

### 7.3 Counterparty card

```
+-------------------------------------------------------+
| [Реквизиты] [Банк] [Паспорт]                         |  <- TabControl
| +---------------------------------------------------+ |
| | (o) Юридическое  ( ) Физическое                   | |  <- type
| | +-----------------------------------------------+ | |
| | | Наименование: [____________________________]   | | |
| | | ИНН: [_______] КПП: [_______] ОГРН: [______]   | | |
| | +-----------------------------------------------+ | |
| | | Юридический адрес      | Фактический адрес     | | |  <- 2 cols
| | | Индекс: [____]         | Индекс: [____]       | | |
| | | Строка 1: [________]    | Строка 1: [________]  | | |
| | | ...                     | ...                  | | |
| | +-----------------------------------------------+ | |
| | | Почтовый адрес         |                       | | |
| | +-----------------------------------------------+ | |
| | +-----------------------------------------------+ | |
| | | Поле | Значение | Тег      (custom fields)   | | |  <- Dock=Bottom,
| | +-----------------------------------------------+ | |     OUTSIDE scroll host
| +---------------------------------------------------+ |
| | [Сохранить] [Отмена]                               | |  <- Dock=Bottom on form
+-------------------------------------------------------+
```

### 7.4 Print window (envelope)

```
+---------------------------------------------------------------+
| Печать конверта                                              |
| Шаблон: [Конверт C6 (приёмка)  v]   Принтер: [Microsoft ... v]|
| Экземпляр: [1]  Уведомление: ( ) Простое ( ) Заказное          |
| [Расчёт] [Печать] [Предпросмотр]              [Отмена]        |
+---------------------------------------------------------------+
```

### 7.5 Print preview

```
+---------------------------------------------------------------+
| Предпросмотр печати                          [Закрыть]        |
| +--------------------------------------+  Масштаб: [150% v] |
| |                                      |  Поля:              |
| |        (canvas — page, scrollable)    |   ( ) Показывать     |
| |                                      |   (o) Скрывать       |
| |                                      |  [Напечатать]        |
| +--------------------------------------+                     |
+---------------------------------------------------------------+
```

## 8. Deviations from the legacy reference (all deliberate)

| # | Deviation | Reason |
|---|---|---|
| 8.1 | The reference encodes sender = red, recipient = blue. Picket302 **keeps** this role coding. | It is a genuine semantic distinction in the reference, not decoration. |
| 8.2 | The reference keeps a large legacy ribbon ("Призрак"/"Захват нашей базы") and MDI chrome. Picket302 uses a menu strip + 4 tabs. | Documented in `docs/UX-REDESIGN-2026-11-05.md` §3: the legacy capture/multi-user shell is a **different product surface**, not the address-book workflow being reimplemented. |
| 8.3 | The reference uses a yellow/amber selection highlight; Picket302 uses `#E8F1FC`. | The amber belongs to the legacy skin; a neutral blue selection is more legible on the white grid and matches `Primary`. |
| 8.4 | The reference's font is a bitmap-era Windows font at 8 pt. Picket302 uses Segoe UI 9 pt. | Segoe UI is the Windows operator font; 9 pt is the legibility floor for Cyrillic legal names. |
| 8.5 | The reference has no explicit empty-state. Picket302 adds `EmptyStateHint`. | §11.1 of the brief requires an empty state that proposes a next action. |

## 9. What this contract forbids (checklist for Phase 3)

1. A `Height`/`Width` on a control that holds variable text.
2. A button whose caption disappears at narrow width (wrap instead).
3. ALL CAPS in an ordinary caption.
4. An icon-only button without a text caption and without `AccessibleName`.
5. A second helper `Form` used purely as a layout host (extra HWND).
6. A bottom-docked panel placed *inside* an `AutoScroll` container.
7. A colour hardcoded in a form instead of a `EnvelopeUiTheme` token.
8. Hiding or disabling a primary action because the window is narrow.
9. Removing F5/F6/F7.
10. Any modal dialog without a cancel/close path.
