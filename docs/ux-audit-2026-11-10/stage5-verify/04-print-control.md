# Stage 5 (verify) — Этап 5: контролируемая печать и данные

## Ограничения среды (зафиксированы этапом 0)

- принтеры: **PDF-XChange 5.0 for ABBYY (по умолчанию, виртуальный)**,
  OneNote (Desktop) → `nul:`, Microsoft Print to PDF → `PORTPROMPT:`;
  физических принтеров нет;
- прав администратора нет: добавить принтер/порт нельзя,
  `PrintQueue.Pause()` → Access denied (проверено);
- пользователь работает за ПК: диалог порта (PORTPROMPT «Сохранить как…»
  / окно PDF-XChange) украл бы фокус — запрещён границами задания;
- принтер по умолчанию не менялся.

Различение уровней по заданию: «построен документ» / «задание попало в
spooler» / «создан корректный виртуальный результат» / «физически
напечатано» — разделены ниже.

## Уровень 1. «Построен документ» (предпросмотр/рендеринг)

- `PREVIEWSCAN` драйвера `run-previewflow` (pixel-scan холста
  предпросмотра, непустая отрисовка) — PASS в моём прогоне 20:40:50–
  20:41:08 (`evidence/runtime/previewflow-happy-evidence.md`,
  `32-предпросмотр-с-полями.png`);
- юнит-тесты рендеринга (PostIndexRenderer, QrCodeRenderer,
  ServiceStampRenderer, EnvelopeLogoRendering, PrintPreviewEngine,
  PaperSizeValidator и др.) входят в 1370/1370 non-COM набора.

## Уровень 2+3. «Задание в spooler» + «корректный виртуальный результат»

### 2a. Движок печати (engine-level, PrintToFile)

Инструмент: `P302PrintProbe` (scratch): `PrintDocument` →
`PrinterSettings.PrintToFile=true` + `PrintFileName`, принтер
**Microsoft Print to PDF** (точно назван), одна страница с текстом
«PICKET302 STAGE5 PRINT PROBE».

Результат (17:18:29Z):

```text
RESULT: FILE_CREATED size=89347 head=[%PDF-1.7]
sha256=A4B9A70F5A40B71303BD0AB217C42AA155021D494327146E234C529909767D57
PDF_MAGIC_OK; exit=0; 3279 мс
JOBS_BEFORE=0 JOBS_AFTER=0 ALL_QUEUES_JOBS=0
FG_UNCHANGED=True (диалогов не было, foreground пользователя не менялся)
```

### 2b. Штатный спулер приложения (app-level)

Инструмент: `P302SpoolProbe` (scratch) — вызывает **продуктовый**
`SystemDrawingPrintSpooler.PrintAsync` (2 страницы, A4, Portrait,
принтер Microsoft Print to PDF) с документированной тестовой точкой
`НастройкаДокументаДляПроверок`, через которую включён `PrintToFile`
штатного драйвера (единственный способ избежать интерактивного
PORTPROMPT без прав администратора).

Результат (17:28:40Z):

```text
PRINT_ASYNC_COMPLETED rendered=2
RESULT: FILE_CREATED size=1199 head=[%PDF-1.7] PDF_MAGIC_OK
sha256=F81C0D115841EF8382D89B0728AD3F4A70B7E7775B5BCAFD16B30D6AB6632819
```

Задание наблюдалось **в реальной очереди** во время прогона (опрос
100 мс):

```text
Microsoft Print to PDF #id3 doc=[PICKET302-STAGE5-SPOOL-PROBE]
  status=Spooling → Spooling,Printing,Retained (size 2981→5929)
  → Printing,Retained → завершено
leftover jobs after run: Microsoft Print to PDF=0;
  PDF-XChange 5.0 for ABBYY=0; OneNote (Desktop)=0
FG_UNCHANGED: True
```

Интерпретация: цепочка «продуктовый спулер → winspool → очередь →
драйвер → файл» работает; задание попало **только** в выбранную
очередь; неожиданных заданий на других принтерах нет; размер 1199 B
соответствует двум пустым страницам (render-callback пробы ничего не
рисует — проба проверяет транспорт, не вёрстку; вёрстка доказана
уровнем 1 и юнит-тестами).

## Уровень UI-цепочки (до spooler)

Полные модальные цепочки доказаны драйверами (мой прогон 20:38–20:41,
все exit=0, `spooler jobs before/after: 0`):

- `run-noaddress`: F5/F6 без адреса → MessageBox «Печать»,
  MAILCOUNT=0, spooler 0 (негативный путь);
- `run-noprinter`: F5 → «Принтер по умолчанию не установлен»
  (реальный код-путь с подменой PrinterManager), окна «Подача
  конвертов»/«Предпросмотр» не появились, данные не потеряны;
- `run-previewflow`: F6 → карточка → «Печать» → «Подача конвертов» →
  «Предпросмотр печати» + PREVIEWSCAN → Закрыть, spooler 0;
- `run-printflow`: F5/F6 цепочки до диалога печати и предпросмотра;
- `run-screens2` S4/S5: «Шаблон не выбран.»; SEEDTEMPLATE → F7 →
  «Проверка размера бумаги» (блокировка неверного размера).

**Граница:** клик «Печать» в `FormEnvelopePrint`/`FormPrintPreview`,
доводящий задание до реального spooler из UI, намеренно не выполнялся:
выбранный принтер — интерактивный виртуальный (PORTPROMPT/порт
PDF-XChange), диалог сохранения украл бы фокус пользователя.
Компенсация: уровень 2b доказывает сегмент «spooler → очередь → файл»
штатным кодом, а UI-цепочки доказаны до границы spooler.

## Аудит и данные

- `print_audit.log` production-профиля не изменялся (fingerprint
  байт-в-байт до/после всех прогонов); журналы аудита печати
  UI-прогонов писались в изолированные temp-профили harness
  (удалены, `production delta new=0 changed=0 removed=0`);
- запись отправления в БД (mails) на UI-уровне доказана
  `run-screens2` S3 (строка появляется во вкладке «Отправленные»,
  MAILCOUNT/SENTINFO) на изолированной БД;
- COM-путь `PrintEnvelope` (запись отправления через COM) не
  выполнялся — см. 03-com (граница COM_VERIFIED=PARTIAL).

## Флаги

```text
VIRTUAL_PRINT_VERIFIED=true (engine PrintToFile + app-level штатным
  спулером: задание в очереди выбранного виртуального принтера и
  корректный PDF-результат; UI-клик до spooler — граница выше)
PHYSICAL_PRINT_VERIFIED=false (физических принтеров на хосте нет; N/A)
```

## Артефакты

- `evidence/print/probe-201829.pdf` (89 347 B, engine-level),
- `evidence/print/app-spool-probe-202840.pdf` (1 199 B, app-level),
- `evidence/print/app-spool-probe.out` (вывод пробы),
- `evidence/print/spool-test.json` (первая попытка с Pause — Access
  denied, зафиксирована как ограничение среды),
- `evidence/runtime/*printflow*/*previewflow*/*noaddress*/*noprinter*`
  — evidence UI-цепочек;
- скрипты и исходники проб: scratch-каталог сессии (`printprobe/`,
  `spoolprobe/`, `print-run.ps1`, `spool-test2.ps1`).
