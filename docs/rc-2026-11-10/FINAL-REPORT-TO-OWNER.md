# FINAL REPORT TO OWNER — RC Picket302 (проверочный пакет, реконсиляция)

Дата: 2026-11-14. Ветка `piscine/picket302-rc`. **Не пушится** — слияние и
решения раздела 7 за владельцем. Настоящая редакция — результат двух
корректирующих проходов по замечаниям независимой проверки.

Первый проход: устранены прежние противоречия сводки (счёт этапов и
счётчики функций, число строгих прогонов, число раундов финального ревью,
неверный статус функции №10); добавлены первичный протокол R4, фактические
счётчики TRX с SHA-256 и независимый прогон сборки.

Второй проход: реестр этапов развёрнут в 50 индивидуальных строк (ровно одна
строка на этап) и устранено расхождение между двумя числами строгого набора в
`S50-final-report.md`.

Третий проход: по находкам независимого документального ревью (R5, протокол
`reviews/FINAL-DOC-REVIEW.md`) исправлены DOC-01 (P1) — шесть строк реестра
функций, отставших от кода волны 5, сверены с кодом и тестами (пересчёт на
этом шаге — 157/4/2, промежуточный; после DOC-03 — 156/5/2) — и DOC-02 (P3) — историческому блоку S8 в ACCEPTANCE.md добавлена
пометка.

Четвёртый проход: по находке повторного ревью (R6, протокол
`reviews/FINAL-DOC-REVIEW-R6.md`) DOC-03 (P2) — функция №158 возвращена в
PARTIAL: клипарт-остаток закрыт волной 5, но составляющая «выбор цвета» по
ТЗ §1.15.7 не реализована (в `BlankFieldSpec`/`BlankSchema.xsd` цвета нет).
Итоги функций пересчитаны: 156/5/2. Код, тесты, конфигурации сборки и бинарные
артефакты ни в одном из проходов не менялись — изменены только документы.

## 1. Идентификаторы

| Параметр | Значение |
|---|---|
| BASE_SHA | `5af9874e8c7550e2accd9c24e4440e659f0810f5` (origin/main) |
| REVIEW-кандидат R4 (вердикт PASS) | `09e152bafb0de8bd4117545db96e5afe24283b6c` |
| Итоговый HEAD (с реконсиляцией) | см. `git rev-parse HEAD` — фиксируется коммитом настоящего документа; код-слой идентичен 09e152b (`git diff --stat 09e152b..HEAD` до этого коммита = только .md) |
| Коммитов от main | `git rev-list --count main..HEAD` — авторитетная команда; число указано в отчёте владельцу для финального SHA (счёт меняется с каждым коммитом прохода, поэтому в документе он не дублируется) |

## 2. Реестр 50 этапов (ровно одна строка на этап)

Арифметика: **COMPLETE 41 + AWAITING_OWNER_ACCEPTANCE 1 +
BLOCKED_EXTERNAL 2 + OWNER_DECISION_REQUIRED 1 +
NOT_STARTED/DEPENDENT_ON_OWNER_DECISION 4 + NOT_RUN 1 = 50.**

Счёт по состояниям (из таблицы ниже; сумма 50): COMPLETE — 41;
BLOCKED_EXTERNAL — 2; OWNER_DECISION_REQUIRED — 1;
NOT_STARTED/DEPENDENT_ON_OWNER_DECISION — 4; NOT_RUN — 1;
AWAITING_OWNER_ACCEPTANCE — 1. Этапы S18, S19, S41, S42, S43, S44, S45,
S49, S50 названы своими ID в соответствующих строках таблицы.

| Этап | Состояние | Критерий закрытия | Первичный источник | Ограничение / решение владельца |
|---|---|---|---|---|
| S01 | COMPLETE | Счётчики функций 163/133/26/4 подтверждены на 5af9874; расхождения чисел тестов (1370→1374→1384) и метрик объяснены; каждая из 30 незавершённых функций получила основание, приоритет, зависимость, закрывающий этап и способ проверки | docs/rc-2026-11-10/S01-baseline.md §4, §5, §8 | — |
| S02 | COMPLETE | Для каждой пары R1–R9 указан источник с перепроверенным SHA-256 и статус (R9 = NOT_SCORED); фото лотка принтера отделено от UI-референсов; взвешенная база сведена к ΣW=16 | docs/rc-2026-11-10/S02-reference-inventory.md §2, §3, §6 | R9 вне базы скоринга — нужен подлинный UI-кадр печатного workflow от владельца |
| S03 | COMPLETE | 45 PNG сняты неактивирующим изолированным harness на текущем коде (11 драйверов, exit 0, verdict PASS); для каждого кадра — SHA кода, DPI, HWND/PID, профиль, исходное состояние; карта дублей 8 групп / 36 уникальных изображений; production fingerprint 0/0/0 | docs/rc-2026-11-10/S03-capture.md §1, §3, §4; docs/rc-2026-11-10/evidence/runtime-s03/ | Дефект harness «цель захвата $h.Hwnd» (run-screens2.ps1) зарегистрирован и не влияет на остальные кадры |
| S04 | COMPLETE | Матрица K1–K6 × R1–R8 рассчитана по 8/8 валидных пар; честный балл OVERALL(R1–R8)=69,9 % зафиксирован как ниже порога 70 % | docs/rc-2026-11-10/S04-visual-score.md (раздел «Матрица», «Итог и флаги») | OVERALL_70_PERCENT_MEASURED=PARTIAL (69,9 %) до пересчёта S06–S10; ратификация методики — владелец |
| S05 | COMPLETE | Coverage переизмерен на интегрированном коде (6 сборок, TOTAL 64,77 % line / 52,51 % branch); регресс S05-1 (молчаливое выпадение сборки App из coverage) найден и закрыт; карта рисков непроверенных путей построена | docs/rc-2026-11-10/S05-coverage-risk.md §1, §2, §8 | Guard «число пакетов cobertura == 6» реализован скриптом, журнал: docs/rc-2026-11-10/evidence/runtime-s06b/coverage-guard.txt |
| S06 | COMPLETE | Дефект D-S06-1 закрыт пиксельным доказательством; R1/R3/R7 пересчитаны из полной матрицы; OVERALL=70,31 % ≥ 70; команды F5–F8 работают (живые цепочки + контракт хоткеев); регрессий нет | docs/rc-2026-11-10/S06-mainwindow.md §1, §3, §6 | Машинная LAYOUTCHECK драйверами S06 не выполнялась (зафиксировано в §6 документа) |
| S07 | COMPLETE | Функция №10 «Группировка строк» реализована: контигуальные блоки, сворачивание/разворачивание, снятие группировки с возвратом порядка, сохранение выделения, совместимость с поиском; 11 тестов, runtime-команда GROUP и живой прогон S9 | docs/rc-2026-11-10/S07-search-grouping.md §1, §3, §6; tests/Picket302.Tests/App/RowGroupingControllerTests.cs | Многоуровневая группировка и персистентность выбора колонки — зарегистрированные расширения (S07 §5), не ТЗ-минимум |
| S08 | COMPLETE | Легаси-опции таблиц реализованы; R7 честно пересчитан (87) после отката завышения ревью; OVERALL=70,31 %; «3D вид» задокументирован как не реализованный | docs/rc-2026-11-10/S08-secondary-dialogs.md §2, §3, §7 | R5/R6 — решения владельца (не закрыты документальной правкой) |
| S09 | COMPLETE | Реальный Windows DPI 125 % на хосте не выполнен (NOT_RUN_ON_HOST — смена масштаба инвазивна); симуляция пере-валидирована на фиксах S06–S08; дефект D-S0910-1 зарегистрирован до DPI-стенда | docs/rc-2026-11-10/S09-S10-dpi-matrix.md §S09-DPI, §«Итог этапов» | OS_DPI_125_VERIFIED=NOT_RUN_ON_HOST — нужен стенд владельца (реальные 125 %) |
| S10 | COMPLETE | Визуальная матрица пересчитана после S06–S08: OVERALL(R1–R8)=1125/16=70,31 %; DPI 150 % на хосте не выполнен (NOT_RUN_ON_HOST) | docs/rc-2026-11-10/S09-S10-dpi-matrix.md §«Пересчёт визуальной матрицы», §«Итог этапов» | OS_DPI_150_VERIFIED=NOT_RUN_ON_HOST; ратификация OVERALL-эрратума (70,31 %) — владелец |
| S11 | COMPLETE | Чистый COM baseline: реестр замерён до и после; холодная активация из документированного чистого состояния (hr=0, -Embedding, CoRegister S_OK, graceful revoke); postcondition очистки подтверждён | docs/rc-2026-11-10/S11-com-baseline.md §1, §2, §3, §4; docs/rc-2026-11-10/evidence/com-s11/ | — |
| S12 | COMPLETE | Симптом Stage4 (0x80040154) из чистого состояния не воспроизводится (6/6 циклов); категория B′ (загрязнение SCM после kill без CoRevoke) установлена вместо дефекта приложения | docs/rc-2026-11-10/S12-S13-com-diagnosis.md §S12 | Условие «при воспроизведённом симптоме» не наступило — ProcMon не потребовался |
| S13 | COMPLETE | Решение по исправлению принято: код-фикс не требуется (per-PID shutdown-событие и graceful CoRevokeClassObject уже реализованы и подтверждены 6 циклами); переход HKCU→HKLM не требуется | docs/rc-2026-11-10/S12-S13-com-diagnosis.md §S13 | Смена модели регистрации для машинной установки — решение владельца (S15) |
| S14 | COMPLETE | Приёмка 5× холодных активаций (hr=0) + UNC-цикл + dbguard (SafeLoadDatabase/PrintEnvelope guard без записи в production) выполнены; CoRevoke и очистка ключей доказаны во всех циклах | docs/rc-2026-11-10/S14-com-acceptance.md §«Приёмка 5×», §«UNC-путь», §«SafeLoadDatabase / PrintEnvelope»; docs/rc-2026-11-10/evidence/com-s14/ | COM_FULL_ACCEPTANCE_VERIFIED=PARTIAL (print happy-path NOT_RUN_EXTERNAL); находка D-S14-warncode открыта |
| S15 | COMPLETE | Поставочная COM-модель HKCU per-user проверена: register/unregister без прав администратора, 0 ключей и 0 процессов после; инструкции восстановления документированы | docs/rc-2026-11-10/S15-com-delivery.md §2, §3, §5; docs/rc-2026-11-10/evidence/com-s15/ | HKCU/HKLM — OWNER_DECISION_REQUIRED (машинная установка не реализована) |
| S16 | COMPLETE | Виртуальный печатный конвейер перепрогнан на RC: app-spooler→PDF 2 страницы и engine→PDF 1 страница (magic OK, SHA зафиксированы), спулер-задания 0/0; юнит-наборы предпросмотра/аудита 34/34 и BlankEditorTests 19/19 | docs/rc-2026-11-10/S16-virtual-print.md §1, §2, §3, §4; docs/rc-2026-11-10/evidence/print-s16/ | POST_INDEX_STAMP_VERIFIED=NOT_RUN_ON_HOST — нет лицензированного ZIPcode.ttf (блокировка доказана тестом) |
| S17 | COMPLETE | Сценарии «не печатают» доказаны (спулер 0→0 в каждом прогоне); продуктовый конвейер до спулера доказан (S16); guard отсутствия шрифта работает; дефекты D-S17-01 и D-S17-02 исправлены с регрессионными тестами | docs/rc-2026-11-10/S17-ui-spooler.md §1, §2, §3, §4; docs/rc-2026-11-10/evidence/runtime-s17/ | UI_TO_SPOOLER_VERIFIED=PARTIAL (happy-path BLOCKED_EXTERNAL: лицензия шрифта + интерактивный принтер); D-S17-03 открыт — нужен тихий режим окна |
| S18 | BLOCKED_EXTERNAL | Калибровочный лист ±1 мм на физическом стенде печати (проверка смещения по физическому отпечатку) | docs/rc-2026-11-10/S18-S20-print-wave.md §S18 | Стенд отсутствует: физических принтеров нет, прав админа на добавление портов нет; PHYSICAL_PRINT_VERIFIED=NOT_RUN |
| S19 | BLOCKED_EXTERNAL | Лоток конвертов и термопринтер на стенде (дуплекс, лоток, ориентация) | docs/rc-2026-11-10/S18-S20-print-wave.md §S19 | Оборудование отсутствует; покрыт только двухпроходный режим по чётности юнит-тестами; неподдерживаемый режим не считается пройденным |
| S20 | COMPLETE | Функция №45 «Смещение по паре принтер+формат»: UI-точка «Калибровка…» в диалоге печати, модель/репозиторий профиля, применение смещения в печати и предпросмотре, 4 теста (квантизация 0,1 мм, раздельность пар, обновление, отсутствие профиля) | docs/rc-2026-11-10/S18-S20-print-wave.md §S20; tests/Picket302.Tests/Printing/PrinterOffsetProfileTests.cs | Физическое подтверждение смещений ±1 мм — вместе со стендом владельца (S18) |
| S21 | COMPLETE | Функция №33 «Адресные строки: реверс порядка»: ApplyAddressOrder встроен в конвейер после применения территории (действует в предпросмотре и печати), пустые строки не создаются, содержимое не меняется; 5 тестов | docs/rc-2026-11-10/S21-S25-wave5.md §S21; tests/Picket302.Tests/Printing/ApplyAddressOrderTests.cs | — |
| S22 | COMPLETE | Функция №35 «Последние параметры печати одного конверта»: хранение/восстановление копий и принтера в диалоге печати с безопасным откатом повреждённых значений; 7 тестов | docs/rc-2026-11-10/S21-S25-wave5.md §S22; tests/Picket302.Tests/Application/PrintOptionsLastParametersTests.cs | — |
| S23 | COMPLETE | Функция №49 «Специальные бланки»: меню «Печать → Специальные бланки» (доверенность, заявление), подстановка реквизитов выбранного отправителя, отмена не создаёт заданий; 6/6 тестов сериализации | docs/rc-2026-11-10/S21-S25-wave5.md §S23 | — |
| S24 | COMPLETE | Функция №146 «Этикетки A5/A4/A3 + PDF»: экспортёр с раскладкой через SheetLayoutImposition, PrintToFile-PDF, отмена по токену на каждой странице; 3 теста (пустой список, отмена, честная сетка 1×2) | docs/rc-2026-11-10/S21-S25-wave5.md §S24; tests/Picket302.Tests/Printing/AddressLabelsPdfExporterTests.cs | — |
| S25 | COMPLETE | Функция №144 «Каталог бланков > 50»: 52 реальных различимых макета (конверты ГОСТ/ISO, праздничные открытки, офисные бланки A4); тест Count>50 + уникальность имён | docs/rc-2026-11-10/S21-S25-wave5.md §S25 | — |
| S26 | COMPLETE | Функция №158 «Клипарт в blank.xml»: модель, XSD, сериализатор (round-trip), рендер в предпросмотре и печати, выбор клипарта в редакторе бланков, миграция БД V008; 3 теста | docs/rc-2026-11-10/S26-clipart.md §1, §2, §3; tests/Picket302.Tests/Printing/ClipartInBlankTests.cs | Вставка произвольных пользовательских файлов (не из библиотеки) — вне функции 158 |
| S27 | COMPLETE | Функция №56 «Пользовательские отчёты»: менеджер и диалог в составе приложения, исполнение пользовательских query.xml поверх живой базы под SELECT-only guard (сканер обходов), изоляция ошибок, приёмка ADDUSERREPORT/USERREPORTS; 29 кейсов | docs/rc-2026-11-10/S27-user-reports.md §1–§5 и эрратумы §6–§7; docs/rc-2026-11-10/WAVE6-checkpoint.md §1 | D-S27-05 (лимит 50 строк на стороне исполнения) — в бэклог волны тестов |
| S28 | COMPLETE | Функция №58 «Промежуточный XML реестра»: дамп строк реестра ф.103 во временный XML до спулинга, чекбокс в меню, отказ дампа не прерывает печать; 9 тестов | docs/rc-2026-11-10/S28-report-data-dumper.md §1–§3 | Подключено к тихой печати реестра — единственный контур, строящий реестры из слоя данных |
| S29 | COMPLETE | Функция №66 «Мастер шаблонов документов: preview результата»: генерация из текста редактора через общий писатель Save, ошибка генерации не выдаётся за результат, IsDirty/TemplatePath не сбрасываются; 5 тестов + эрратум ревью S29 | docs/rc-2026-11-10/S29-doc-wizard-preview.md §1–§4 и эрратум §6 | OpenXML-ветка preview (описание пакета вместо содержимого) — задокументированное ограничение §4 |
| S30 | COMPLETE | Функция №67 «12 XML + 16 XSLT»: 16 поставляемых стилей WordprocessingML + каталог, инвентаризация 12 query-XML, компиляция/маркеры/рендер/DOCX без Office; 6 тестов | docs/rc-2026-11-10/S30-document-styles.md §1–§3 | Маркеры рассылок/отправителя появляются по мере готовности функций (тест 4 гарантирует отсутствие «мёртвых» ссылок) |
| S31 | COMPLETE | Функция №73 «Редактор настроек импорта CSV»: профиль round-trip через хранилище, сопоставление колонок, байты CP1251, разделители ;/tab, preview-ошибки столбцов; 7 тестов | docs/rc-2026-11-10/S31-csv-editor.md §1, §3; tests/Picket302.Tests/Infrastructure/CsvProfileStoreTests.cs | Кнопки «Сохранить/Загрузить профиль…» используют файловые диалоги — round-trip проверен через хранилище напрямую |
| S32 | COMPLETE | Функция №29 «Входящая корреспонденция»: журнал + миграция V009 + репозиторий, сканы в профиле не теряются при сбое, лимит скана 50 МБ до копирования, поиск; 6 тестов + эрратум ревью S32 | docs/rc-2026-11-10/S32-incoming-mail.md §1–§5 и эрратум §7 | Сужение объёма №29 (встроенный просмотр скана, отдельная таблица, форма входящих) — решение владельца |
| S33 | COMPLETE | Функция №30 «Движение документов»: приём→передача→архив, append-only контракт репозитория, текущее местонахождение, миграция V011 (уникальность приёма на уровне схемы), UI-диалог; 12 тестов, 8 находок ревью закрыты | docs/rc-2026-11-10/S33-document-movement.md §1–§5 и §6–§7; docs/rc-2026-11-10/WAVE7-checkpoint.md §1 | Передача/архив после чтения истории — без транзакции (риск принят ревьюером как низкий) |
| S34 | COMPLETE | Функция №110 «Трекер Почты России: UI»: диалог партии/ШПИ/даты/пункта, запрос статусов, три состояния (найдено / нет данных / ошибка) на тестовом источнике, фабрика с параметрами и DPAPI; 6 тестов | docs/rc-2026-11-10/S34-postal-tracker.md §1–§3 | Регистрация партии хранится в памяти диалога; реальный сервис pochta.ru не вызывается |
| S35 | COMPLETE | Функции №109 «Утилита удалённого доступа» (http-адрес отклоняется до загрузки/запуска) и №136 «Диагностика “сервер БД не запускается”» (команды пинятся, delete без подтверждения не выполняется, сырой вывод человеку); 5 тестов | docs/rc-2026-11-10/S35-support-diagnostics.md §1–§3 | Реальный запуск утилиты поддержки и сетевые проверки подменены |
| S36 | COMPLETE | Функция №94 «SSL-соединение SMTP»: TLS-стенд с 3 режимами (None/StartTls/ImplicitTls), доверие по отпечатку, отказы без утечки учётных данных, сниффер-доказательство «plaintext ≠ защищённая»; 5 тестов | docs/rc-2026-11-10/S36-smtp-tls.md §1–§3; tests/Picket302.Tests/Infrastructure/SmtpTlsStandTests.cs | PersistKeySet оставляет приватные ключи самоподписанных сертификатов в профиле — гигиенический остаток |
| S37 | COMPLETE | Функция №95 «SMTP-прокси»: стенд HTTP CONNECT — маршрут к SMTP-серверу, Basic-авторизация с DPAPI-расшифровкой, отказ 407/403, реальный таймаут клиента (ожидание ≥1,5 с); 4 теста + эрратум ревью S37 | docs/rc-2026-11-10/S37-smtp-proxy.md §1–§3 и эрратум §5; tests/Picket302.Tests/Infrastructure/SmtpProxyStandTests.cs | Стенд — один туннель; SOCKS не поддерживается (вне ТЗ) |
| S38 | COMPLETE | Функции №96 «Формат письма: текст/HTML» и №99 «Точный ReplyTo/отправитель»: захват полного DATA (text/plain quoted-printable с русским текстом и text/html base64), envelope MAIL FROM = отправитель, RCPT TO = точный адрес, Reply-To заголовком; 4 теста | docs/rc-2026-11-10/S38-smtp-mime.md §1–§4; tests/Picket302.Tests/Infrastructure/SmtpMimeTests.cs | Байтовая точность CRLF между строками тела — фасадная ответственность |
| S39 | COMPLETE | Функция №97 «WYSIWYG HTML-редактор»: команды форматирования, работа с шаблоном, санитизация на всех путях (Загрузить, ручное редактирование кода, предпросмотр, «Готово»), совпадение UI и MIME байт-в-байт; подтверждённый обход `<img src=x ONERROR=…>` закрыт allowlist-токенизатором; 33 теста редактора | docs/rc-2026-11-10/S39-html-editor.md §1–§3; src/Picket302.App/UI/Editors/HtmlMailEditor.cs; tests/Picket302.Tests/App/HtmlMailEditorTests.cs | Предпросмотр рендерится WebBrowser в режиме IE — контракт структурный, не пиксельный |
| S40 | COMPLETE | Функция №100 «Встроенный SMTP-клиент»: отчёт партии Total/Sent/Failed, журнал отправки (запись и получатель), отмена до отправки, частичный отказ RCPT 550 с персонализированной ошибкой; сверх того — retry-пин счётного стенда: один таймаут даёт ровно один повтор, отмена и постоянная ошибка повтора не дают, письмо доставляется ровно один раз | docs/rc-2026-11-10/S40-smtp-client.md §1–§2; tests/Picket302.Tests/Infrastructure/SmtpClientContractTests.cs; tests/Picket302.Tests/Infrastructure/SmtpRetryStandTests.cs | Очередь рассылок верхнего уровня (EmailDistributionService) — вне контракта фасада |
| S41 | OWNER_DECISION_REQUIRED | Подготовлен архитектурный документ по №127: текущая готовность, варианты A (SQLite-only) / B (PostgreSQL) / C (гибрид Pro), последствия каждого, формат ответа; DONE функции не присваивается | docs/rc-2026-11-10/S41-db-architecture.md §1–§6 | OWNER_DECISION=SQLITE_ONLY / POSTGRESQL / HYBRID_PRO (формат ответа — §7) — решение владельца, блокирует S42–S45 |
| S42 | NOT_STARTED/DEPENDENT_ON_OWNER_DECISION | Не выполнен: этап не имеет отдельного зафиксированного критерия в закоммиченных источниках; S42–S45 определены только как волны реализации выбранного варианта №127 (драйвер, миграции, сессия, блокировки) и не стартуют до решения владельца | docs/rc-2026-11-10/S41-db-architecture.md §3 (оценка объёма волн), §6 (формат решения; «S42–S45 не стартуют») | Решение владельца по №127 не получено — работы не начинались |
| S43 | NOT_STARTED/DEPENDENT_ON_OWNER_DECISION | Не выполнен: этап не имеет отдельного зафиксированного критерия в закоммиченных источниках; S42–S45 определены только как волны реализации выбранного варианта №127 (драйвер, миграции, сессия, блокировки) и не стартуют до решения владельца | docs/rc-2026-11-10/S41-db-architecture.md §3 (оценка объёма волн), §6 (формат решения; «S42–S45 не стартуют») | Решение владельца по №127 не получено — работы не начинались |
| S44 | NOT_STARTED/DEPENDENT_ON_OWNER_DECISION | Не выполнен: этап не имеет отдельного зафиксированного критерия в закоммиченных источниках; S42–S45 определены только как волны реализации выбранного варианта №127 (драйвер, миграции, сессия, блокировки) и не стартуют до решения владельца | docs/rc-2026-11-10/S41-db-architecture.md §3 (оценка объёма волн), §6 (формат решения; «S42–S45 не стартуют») | Решение владельца по №127 не получено — работы не начинались |
| S45 | NOT_STARTED/DEPENDENT_ON_OWNER_DECISION | Не выполнен: этап не имеет отдельного зафиксированного критерия в закоммиченных источниках; S42–S45 определены только как волны реализации выбранного варианта №127 (драйвер, миграции, сессия, блокировки) и не стартуют до решения владельца | docs/rc-2026-11-10/S41-db-architecture.md §3 (оценка объёма волн), §6 (формат решения; «S42–S45 не стартуют») | Решение владельца по №127 не получено — работы не начинались |
| S46 | COMPLETE | Функции №121 «Детекция ВМ» (мягкая детекция без исключений на хосте, source-pin «без WMI») и №126 «Редакции Free/Home/Professional» (различимые лимиты, надмножество фич, полный каталог, UI-гейты) закрыты тестами; строки реестра S01 переведены в CLOSED | tests/Picket302.Tests/Licensing/EditionsAndVmDetectorTests.cs; docs/rc-2026-11-10/evidence/runtime-s46/s46-debug.trx.gz (1561/0/0); docs/rc-2026-11-10/WAVE8-checkpoint.md §3; docs/rc-2026-11-10/S01-baseline.md §5 (строки 121 и 126) | Отдельного документа этапа S46 в репозитории нет — источники только чекпойнт, реестр и тесты; предупреждение «О программе» — визуальный слой, вне юнит-контракта |
| S47 | COMPLETE | Функция №163 «Обновления и сервисные формы» (часть целостности): SHA-256 из манифеста updates.txt, целый пакет ставится, повреждённый откатывается с удалением файла и записью в журнал, обратная совместимость манифеста без хеша; 4 теста. Прод-путь загрузки — HTTPS-only с обязательным хешем | docs/rc-2026-11-10/S47-update-package.md §1–§3; tests/Picket302.Tests/Infrastructure/UpdatePackageTests.cs; docs/rc-2026-11-10/evidence/runtime-s47/s47-debug.trx.gz (1565/0/0) | SHA-256 подтверждает целостность относительно манифеста, но не является криптографической подписью; ключ подписания и доверенный канал — решение владельца (функция №163 остаётся PARTIAL) |
| S48 | COMPLETE | Зафиксированы обоснования DEFERRED для №14 (probili.ru — сервиса в коде нет) и №125 (антиотладка — запрещена ТЗ §7.4, приёмки намеренно нет) и статус №163 (целостность ≠ подпись) | docs/rc-2026-11-10/S48-S49-deferred-and-install.md §1, §2, §3 | №14 — допустимость внешней интеграции решает владелец; №125 — DEFERRED по запрету ТЗ, не по нехватке ресурсов |
| S49 | NOT_RUN | Установка и первый запуск на чистой машине (ВМ-снапшот без .NET и без профиля пользователя): пустая адресная книга + демо-база, печать конверта C6 на виртуальном принтере, COM-активация, лицензия trial, журнал и выставление CLEAN_MACHINE_INSTALL_VERIFIED | docs/rc-2026-11-10/S48-S49-deferred-and-install.md §4 (процедура для владельца) | Изолированного чистого контура на станции нет; CLEAN_MACHINE_INSTALL_VERIFIED=NOT_RUN (компоненты установки покрыты тестами, но это не подмена чистой машины) |
| S50 | AWAITING_OWNER_ACCEPTANCE | Цикл работ и отчётности завершён исполнителем: реестр 50 этапов, счётчики 163 функций, протокол финального ревью кода, артефакты (TRX/сборка) с SHA-256 и перечень решений владельца сведены в проверочный пакет | docs/rc-2026-11-10/FINAL-REPORT-TO-OWNER.md; docs/rc-2026-11-10/S50-final-report.md | Принятие владельцем не получено — до принятия этап не считается COMPLETE |

Формулировка итога: **цикл RC-работ завершён исполнителем** (все
запланированные и не заблокированные этапы выполнены, отчётность
подготовлена); **фактически COMPLETE — 41 этап из 50**; 8 этапов
ограничены внешними контурами/решениями (см. таблицу) и не маскируются
под READY.

## 3. Функции (163, docs/ACCEPTANCE.md)

**DONE 156 / PARTIAL 5 / DEFERRED 2 = 163** (построчный парсер строк
функций с приоритетом [П]/[Д]/[К]; идентификаторы 1–163 ровно по одному
разу; №10 «Группировка строк» → DONE по результату сверки контракта —
см. §3.1; шесть строк, отставших от кода волны 5, сверены с кодом и тестами по
замечанию независимого ревью DOC-01 — пять из них DONE, №158 остаётся PARTIAL
по DOC-03 — см. §3.2 и §3.3). Разбивка по приоритетам:
[К] 64 DONE + 1 PARTIAL (№127); [П] 74 DONE + 2 PARTIAL (№110, №158);
[Д] 18 DONE + 2 PARTIAL (№121, №163) + 2 DEFERRED (№14, №125).

### 3.1. Реконсиляция №10 (группировка строк)

Контракт 00_FUNCTION_INDEX: «Группировка строк в списке, настройка и
порядок группировки, снятие группировки» [П]. Реализация
(`RowGroupingController.cs` + MainShell): выбор колонки группировки
(контекстное меню «Группировка строк»), непрерывные блоки по первому
вхождению (порядок группировки), сворачивание/разворачивание кликом по
заголовку «− Ключ (N)», снятие («Без группировки» возвращает исходный
порядок), сохранение выделения (теги записей) и совместимость с поиском
(разворот скрытой строки — ревью-фикс F1). Тесты:
`RowGroupingControllerTests` (11, порядок-независимые); runtime: команда
GROUP + кадр S9 (S07-док §2/§6); ревью S07 закрыто PASS (R5). Одноуровневая
группировка = паритет с легаси-референсом; многоуровневая и персистентность
выбора колонки — зарегистрированные расширения (S07 §5), ТЗ-минимум не
требует. **Вывод: DONE** (прошлый DEFERRED описывал состояние до S07).

Ключевые честные PARTIAL (это все пять): №163 (SHA-256 = целостность, НЕ
подпись; ключ и канал — владелец; UI-путь установки закрыт), №110 (реальный
pochta.ru не вызывается), №121 (нет предупреждения в «О программе»), №127
(OWNER_DECISION), №158 (не закрыт «выбор цвета» по ТЗ §1.15.7 — §3.3).

### 3.2. Реконсиляция шести строк реестра функций (замечание DOC-01)

Независимое документальное ревью (R5, REVIEWED_SHA `c3547cd`) обнаружило, что
шесть строк реестра `docs/ACCEPTANCE.md` остались со статусом PARTIAL и с
довоенной формулировкой, опровергнутой кодом и тестами волны 5. Агрегаты
151/10/2 (состояние реестра до исправления; источник — протокол R5, находка
DOC-01) были внутренне согласованы, но противоречили реестру этапов
(S20/S21/S23/S24/S25/S26 = COMPLETE) и самим волновым документам («ЗАКРЫТ»).

Сверка по первичным источникам (код + тесты + волновые документы):

| № | Что было написано в реестре | Что подтверждают первичные источники | Итог |
|---|---|---|---|
| 33 | «реверс порядка переносится в контекст, но не применяется; тестов нет» | `PrintSessionController.ApplyAddressOrder` в конвейере после `ПрименитьТерриторию`; `ApplyAddressOrderTests` (5); S21 — «ЗАКРЫТ» (`04e91a3`) | DONE (`04e91a3`) |
| 45 | «прямого теста нет» | `FormPrinterCalibration` (UI «Калибровка…»), `ПолучитьСмещениеПринтера` в печати, смещение в `PrintPreviewEngine`; `PrinterOffsetProfileTests` (4); S20 — «ЗАКРЫТ» (`e03b43e`) | DONE (`e03b43e`) |
| 49 | «подстановка реквизитов контрагента и меню … не реализованы» | меню «Печать → Специальные бланки» в `MainShell.cs`, подстановка реквизитов через `onBeforePrint`, обе формы в каталоге (`LbkPackageTests`); S23 — «ЗАКРЫТ» (`b428cb9`) | DONE (`b428cb9`) |
| 144 | «28 бланков; порог теста занижен до 20; праздничных нет» | 52 реальных макета (ГОСТ/ISO, праздничные открытки, офисные A4); `BuiltinTemplatesTests` — `Assert.True(библиотека.Count > 50)` + уникальность имён; S25 — «ЗАКРЫТ» (`349198a`) | DONE (`349198a`) |
| 146 | «сохранение макета в PDF полностью отсутствует» | `AddressLabelsPdfExporter` (A5/A4/A3, PrintToFile-PDF, отмена по токену); `AddressLabelsPdfExporterTests` (3); S24 — «ЗАКРЫТ» (`02343e8`) | DONE (`02343e8`) |
| 158 | «вставка клипарта в произвольный макет blank.xml не реализована» | `ClipartId` в модели/XSD/сериализаторе, рендер в preview и печати, выбор в редакторе, миграция V008; `ClipartInBlankTests` (3); S26 — «закрыт полностью» (`7376f64`+`8f05cbd`+`aac1ae0`) | **PARTIAL** — клипарт закрыт, но не закрыта составляющая «выбор цвета» (ТЗ §1.15.7, `TFRMCOLOR`/`TWPOPUPCOLOR`): в `BlankFieldSpec` и `BlankSchema.xsd` цвета нет, `ColorRgb` есть только в БД-сущности `BlankField` (§3.3) |

Правило ACCEPTANCE («реализовано в коде И покрыто конкретным тестом → DONE»)
выполняется для **пяти** из шести: №158 возвращена в PARTIAL по замечанию
DOC-03 (см. §3.3). **Код не менялся** — менялась только честность реестра
(и его сводка, §3, §2 S50). Итоги пересчитаны: **DONE 156 / PARTIAL 5 /
DEFERRED 2 = 163**; оставшиеся PARTIAL — №110, №121, №127, **№158**, №163;
DEFERRED — №14, №125.

### 3.3. Возврат №158 в PARTIAL (замечание DOC-03)

Повторное независимое ревью (R6, REVIEWED_SHA `4b146f9`) вернуло
CHANGES_REQUIRED с одной находкой: функция №158 «Шаблоны, клипарт, шрифты и
цвета: каталог, редактирование, библиотека картинок, выбор шрифта и цвета»
(`00_FUNCTION_INDEX.txt:200`; ТЗ §1.15.7) была помечена DONE, но именованная
составляющая **«выбор цвета» не реализована**.

Проверка по первичным источникам:
- в `BlankFieldSpec` (`src/Picket302.Domain/Serialization/BlankModel.cs`) нет
  свойства цвета; «выбор шрифта» покрыт (`FontName`, `FontSizePt`, `Bold`,
  `Italic`);
- в `BlankSchema.xsd` нет атрибута цвета;
- `ColorRgb` существует только в слое БД-сущности
  `src/Picket302.Domain/Entities/BlankField.cs` (+ `BlankFieldRepository`,
  `FixA4Mode`) — другой слой, в редакторе бланков не редактируется;
- `ColorDialog` в приложении есть только в HTML-редакторе писем и в настройках
  вида таблиц, но не в редакторе бланков;
- инспектор свойств привязан к `CanvasController.GetSelected()` →
  `BlankFieldSpec?` — цвета в модели нет.

По правилу ACCEPTANCE («реализация без названной в тексте функции составляющей →
PARTIAL») №158 возвращена в PARTIAL с явным остатком; клипарт-остаток при
этом остаётся закрытым (S26). Пересчёт: **DONE 156 / PARTIAL 5 / DEFERRED 2 =
163**; [П] 74 DONE + 2 PARTIAL (№110, №158). Реализация выбора цвета в
редакторе бланков — решение владельца (отдельный этап/задача); до него функция
не может быть DONE.

## 4. Тесты и артефакты (историческое свидетельство vs свежий прогон)

**Сохранённые TRX на 09e152b** (прочитаны как данные, без запуска тестов):

| Файл | total | passed | failed | notExecuted | SHA-256 |
|---|---|---|---|---|---|
| evidence/runtime-final4/final4-debug.trx.gz | 1590 | 1590 | 0 | 0 | `86a9c011d02c44b5f5c332faaed192de8b7d25e0c88af39d48c1536d1d77e099` |
| evidence/runtime-final4/final4-release.trx.gz | 1590 | 1590 | 0 | 0 | `b9517ba9085e89037b93ec97ca6841faeba369aacdd1e34fcecd7726bf8d8517` |

Строгая команда (обе конфигурации): `dotnet test tests/Picket302.Tests/Picket302.Tests.csproj -c {Debug|Release} --no-build --filter "FullyQualifiedName!~Acceptance&FullyQualifiedName!~ComAutomation&FullyQualifiedName!~Printing.PrintEngineTests"`.

Граница точности: **свежего прогона тестов на итоговом HEAD в этом проходе
не выполнялось** — 1590/1590/0 являются историческим свидетельством на
09e152b; код-слой идентичен (документальные коммиты после 09e152b не
меняют исходники), поэтому артефакты остаются валидными для кода итогового
HEAD, но это не «лично перезапущено на HEAD».

## 5. Сборка

**Свежий прогон реконсиляции** (изоляция: bin/obj рабочего дерева; профиль/
реестр/принтер/сеть не затрагиваются): `dotnet build Picket302.sln -c {Debug|Release} -v m`
→ **exit 0 в обеих конфигурациях; предупреждения компилятора 0/0**
(grep -cE " warning " по полным логам). Артефакты:
evidence/build-reconcile/{BUILD-PROVENANCE.md, build-debug.log.gz
(SHA-256 `b49808007c2c890deb6c462fe459c033066732f19f7feefa73d9b7c77fb0625e`),
build-release.log.gz (SHA-256
`c0db7443a0eca9aff70494ba0622ffa03b48bdfc3902664a67fd51f3345b92b5`)}.
Исторические заявления R1–R4 «0 warnings» были подкреплены только commit
messages — настоящий прогон закрывает этот пробел независимо.

## 6. Финальное ревью Qwen — четыре раунда (R1–R4)

| Раунд | SHA | Объём просмотра | Вердикт | Findings → закрытие |
|---|---|---|---|---|
| R1 | c4f9a7d | полный дифф main…c4f9a7d | CHANGES_REQUIRED | FINAL-01 (P1) + 2×P2 + 7×P3 → 798a2f7 |
| R2 | 798a2f7 | полный дифф (повторная верификация закрытий R1 + охота на новые обходы санитизатора) | CHANGES_REQUIRED | FINAL2-01 (P1) + P3 → 1e62d41 |
| R3 | 1e62d41 | дельта 798a2f7…1e62d41 + контекст полного диффа (сам ревьюер отметил: построчная проверка R1-объёма не повторялась) | CHANGES_REQUIRED | FINAL3-02 (P1) + P2 + P3 → 09e152b |
| **R4** | **09e152b** | дельта 1e62d41…09e152b + целенаправленная охота на обходы style-гейта (calc/var/&#38;/;/OWASP-CSS) | **PASS** | Findings: none |

Первичный протокол R4 сохранён дословно (7978 байт, копия output.txt
ревью-агента): **docs/rc-2026-11-10/reviews/FINAL-R4-verdict.md**,
SHA-256 `f79fb5fddcd7a45fb23dca76a9ba8cf09d3f4864f98eb8720340609a83187943`;
содержит REVIEWED_SHA 09e152b, BASE_SHA 5af9874, VERDICT: PASS.
Граница: **PASS относится к коду на 09e152b**; документальные коммиты
после него (итоговый отчёт, реконсиляция) не входили в R4 и требуют
самостоятельной проверки владельца/повторного ревью.

### 6.1. Документальный прогон ревью (R5)

Объём: **документальная дельта** — реестр 50 этапов по одной строке на этап,
согласованность чисел, проверяемость указанных источников, неизменность кода
и честность границ доказательств. Код не входил в предмет проверки: после
09e152b исходники не менялись, что проверяется командой
`git diff --name-only 09e152b..HEAD` (в списке — только `docs/`).

**R5** (REVIEWED_SHA = `c3547cd8acbc16d3d48afe4d0756a6b8dc4f3c61`) — **VERDICT: CHANGES_REQUIRED**: DOC-01 (P1) —
шесть строк реестра функций отставали от кода волны 5; DOC-02 (P3) —
исторический блок S8 в ACCEPTANCE.md без пометки. Первичный протокол:
`reviews/FINAL-DOC-REVIEW.md`.

Обе находки закрыты в этом коммите: DOC-01 — §3.2 (пересчёт на этом шаге —
157/4/2, промежуточный; после DOC-03 — 156/5/2), DOC-02 — историческая
пометка в ACCEPTANCE.md. Код, тесты, конфигурации сборки
и бинарные артефакты не менялись.

**R6** (REVIEWED_SHA = `4b146f9ec47c62be24329e2be71cd6869f34cf68`) — **VERDICT: CHANGES_REQUIRED**, одна находка:
DOC-03 (P2) — №158 не может быть DONE, пока не закрыта составляющая «выбор
цвета» по ТЗ §1.15.7. Первичный протокол: `reviews/FINAL-DOC-REVIEW-R6.md`.
DOC-01 и DOC-02 подтверждены закрытыми.

Находка закрыта в этом коммите: №158 возвращена в PARTIAL с явным остатком
(§3.3), пересчитаны итоги функций (156/5/2). Код, тесты, конфигурации сборки и
бинарные артефакты не менялись.

**R7** (REVIEWED_SHA = `fda34bfaabbf89ef5028f6618b7083a5f3dc1c1a`) — **VERDICT: CHANGES_REQUIRED**, findings: см. §10. Первичный
протокол: `reviews/FINAL-DOC-REVIEW-R7.md`.

Граница: R7 проверял **документальную дельту** `4b146f9ec47c62be24329e2be71cd6869f34cf68..fda34bfaabbf89ef5028f6618b7083a5f3dc1c1a`; код (09e152b) не
входил в предмет проверки. Коммит с самим протоколом — документальный: он не
меняет ни кода, ни чисел реестра, ни формулировок §2–§3; отдельным read-only
прогоном R8 на финальном HEAD проверено, что дельта состоит только из этого
протокола и указателей в §6.1/§10.

## 7. РЕШЕНИЯ ВЛАДЕЛЬЦА (не закрыты документальной правкой)

1. **№127**: `OWNER_DECISION=SQLITE_ONLY | POSTGRESQL | HYBRID_PRO` (`S41-db-architecture.md`); блокирует S42–S45.
2. **№29** сужение объёма по ТЗ.
3. **№163** ключ подписи пакетов + доверенный канал.
4. **№14** probili.ru (DEFERRED — допустимость внешней интеграции).
5. Ратификация OVERALL-эрратума (70,31%) — методика/эрратум не ратифицированы владельцем.
6. HKCU/HKLM COM-поставки (S15).
7. Контуры: физический печатный стенд (S18/S19), чистая машина (S49), DPI 125/150 (NOT_RUN_ON_HOST).

## 8. Ограничения без повышения статуса

COM_FULL_ACCEPTANCE=PARTIAL · UI_TO_SPOOLER=PARTIAL · PRINT_PHYSICAL=NOT_RUN ·
DPI_125/150=NOT_RUN_ON_HOST · CLEAN_MACHINE=NOT_RUN · №127/S41=OWNER_DECISION
· №163=PARTIAL (целостность ≠ подпись) · визуальная 70,31% = не ратифицировано.

## 9. Список коммитов

Полный и авторитетный перечень — команда `git log --oneline main..HEAD`.
Ниже — снимок перечня на `d21ff23`
(103 коммита от main); он не включает завершающие документальные коммиты
этого прохода.

Строки ниже — дословные subject-ы коммитов (`git log --oneline`). Числа
внутри subject-ов (например, счётчики строгих прогонов на момент того
коммита) — **исторические** и не являются финальными результатами RC;
финальные значения — §2, §3 и §4.

```
d21ff23 docs(rc): reconcile acceptance ledger and final evidence
c4686d3 docs(rc): FINAL-REPORT-TO-OWNER - R4 verdict PASS embedded (Findings: none), verification package complete
09e152b fix(security): final review R3 findings FINAL3-01..03 closed
1e62d41 fix(security): final review R2 findings FINAL2-01/02 closed
798a2f7 fix(security): final review findings FINAL-01..10 closed
c4f9a7d chore(rc): zero-warning builds + final strict runs (Debug=Release=1582/0/0), git diff --check clean
d408c41 chore(rc): strip trailing whitespace in committed evidence files (journals/logs - semantic content unchanged)
68acd39 docs(rc): S50 rewritten with honest accounting (correction pass)
117dfa3 docs(acceptance): sync 18 rows with actual RC state; summary DONE 149 / PARTIAL 10 / DEFERRED 4 (163 total)
cc9d40c fix(security): correction pass items 2-3 - update package trust gates (HTTPS-only, mandatory hash), SMTP retry pin with counting stand, no-duplicate guard after body
9b2385d fix(security): S39 sanitizer rewritten as allowlist tokenizer (confirmed ONERROR bypass closed)
93057c9 docs(rc): F-CP-01 completion - flags block committed to S01 §6 (left unstaged in 3e157ab); S37 §2 403-test row (D-S37-03 closure row)
c282825 docs(rc): S50 final report - 50-stage cycle summary, statuses, flags, owner decisions, limitations
3e157ab fix(rc): S39-R2 findings F-S39R2-01..04 + S40-R2 trace note + checkpoints F-CP-01..04
5cde160 docs(rc): S48/S49 - deferred rationale (14 probili, 125 anti-debug), forms coverage note, clean-machine procedure (NOT_RUN stays, owner instructions)
764a458 feat(rc): S47 update package integrity (function 163 download+hash, CLOSED), +4 tests
7da7048 docs(rc): Wave 8 checkpoint (S36-S40) - SMTP TLS/proxy/MIME/editor/client + S41 owner-decision doc, flags, review ledger
3e661f9 docs(rc): S01 registry rows 121/126 CLOSED (S46)
9c84d26 fix(rc): S39 review findings D-S39-01..04 + S40 doc fixes, +6 S46 tests
df31b7f docs(rc): S41 DB architecture decision document (function 127, owner decision)
31d8e70 feat(rc): S40 SMTP client contract tests (function 100 CLOSED) + S38 review fixes, +3 tests
17a7da7 feat(rc): S39 WYSIWYG editor contract tests (function 97 CLOSED), +13 tests
d72cc84 fix(rc): S37 review findings D-S37-01..04 - silent proxy holds connection open (real timeout), 403 branch test, dead code removed, doc errata
3ee5931 feat(rc): S38 SMTP MIME and envelope tests (96+99 CLOSED), +4 tests
6dee3a7 feat(rc): S37 SMTP proxy stand (function 95 CLOSED) + S36 review fixes, +4 tests
30cbadd feat(rc): S36 SMTP TLS stand (function 94 CLOSED) + S33R2/S34 doc-test fixes, +5 tests
0824c60 docs(rc): Wave 7 checkpoint (S31-S35) - five functions CLOSED, review pipeline results, flags, owner questions
6818d68 feat(rc): S35 support launcher and DB service doctor tests (109+136 CLOSED), +5 tests
cd96c5e feat(rc): S34+S33fix - postal tracker UI (110 CLOSED) and S33 review findings D-S33-01..08
30830b0 feat(rc): S34 postal tracker UI (function 110) - batch registration dialog, status query with found/no-data/error states, +6 tests
defb0af docs(rc): S01 registry row 30 CLOSED (S33)
91ad656 feat(rc): S33 document movement tracker (function 30) + incoming mail UI, +12 tests
b24b047 fix(rc): S32 review findings D-S32-01..06 - duplicate number translated to InvalidOperationException in repository, 50MB scan limit, date convention, doc errata
e676e8e feat(rc): S32 incoming mail journal (function 29) - IncomingMail entity, V009 migration, repository, scan-in-profile service, +7 tests
441670a feat(rc): S31 CSV import editor tests (function 73) + S27R3-01 pin tests, +9 tests
2f3b20d fix(rc): S27-R2 findings - WITH+DML guard bypass closed (S27R2-01 P1), form isolation hardening, docs errata
ce89efd fix(rc): S29 review findings D-S29-01..04 - TemplatePath assert, preview filter extended, comment restored, dialog in accessibility contract
ba96777 docs(rc): Wave 6 checkpoint (S27-S30) - four functions CLOSED in S01 registry, flags consolidated, background reviews tracked, open items carried
9b756da feat(rc): S30 shipped document styles (function 67) - 16 XSLT templates + catalog, inventory validation of 12 query XMLs, +6 tests
b160c66 chore(rc): strict trx after S27-fix (Debug+Release)
8598ae7 fix(rc): S27 review findings D-S27-01..09 - async void isolation, int.Parse root fix, SELECT-only guard, IO filters, limit test, docs errata
2435ba7 feat(rc): S29 document wizard result preview (function 66) - render from editor text via save pipeline, error isolation, +5 tests
3e9213f docs(rc): Wave3 R2 findings closed - errata on S14 (CoRevoke marker provenance, warncode hypothesis withdrawn per code structure, 700ms claim removed, psenc cross-refs, resume point); verdict archived
2641653 feat(rc): S28 report data dumper wiring (function 58) - dump registry rows to temp XML before spooling, menu checkbox, +9 tests
1a36917 docs(rc): S27 stage summary and evidence (trx.gz x2, acceptance logs, user dir listing)
1bc1701 feat(rc): S27 user reports UI (function 56) - CustomReportManager wired into AppServices, FormUserReports dialog, RunDefinitionAsync on live SQLite
cc4af80 docs(rc): S26 clipart remainder summary (model/XSD/serializer/renderer/editor/DB)
fd695b0 chore(rc): fresh Debug strict trx after full Debug sln build (1424/0/0)
b70e28f chore(rc): refresh Debug trx evidence after full Debug sln build (both configs green 1424/0/0)
b601099 chore(rc): rebuild Debug Infrastructure (embedded V008 description) and refresh Debug trx evidence
aac1ae0 feat(rc): S26 wiring complete - BlankFieldRepository clipart_id SQL, V008 description header, clipart rendering in preview/print via resolver; strict Debug+Release green
5218186 chore(rc): add description header to V008 migration (MigrationTests requires non-empty description)
8f05cbd feat(rc): S26 DB persistence for ClipartId - V008 migration + repository SQL (SELECT */INSERT/UPDATE), fixes MigrationTests expectations
7376f64 feat(rc): S26 close function 158 remainder - ClipartId in blank model/XSD/serializer/renderer, clipart picker in blank editor, resolver wiring, +3 round-trip tests
eea71e6 docs(rc): Wave-5 summary S21-S25 (functions 33/35/49/146/144)
349198a feat(rc): S25 close function 144 - catalog expanded to 52 real layouts (envelope formats GOST/ISO, holiday cards, office forms); library test >50 + distinct names
a83738b chore(rc): fix remaining CS8602 in S24 tests
9f8e30a chore(rc): fix remaining CS8602 in S24 tests
efdb950 chore(rc): fix CS8604/CS8602 nullability warnings in S24 tests
02343e8 feat(rc): S24 close function 146 - AddressLabelsPdfExporter (A5/A4/A3 sheets, PrintToFile PDF, cancellation) + 3 tests (empty/Cancelled/imposition grid)
010610a chore(rc): S23 strict trx evidence (1418/0/0)
b428cb9 feat(rc): S23 close function 49 - special blanks menu (power of attorney + creditor application), sender requisites substitution via onBeforePrint, editor preview
a28b8f4 feat(rc): S22 close function 35 - last single-envelope print parameters (copies+printer persisted, restore on dialog open, corrupted-safe defaults, +7 tests); strict 1413/0/0
04e91a3 feat(rc): S21 close function 33 - ApplyAddressOrder reverse for AddressOrderSmallToLarge formats (print+preview pipeline, +5 tests); strict 1411/0/0
8f24159 docs(rc): S18/S19 BLOCKED_EXTERNAL (no physical stand), S20 closed (function 45 calibration UI+tests)
e03b43e feat(rc): S20 close function 45 - printer calibration dialog (0.1mm step, per-pair isolation), AppServices wiring, print-dialog entry, +4 tests; strict 1406/0/0
243333f docs(rc): S17 UI->spooler honest status (PARTIAL: cancel scenarios proven, happy-path BLOCKED_EXTERNAL on font license; D-S17-01/02 fixed, D-S17-03 open quiet-window)
63bb970 fix(rc): S17 findings - D-S17-01 FixA4 refreshes printer info (+3 tests), D-S17-02 ZIPcode font-block routed to single message (no paper-dialog loop), PRINTTOFILE/SEEDTEMPLATEPRINT acceptance hooks, printconfirm driver; strict 1402/0/0
8e474fa feat(rc): S16 virtual print pipeline re-verified on RC (app-spooler PDF 2pp + engine PDF, ZIPcode blocking test PASS, spooler 0/0)
5533bb2 docs(rc): Wave-3 checkpoint PASSED (S11-S15 COM); consolidate Wave-3 flags into S01 registry (F-W3-4)
d2693de feat(rc): S15 delivery COM model - HKCU per-user register/unregister clean (0 keys/0 procs, no admin), recovery instructions, HKLM owner decision
b78c97b feat(rc): S14 SafeLoadDatabase/PrintEnvelope COM guard proof (no production write); D-S14-warncode finding; print happy-path NOT_RUN_EXTERNAL (interactive virtual printer)
416e998 feat(rc): S14 UNC cold cycle PASS (hr=0 125ms PID 90632, share cleaned); close COM-review P3 F-W3-1/2/3 (retarget logenc->PS BOM, baseline wording, instrumental labels)
b1847c7 feat(rc): S12/S13 COM diagnosis (Stage4 symptom not reproducible, B' SCM pollution, no code fix) + S14 5-cycle cold acceptance PASS (UNC + PrintEnvelope deferred)
881f077 feat(rc): S11 clean COM baseline + cold activation PASS on RC (hr=0, PID 93176, -Embedding, CoRegister S_OK, graceful revoke, full cleanup)
46794c1 docs(rc): Wave-2 checkpoint PASSED (S06-S10 all re-review PASS); close F-R5 nits, ACCEPTANCE summary erratum (S01 Q1-3 promise)
ccc0f36 fix(rc): S07 r5 - remove leftover Font allocation (F-R4-1), discriminating intermediate asserts (F-R4-2), honest bounded-leak wording + run row (F-R4-3)
36659f4 fix(rc): S07 r4 - discriminating reload fixture 10/11/12 alternating (F-R3-1), font cache follows grid Font change (F-R3-2), style font shares cache (F-R3-4), doc run rows (F-R3-3)
8489432 docs(rc): S06-fix R3 - true-under-erratum flag qualification everywhere (N1), factual LAYOUTCHECK inventory (N2), width-assert wording (N3), guard journal with artifact SHAs (N4), ratification carry-over
9c6cf72 fix(rc): S07 r3 - cached bold font in paint (F-R2-1), order-snapshot reset on reload (F-R2-2), discriminating assert (F-R2-3), per-run coverage labels + full SHA (F-R2-4), symmetrical unsubscribe (F-R2-5)
8d69fa2 docs(rc): close S06 review F1-F5 (honest LAYOUTCHECK wording, flag erratum to S02 pending owner ratification, forensics invariants, red-test attribution, guard journal)
4b42705 fix(rc): close S07 review F1-F8 (tag-based search navigation under grouping, fail-closed S9, spanning group headers, order restore, dispose, guard journal)
d00a722 docs(rc): S09/S10 D2/D4 phrasing - cards are dialogs, main window is D1/D3/D5 (P3 nit)
6031f6b docs(rc): S08 §7 stale 91/70.81 -> 87/70.31 (F-R3-1)
0d9ef6c docs(rc): S09/S10 round-2 - honest binary provenance (F-05), bracket rephrased (F-08), 70.31 in summary (F-06), checkpoint carry-over items
2ea28ca docs(rc): purge refuted 3D-view equivalence from doc and source comment (F-NEW-1), sync S01 OVERALL registry line (F-NEW-2)
4dd6269 fix(rc): close S08 review F2/F5/F6 (honest rescore R7=87 OVERALL 70.31, font dispose) and S09/S10 F-01..F-04 (defect D-S0910-1 registered, honest DPI labels, hashes, flag name)
9c433ad fix(rc): S08 review P2 - dialog height fits 13 rows (label was clipped), legacy field order, no-BOM, extended reset test
ee014ed docs(rc): S09/S10 real DPI NOT_RUN_ON_HOST (owner stand required), DPI simulation revalidated on fixes, matrix 70.81%
f063cef feat(rc): S08 legacy table-dialog options (header height, bold header, frozen columns); R7=91, OVERALL 70.81%
9cffaa7 feat(rc): S07 row grouping function 10 (collapsible group blocks, context menu, GROUP harness cmd) + 7 tests
e047147 chore(rc): drop 20MB cobertura blobs from evidence (hash-referenced per convention), commit gzipped trx proofs, ignore cobertura in evidence
282fe54 feat(rc): S06 close D-S06-1 (reset button width + legacy dialog title), fix blank-editor capture target, coverage guard; OVERALL 70.31%
9190336 docs(rc): close S03-fix P3 findings; Wave-1 checkpoint report (all 5 stages re-review PASS)
00660db docs(rc): close S05-fix P3 findings Q5-F1..F3 (64.77 total, ВыполнитьПечатьAsync, SMTP coverage clarification)
12669f0 docs(rc): close S04-fix P3 observations N1-N3 (inline pair frame, score sensitivity wording, registry OVERALL line)
532be08 docs(rc): close Qwen findings on S03/S04/S05 (S04 arithmetic 69.9%, R7 rescore, D-S06-1 defect; S03 manifest vs journals; S05 74.00/280/SMTP)
fd7cb0d docs(rc): close Qwen findings Q1-1..Q1-4, Q2-1..Q2-3 (spooler flag, 1374 attribution, flag mapping, erratum)
46b81b0 test(rc): S05 restore App coverage instrumentation (PreserveCompilationContext) + coverage risk map
5aaba48 docs(rc): S04 visual score R1-R8 = 76.6% on corrected base (R9 NOT_SCORED)
81c8419 docs(rc): S03 fresh UI capture via non-activating harness (45 PNG, 11 drivers PASS)
1f7d195 docs(rc): S02 KonvertProNet reference inventory, R9 photo excluded from scoring
e71fbbe docs(rc): S01 baseline actualization and open-items registry
```
(снимок на d21ff23: 103 коммита от main; далее — документальные коммиты
этого прохода: реестр 50 этапов, правка по DOC-01 и протоколы независимого
ревью; авторитетный счёт — `git rev-list --count main..HEAD`)
```

## 10. Независимое документальное ревью (R5) — протокол и вердикт

Ревью выполняется **на коммите, содержащем настоящий документ**, в режиме
read-only, с проверкой на противоречия: 50 индивидуальных строк реестра,
согласованность чисел, существование указанных первичных источников,
неизменность кода и честность границ доказательств.

**R5** — REVIEWED_SHA `c3547cd8acbc16d3d48afe4d0756a6b8dc4f3c61`, **VERDICT: CHANGES_REQUIRED**. Findings: DOC-01
(P1), DOC-02 (P3). Первичный протокол (дословный ответ ревьюера, без
переписывания): `docs/rc-2026-11-10/reviews/FINAL-DOC-REVIEW.md`
(17866 байт, SHA-256 `c5299a092c5a21f664edbdf5880e62e9d72dd384c8191a379448a5ce907d855d`;
SHA-256 и размер самого текста отзыва — в шапке файла). Обе находки закрыты в этом коммите — DOC-01 §3.2,
DOC-02 — пометка исторического блока в ACCEPTANCE.md.

**R6** — REVIEWED_SHA `4b146f9ec47c62be24329e2be71cd6869f34cf68`, **VERDICT: CHANGES_REQUIRED**. Findings: DOC-03
(P2). Первичный протокол (дословный ответ ревьюера, без переписывания):
`docs/rc-2026-11-10/reviews/FINAL-DOC-REVIEW-R6.md` (19553 байт, SHA-256 `ba77b2007ede84c31b311fc1a9d759b353a35d653d969ee91216a084c6e40a84`; SHA-256 и размер самого текста отзыва — 18848 байт / `8412980ad59fba9d032f2408a5dc20f68b919f2f6196dba42b43084b2ada0489` — в шапке файла; правка по замечанию независимого ревью DOC-04).
DOC-01 и DOC-02 подтверждены закрытыми; DOC-03 закрыт в этом коммите — №158
возвращена в PARTIAL с явным остатком (§3.3), пересчитаны итоги функций.

**R7** — REVIEWED_SHA `fda34bfaabbf89ef5028f6618b7083a5f3dc1c1a`, **VERDICT: CHANGES_REQUIRED**, findings: см. протокол. Первичный протокол
(дословный ответ ревьюера, без переписывания):
`docs/rc-2026-11-10/reviews/FINAL-DOC-REVIEW-R7.md` (15594 байт, SHA-256 `1ea66d6549c21a6e3b38dbcabd72189a98ec879ae4b9a65af0fa19bd763e233d`;
SHA-256 и размер самого текста отзыва — 14837 байт / `c6e3482cd3e1c371f2b562b1040a98eb52ac68bcfd13a84205caa0c60660af1e` — в шапке файла).
Предмет: закрытие DOC-03 (подтверждено) и DOC-04 (метаданные протокола R6 —
исправлены в этом коммите) и отсутствие новых противоречий; код не запускался.

**Итог цепочки независимого документального ревью:** R5 (c3547cd) —
CHANGES_REQUIRED (DOC-01, DOC-02) → закрыто в 4b146f9; R6 (4b146f9ec47c62be24329e2be71cd6869f34cf68) —
CHANGES_REQUIRED (DOC-03) → закрыто в 2911030; R7 (fda34bfaabbf89ef5028f6618b7083a5f3dc1c1a) — **CHANGES_REQUIRED**. Все находки
закрыты документальными правками; код, тесты, конфигурации сборки и бинарные
артефакты не менялись ни в одном раунде (`git diff --name-only
09e152b..HEAD` содержит только `docs/`).

Финальный HEAD ветки содержит один коммит сверх REVIEWED_SHA R7 — этот
протокол и указатели в §6.1/§10; изменений в проверявшемся содержании он не
вносит (подтверждено read-only прогоном R8 на финальном HEAD; протокол R8 не
коммитится — иначе ревью никогда не совпало бы с финальным SHA, передан
владельцу в отчёте ревью).

Границы проверки: **только документальный слой**. Ни тесты, ни сборка, ни
UI/COM/печать/SMTP/реестр/сеть не запускались; счётчики TRX и хеши читались
как данные из закоммиченных артефактов.

Чего ревью **не** означает: оно не подтверждает физическую печать, установку
на чистую машину, реальный DPI 125/150, криптографическую подпись пакета
№163 и решение владельца по №127 — всё это остаётся открытым (разделы 7
и 8).
