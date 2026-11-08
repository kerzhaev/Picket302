# Phase 5 — Print, post-index, ZIPcode: audit results (stage 3, 2026-11-08)

## 1. Status flags (honest)

| Flag | Value | Basis |
|---|---|---|
| `PRINT_VERIFIED` | **NOT_RUN_BY_SCOPE** | No physical printer was agreed; nothing was sent to a physical device. Spooler evidence below. |
| `POST_INDEX_TEXT_RENDER_VERIFIED` | **true** | `PREVIEW-SCAN` proof: field `[edtIndeks_to]` (BlankFieldKind.Text) rendered dark=586 px in preview (`previewflow-happy-evidence.md`), outside-printable=0. |
| `POST_INDEX_STAMP_VERIFIED` | **NOT_RUN_ON_HOST** | The machine-readable stamp requires the licensed `ZIPcode.ttf`, which is absent from the app and unregistered on this host (details §3). |
| `PHYSICAL_PRINTER_USED` | **false** | — |
| `SPOOLER_EVIDENCE` | **0→0 in every run** | `Win32_PrintJob` counted before/after each driver; noaddress additionally asserts it as a condition. |
| `COM_VERIFIED` | **DEFERRED_BY_OWNER** | Nothing registered, nothing launched, no cold activation. |

## 2. What was verified without hardware

Per §13 allowed list:

- **Расчётная геометрия** — `PostIndexSpec`/`PostIndexRendererTests`: ГОСТ Р 51506-99 dimensions (3.7×5.5 mm digit, 0.45 mm line, 5.4 mm pitch), validation warnings (`LineWidthWarning`, `DigitSizeWarning`).
- **Preview** — full F6 chain with field-bearing template (`run-previewflow.ps1`, PS 5.1 + PS 7): canvas 1296×916 (C6 at 4 px/mm), `dimsOk=true`, dark=3296, `outsidePrintable=0`, per-field darkness `[edt_str_to1]=1883, [edt_str_to2]=827, [edtIndeks_to]=586` — three distinct field renderings, not a blank canvas.
- **Ограничение printable area** — `outsidePrintable=0` in the pixel scan; blank C6 layout clamps SEEDFIELDS coordinates to printable∩blank.
- **Предупреждения** — paper-size blocked dialog covered by `PrintDialogsTests` (16/16) and `FormPaperSizeBlocked`; oversize warning by `FormOversizeWarning`/`OversizeWarningPresenter` tests.
- **Spooler до/после** — 0→0 in all 10 driver runs (5 drivers × 2 shells).
- **Отсутствие печати после отмены** — F5 cancel path in printflow: dialog cancelled, no job (`printflow-happy-evidence.md`).
- **Preparation** — this checklist (§5).

## 3. ZIPcode audit (facts)

| Fact | Evidence |
|---|---|
| `ZIPcode.ttf` is **not shipped** with the app | No `.ttf` anywhere in `src/` or build output; `ZipcodeFontGuard.ResolveFontPath` searches only `AppContext.BaseDirectory[/Fonts]` |
| `ZIPcode` is **not installed** on host | `InstalledFontCollection` has no `ZIPcode` family; `HKCU\…\Fonts` has no `ZIPcode` entry |
| A file `D:\KonvertProNet\ZIPcode.ttf` (8008 B) **exists on disk** | Verified `Get-Item`; **licence unconfirmed** → not copied into the repo, not registered, not loaded (hard constraint §3: no font without confirmed licence) |
| Guard blocks printing when font is absent | `PostIndexRenderer` throws `PostIndexBlockedException` with `MissingFontWarning` = «Шрифт ZIPcode не найден в системе. Требуется переустановить системные шрифты из комплекта поставки. Подмена на Arial/Times не допускается.» |
| No silent fallback | Guard has no Arial/Times substitution path; template uses `BlankFieldKind.Text` for the readable index — never pretending to be the stamp |
| Preview/print same contract | Both consume `PrintPreviewEngine`/`PrintDataSource`; preview replay defect (mm-frame) fixed in rev.12 — preview and print now share the pixel-frame metafile contract |

**The way to close `POST_INDEX_STAMP_VERIFIED`:** the owner supplies a
licensed `ZIPcode.ttf`; it is placed into the app's font directory (or
installed); then a physical-print stand run proves the stamp. No other path
exists by design.

## 4. What remains NOT verified (and why)

| Item | Reason |
|---|---|
| Physical print (`PRINT_VERIFIED`) | No agreed test printer in scope |
| Machine stamp rendering | Licensed font absent |
| COM / cold activation | DEFERRED_BY_OWNER |
| 125 % / 150 % OS DPI scaling | Host runs 100 %; not simulated |

## 5. Physical-print stand checklist (for the owner)

1. Модель принтера: ______
2. Версия драйвера: ______
3. Формат бумаги: C6 (114×162) / _____
4. Лоток: ручная подача / _____
5. Ориентация: ландшафт (конверт) / _____
6. Тип конверта: ______
7. Калибровочное смещение (мм): X=___ Y=___
8. Тестовый адрес: индекс 125009, «ООО «Получатель Приёмки»», ул. Тверская, 7, г. Москва
9. Ожидаемые координаты: из `PREVIEW-SCAN` bbox (x=81..796, y=125..857 при 4 px/мм → мм×/4)
10. Допуск: ±1 мм по каждой строке
11. Фото/скан результата: приложить к evidence-каталогу
12. Сопоставление preview↔печать: наложить скан на `32-предпросмотр-с-полями.png`; совпадение строк и индекса ±1 мм = PASS
