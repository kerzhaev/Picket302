# RC S16 — Повтор виртуального печатного конвейера

Дата: 2026-11-12. Код `5533bb2` (RC), Release x64, сборка 0/0. Принтер по
умолчанию «Microsoft Print to PDF» (виртуальный; конфигурация сменилась
с PDF-XChange эпохи stage5). Правила: принтер по умолчанию НЕ менялся,
физической печати нет, foreground не захвачен, спулер-задания
контролировались (0/0 до/после каждого прогона).

## 1. App-level спулер (продуктовый конвейер, evidence/print-s16/rc-s16-spool-2p.pdf)

Проба `P302SpoolProbeRc` (ProjectReference на RC Picket302.Printing):
вызывает **продуктовый** `SystemDrawingPrintSpooler.PrintAsync` (2 страницы,
A4 Portrait) с документированной точкой автоматических проверок
`НастройкаДокументаДляПроверок`, через которую включён `PrintToFile`
штатного драйвера (единственный неинтерактивный путь без прав админа —
PORTPROMPT не показывается).

```text
SPOOLPROBE2 printer=[Microsoft Print to PDF] pages=2 pid=103664
PRINT_ASYNC_COMPLETED rendered=2
RESULT: FILE_CREATED size=1199 head=[%PDF-1.7]
sha256=7520508AB55CC5402A2C1487A295F73FE6AB2C12C4E0381026579DC7DCD38141
PDF_MAGIC_OK
JOBS_BEFORE=0 JOBS_AFTER=0   (file(1): «PDF document, version 1.7, 2 page(s)»)
```

Число страниц артефакта = 2 = задано; страницы отрисованы продуктовым
рендером (rendered=2); задание не осталось в очереди.

## 2. Engine-level PrintToFile (evidence/print-s16/rc-s16-engine.pdf)

Проба `P302PrintProbe` (PrintDocument → PrintToFile на именованный
виртуальный принтер; framework-уровень):

```text
RESULT: FILE_CREATED size=89347 head=[%PDF-1.7]
sha256=C7E663CAE8AF929A373B0C7D18EF8EC8AA6596B7DFCBE88E0C8BFBC08C5BBE9F
PDF_MAGIC_OK  (file(1): «PDF document, version 1.7, 1 page(s)»)
```

## 3. Юнит-уровень конвейера на текущем SHA (Release, 5533bb2)

| Компонент | Тесты | Прогон |
|---|---|---|
| Предпросмотр (координаты preview=print, калибровка) | PrintPreviewEngineTests | 34/34 (сводный прогон ниже) |
| Шрифт индекса (толщина, разрядка, блокировка) | PostIndexRendererTests | 34/34 |
| **Блокировка при отсутствии ZIPcode.ttf** | `BlankEditorTests.Validate_ШрифтZipcodeОтсутствует_Блокировка` | 19/19 BlankEditorTests |
| Логотип | EnvelopeLogoRenderingTests | в strict-наборе 1399 |
| Размеры (PaperSizeValidator, блокировка/±1,01 мм) | PaperSizeValidatorTests | в strict-наборе |
| Аудит (формат записи 65, сессии) | PrintAuditWriterTests, PrintSessionAuditTests | 34/34 (сводный) |
| Отмена | CancellationToken в PrintAsync; PrintSessionController | в strict-наборе |

Сводный прогон: `dotnet test -c Release --filter "~PostIndexRendererTests|~PrintAuditWriterTests|~PrintSessionAuditTests|~PrintPreviewEngineTests"` → **34/34**;
`BlankEditorTests` → **19/19**. ZIPcode.ttf на хосте НЕ установлен и НЕ
копировался (лицензия не подтверждена) — блокировка при отсутствии шрифта
подтверждена тестом (`Validate_ШрифтZipcodeОтсутствует_Блокировка`),
штамп-рендер реальным шрифтом — NOT_RUN_ON_HOST (как в stage5).

## 4. Итог

```text
VIRTUAL_PRINT_VERIFIED=true (RC 5533bb2: app-spooler→PDF 2 стр. + engine→PDF 1 стр.,
  читаемые, magic OK, SHA зафиксированы; спулер 0/0)
POST_INDEX_TEXT_RENDER_VERIFIED=true (юнит)
POST_INDEX_STAMP_VERIFIED=NOT_RUN_ON_HOST (нет лицензированного ZIPcode.ttf — блокировка доказана тестом)
```

Физическая печать — S18/S19 (стенд отсутствует). Поля/размеры страниц в
PDF: app-проба рендерит пустые страницы через продуктовый конвейер
(проверка транспорта, не макета); макетные проверки (поля/логотип/адрес)
покрыты рендер-тестами и будут сняты в S20/S24 с реальными шаблонами.

Артефакты: evidence/print-s16/{rc-s16-engine.pdf, rc-s16-spool-2p.pdf};
пробы: Temp\p302-rc-print-20261112\spoolprobe (RC ProjectReference),
stage5 printprobe (framework, неизменен).
