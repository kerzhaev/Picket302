# Phase 2 — Reference matrix (stage 3, 2026-11-08)

Reference declared in the brief: `https://github.com/kerzhaev/Picket302`.

**Structural finding (baseline.md §1.1):** `origin` **is** that repository, and its
only ref equals the audited HEAD byte-for-byte (`aee4ea8`). The published repo
contains **zero image files**. Therefore no *distinct* pixel reference exists
for any screen except what is already inside this repo:

| Available reference artefact | Covers | Type |
|---|---|---|
| `docs/ux-audit-2026-11-03/референс-2026-11-05/01-главное-адресная-книга.png` — legacy AMS ENVELOPS main window | Surface 1 (main screen) | **Pixel reference** |
| `EVIDENCE/EVIDENCE/ui_control_trees.txt` — 54 legacy DFM forms | structure of legacy dialogs | Textual |
| `EVIDENCE/EVIDENCE/ui_captions_reference.txt` — legacy caption inventory | wording of legacy dialogs | Textual |
| `EVIDENCE/EVIDENCE/screenshot_index.txt` — 133 screenshot names | — | Index only, **images absent** |
| `D:\KonvertProNet\KonvertProNet.exe` | everything | **Not launched** — running it drives the legacy print pipeline (forbidden by §4) |

Screens without a pixel reference are marked `REFERENCE_SCREEN_UNAVAILABLE`
per §8 — not scored by assumption (§9). Their structural state is evidenced by
runtime screenshots (§runtime-evidence.md) and layout-contract tests.

## Screenshots of this series

All captured from the REAL app on isolated profiles by `run-screens.ps1` /
`run-emptybook.ps1` / `run-noaddress.ps1` / `run-printflow.ps1` /
`run-previewflow.ps1` (evidence root `%TEMP%\p302-evidence-2026-11-08-stage3`):
`main-1280x720.png`, `main-1000x700.png`, `01-адресная-книга-пустая-1280x720.png`,
`05-counterparty-card.png`, `06-mail-card.png`, `07-grid-appearance.png`,
`08-blank-editor.png`, `09-clipart-picker.png`, `10-distribution-editor.png`,
`20-карточка-отправления-f5.png`, `21-диалог-печати-f5.png`,
`22-предпросмотр-f6.png`, `24-messagebox-формирование-*.png`,
`25-подача-конвертов-*.png`, `31-messagebox-формирование-*.png`,
`32-предпросмотр-с-полями.png`, `09-no-address-warning.png`,
`10-no-address-warning-f6.png`.

## Matrix

| # | Surface | Reference | Current shot | Found deviations | Solution | Status |
|---|---|---|---|---|---|---|
| 1 | Главный экран | **available** (legacy PNG) | `main-1280x720.png`, `main-1000x700.png` | см. оценку ниже | — | SCORED 90 |
| 2 | Адресная книга с данными | UNAVAILABLE (same window as #1; demo data shown in `main-*.png` after SEED) | covered by #1 | — | — | UNVERIFIED (structural PASS: layout tests) |
| 3 | Пустая адресная книга | UNAVAILABLE | `01-адресная-книга-пустая-1280x720.png` | — | empty-state hints per §11.1 | CAPTURED, UNVERIFIED vs reference |
| 4 | Карточка контрагента | UNAVAILABLE | `05-counterparty-card.png` | кнопки без AccessibleName (UX3-02) | FIXED | CAPTURED+FIXED |
| 5 | Карточка с тремя адресами | UNAVAILABLE | inside `05-counterparty-card.png` (3 AddressEditor-блока) | ALL-CAPS заголовки (UX3-03) | FIXED | CAPTURED+FIXED |
| 6 | Карточка с пользовательскими полями | UNAVAILABLE | inside #4 | — | — | CAPTURED, UNVERIFIED |
| 7 | Таблица с длинными значениями | UNAVAILABLE | `main-*.png` (SEED-SCREENS сеет длинное наименование/адрес) | обрезаний не найдено (зонд) | — | CAPTURED, UNVERIFIED |
| 8 | Настройка внешнего вида таблиц | UNAVAILABLE | `07-grid-appearance.png` | кнопки без AccessibleName (UX3-02) | FIXED | CAPTURED+FIXED |
| 9 | Редактор рассылки | UNAVAILABLE | `10-distribution-editor.png` | — | — | CAPTURED, UNVERIFIED |
| 10 | Карточка отправления | UNAVAILABLE | `06-mail-card.png`, `20-карточка-отправления-f5.png` | — | — | CAPTURED, UNVERIFIED |
| 11 | Окно печати текущего конверта | UNAVAILABLE | `21-диалог-печати-f5.png` | — | — | CAPTURED, UNVERIFIED |
| 12 | Окно печати рассылки | UNAVAILABLE | same dialog, batch mode (printflow 25/31) | — | — | CAPTURED, UNVERIFIED |
| 13 | Диалог подачи конвертов | UNAVAILABLE | `25-подача-конвертов-*.png` | — | — | CAPTURED, UNVERIFIED |
| 14 | Предпросмотр | UNAVAILABLE | `32-предпросмотр-с-полями.png`, `22-предпросмотр-f6.png` | — | рендер-дефект исправлен ранее (rev.12) | CAPTURED, UNVERIFIED |
| 15 | Вкладка «Отправленные» | UNAVAILABLE | not captured | — | — | NOT_CAPTURED |
| 16 | Пустое состояние «Отправленные» | UNAVAILABLE | not captured | — | — | NOT_CAPTURED |
| 17 | Редактор бланка | UNAVAILABLE | `08-blank-editor.png` | — | — | CAPTURED, UNVERIFIED |
| 18 | Выбор клипарта | UNAVAILABLE | `09-clipart-picker.png` | **P0: падение при открытии (UX3-01)** | FIXED | CAPTURED+FIXED |
| 19 | Ошибка отсутствующего адреса | UNAVAILABLE | `09-no-address-warning.png`, `10-no-address-warning-f6.png` | — | политика варианта A подтверждена runtime | CAPTURED |
| 20 | Ошибка отсутствующего шаблона | UNAVAILABLE | (нет снимка; путь покрыт кодом+тестами) | — | — | NOT_CAPTURED |
| 21 | Ошибка отсутствующего принтера | UNAVAILABLE | нет снимка; состояние диалога без принтеров покрыто `FormEnvelopePrint`+тестами | — | — | NOT_CAPTURED |
| 22 | Предупреждение о неверном размере бумаги | UNAVAILABLE | нет снимка; covered by `FormPaperSizeBlocked`+PrintDialogsTests | — | — | NOT_CAPTURED |
| 23 | Узкое окно 1000×700 | UNAVAILABLE (reference is 1080p) | `main-1000x700.png` | — | MainShellNarrowLayoutTests 6/6 | CAPTURED |
| 24 | Окно 1280×720 | UNAVAILABLE | `main-1280x720.png` | — | — | CAPTURED |
| 25 | Масштаб 125% | UNAVAILABLE | not run (host scale 100%) | — | — | NOT_RUN (honest limitation) |
| 26 | Масштаб 150% | UNAVAILABLE | not run (host scale 100%); zoom 150% предпросмотра покрыт тестами | — | — | NOT_RUN (honest limitation) |

## Score of surface 1 (main screen) — the only reference-bearing screen

Five blocks × 0..4 against the legacy reference screenshot:

| Block | Score | Justification (evidence) |
|---|---|---|
| 1. Композиция и расположение блоков | **4** | Двухпанельная структура «От кого / Кому» + редакторы адресов сверху + блок логотипа справа + строка состояния — воспроизведены; вкладки вместо MDI (задокументированное отклонение §8.2 контракта) |
| 2. Типографика и подписи | **3** | Semantics of captions match («Индекс», «Строка 1..6», «От кого», «Кому», «Всего/Выбрано»); Segoe UI 9 pt vs legacy bitmap 8 pt — deliberate deviation (§8.4); капитель удалена (UX3-03) |
| 3. Цвета, кнопки и иерархия действий | **3** | Red/blue role coding preserved (§8.1); primary/secondary button split added (improvement over reference's flat toolbar); neutral selection vs legacy amber (§8.3) |
| 4. Отступы, размеры, отсутствие обрезания | **4** | Зонд обрезки: 0 обрезанных подписей/кнопок; обе геометрии в клиентской области (MainShellNarrowLayoutTests 6/6); оба снимка 1000×700 и 1280×720 чистые |
| 5. Состояния, масштабирование, клавиатура | **4** | Пустое состояние с подсказкой (better than reference), F5/F6/F7 preserved, AccessibleName/Description на главных действиях, zoom-контракт предпросмотра |

**Screen score = 18/20 → 90.**

## Weighted result

Per §9 weighting, only screens WITH reference may be scored. Exactly one
surface qualifies (#1, group «главное окно и адресная книга — 25%»); groups
2–5 have no reference-bearing screens and are `UNVERIFIED`.

- `REFERENCE_VISUAL_SCORE = 90` (single-screen, reference-based)
- `REFERENCE_VISUAL_VERIFIED = PARTIAL` — 1 of 26 surfaces has a pixel
  reference; the rest are evidenced structurally (layout-contract tests,
  runtime screenshots, caption inventory), not by pixel comparison.
- Critical defects: **none open** (UX3-01 P0 fixed and verified).
