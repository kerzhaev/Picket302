# FINAL REPORT TO OWNER — RC Picket302 (проверочный пакет, реконсиляция)

Дата: 2026-11-14. Ветка `piscine/picket302-rc`. **Не пушится** — слияние и
решения раздела 7 за владельцем. Настоящая редакция — результат
корректирующего прохода по замечаниям независимой проверки: устранены
противоречия «47 завершено / 42 по перечню», «1587 для final4», «три
раунда», ложный DEFERRED №10; добавлены первичный протокол R4,
фактические счётчики TRX с SHA-256 и независимый прогон сборки.

## 1. Идентификаторы

| Параметр | Значение |
|---|---|
| BASE_SHA | `5af9874e8c7550e2accd9c24e4440e659f0810f5` (origin/main) |
| REVIEW-кандидат R4 (вердикт PASS) | `09e152bafb0de8bd4117545db96e5afe24283b6c` |
| Итоговый HEAD (с реконсиляцией) | см. `git rev-parse HEAD` — фиксируется коммитом настоящего документа; код-слой идентичен 09e152b (`git diff --stat 09e152b..HEAD` до этого коммита = только .md) |
| Коммитов от main | 102 на момент реконсиляции (+1 коммит реконсиляции; точный счёт — `git rev-list --count main..HEAD`) |

## 2. Статусы 50 этапов (однозначная таблица)

Арифметика: **COMPLETE 41 + AWAITING_OWNER_ACCEPTANCE 1 (S50) +
BLOCKED_EXTERNAL 2 (S18, S19) + OWNER_DECISION_REQUIRED 1 (S41) +
NOT_STARTED/DEPENDENT_ON_OWNER 4 (S42–S45) + NOT_RUN 1 (S49) = 50.**

| Этап | Состояние | Критерий закрытия | Первичный источник | Ограничение/решение |
|---|---|---|---|---|
| S01–S17 | COMPLETE | базлайн/захват/матрица/coverage/дефекты/COM-волна по своим докам + ревью PASS | S01–S15-доки, WAVE1–WAVE3-чекпойнты | — |
| S18 | BLOCKED_EXTERNAL | физическая печать A4 на стенде | S18-S20-док | физический стенд отсутствует на станции |
| S19 | BLOCKED_EXTERNAL | лотки/термопринтер на стенде | S18-S20-док | то же |
| S20–S40 | COMPLETE | калибровка №45, волны 5–8 (клипарт, отчёты №56/58/66/67/73, входящие №29/30, трекер №110, поддержка №109/136, SMTP №94–100), корректирующий проход владельца | S20–S40-доки, WAVE4–WAVE8-чекпойнты | — |
| S41 | OWNER_DECISION_REQUIRED | архитектурный документ №127 готов и ревью-нейтрален | S41-db-architecture.md | `OWNER_DECISION=…` — владелец |
| S42–S45 | NOT_STARTED/DEPENDENT_ON_OWNER_DECISION | не выполнялись | S41 §6 | ждут решения №127 |
| S46–S48 | COMPLETE | №121/№126 тесты, №163 целостность+HTTPS-only (PARTIAL по функции — см. §3), №14/№125 deferred-обоснования | S46–S48-доки, EditionsAndVmDetectorTests, UpdatePackageTests | — |
| S49 | NOT_RUN | установка на чистой машине | S48-S49 §4 | нужен чистый контур владельца |
| S50 | AWAITING_OWNER_ACCEPTANCE | цикл работ и отчётности завершён, итоговые документы подготовлены; принятие владельцем не получено | настоящий отчёт | до принятия владельцем этап не считается COMPLETE |

Формулировка итога: **цикл RC-работ завершён исполнителем** (все
запланированные и не заблокированные этапы выполнены, отчётность
подготовлена); **фактически COMPLETE — 41 этап из 50**; 8 этапов
ограничены внешними контурами/решениями (см. таблицу) и не маскируются
под READY.

## 3. Функции (163, docs/ACCEPTANCE.md)

**DONE 151 / PARTIAL 10 / DEFERRED 2 = 163** (построчный парсер строк
функций с приоритетом [П]/[Д]/[К]; идентификаторы 1–163 ровно по одному
разу; №10 «Группировка строк» → DONE по результату сверки контракта —
см. §3.1). Разбивка по приоритетам: [К] 64 DONE + 1 PARTIAL (№127);
[П] 70 DONE + 6 PARTIAL; [Д] 17 DONE + 3 PARTIAL + 2 DEFERRED (№14, №125).

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

Ключевые честные PARTIAL: №163 (SHA-256 = целостность, НЕ подпись; ключ и
канал — владелец; UI-путь установки закрыт), №110 (реальный pochta.ru не
вызывается), №121 (нет предупреждения в «О программе»), №127 (OWNER_DECISION).

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
(`b4980800…`), build-release.log.gz (`c0db7443…`)}. Исторические
заявления R1–R4 «0 warnings» были подкреплены только commit messages —
настоящий прогон закрывает этот пробел независимо.

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

```
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
(102 коммита от main на момент реконсиляции; настоящий коммит реконсиляции — следующий за списком)
```
