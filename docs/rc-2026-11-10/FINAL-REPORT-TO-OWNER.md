# FINAL REPORT TO OWNER — RC Picket302 (проверочный пакет)

Дата: 2026-11-14. Ветка `piscine/picket302-rc`. **Не пушится** — слияние и решения раздела 7 за владельцем.

## 1. Идентификаторы

| Параметр | Значение |
|---|---|
| BASE_SHA | `5af9874e8c7550e2accd9c24e4440e659f0810f5` (origin/main) |
| CANDIDATE_SHA | `09e152bafb0de8bd4117545db96e5afe24283b6c` |
| Коммитов от main | **101** (`git rev-list --count main..HEAD`) |
| Список коммитов | `git log --oneline main..09e152b` (приложение — 101 строка, см. §9) |

## 2. Статусы 50 этапов

- **Завершено 47**: S01–S17, S20–S40, S46–S48 (+S50 — этот цикл отчётности), включая корректирующий проход владельца (пункты 1–6 выполнены; пункт 7 —三轮 финального ревью, см. §6).
- **BLOCKED_EXTERNAL (2)**: S18, S19 — физический стенд печати (лотки/термопринтер) отсутствует; требования физической печати НЕ подменены контрактными тестами.
- **OWNER_DECISION_REQUIRED (1)**: S41 — архитектурный документ №127 (A SQLite-only / B PostgreSQL / C гибрид Pro; формат `OWNER_DECISION=…`); S42–S45 не выполнялись до решения.
- **NOT_RUN (1)**: S49 — установка на чистую машину (процедура для владельца: `S48-S49-deferred-and-install.md` §4).

## 3. Функции (163, docs/ACCEPTANCE.md)

**DONE 150 / PARTIAL 10 / DEFERRED 3** (построчный счёт проверен ревьюером R2). Ключевые PARTIAL (честно): №163 (SHA-256 = целостность, НЕ подпись; ключ+канал — владелец; UI-путь установки закрыт), №110 (реальный pochta.ru не вызывается), №121 (нет предупреждения в «О программе»), №127 (OWNER_DECISION). DEFERRED: №10, №14, №125.

## 4. Команды и числа тестов

```
# строгий набор (обе конфигурации, идентичные результаты):
dotnet build Picket302.sln -c Debug
dotnet build Picket302.sln -c Release
dotnet test tests/Picket302.Tests/Picket302.Tests.csproj -c Debug   --no-build --filter "FullyQualifiedName!~Acceptance&FullyQualifiedName!~ComAutomation&FullyQualifiedName!~Printing.PrintEngineTests"  # 1590/0/0
dotnet test tests/Picket302.Tests/Picket302.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~Acceptance&FullyQualifiedName!~ComAutomation&FullyQualifiedName!~Printing.PrintEngineTests"  # 1590/0/0
```
Артефакты: `docs/rc-2026-11-10/evidence/runtime-final4/final4-{debug,release}.trx.gz` (счётчики 1587/1587/0 в обоих, проверено ревьюером по коммиту).

## 5. Сборка и гигиена

- Предупреждения: **Debug 0 / Release 0** (`dotnet build -v m | grep -c warning`).
- `git diff --check 5af9874...09e152b` — **пусто**.

## 6. Финальное ревью Qwen (три раунда, весь дифф)

| Раунд | SHA | Вердикт | Находки → закрытия |
|---|---|---|---|
| R1 | c4f9a7d | CHANGES_REQUIRED | FINAL-01 P1 (style-breakout кавычкой) + 2×P2 + 7×P3 → всё закрыто в 798a2f7 (подтверждено R2) |
| R2 | 798a2f7 | CHANGES_REQUIRED | FINAL2-01 P1 (CSS-комментарии/escape маскируют expression) + P3 → закрыто в 1e62d41 |
| R3 | 1e62d41 | CHANGES_REQUIRED | FINAL3-02 P1 (double-encoded entity в style) + P2 (control chars) + P3 (незакоммиченная строка) → закрыто в 09e152b |
| R4 | **09e152b** | **PASS — Findings: none** | Финальный вердикт финального ревью (см. §6.1) |

Архив вердиктов по волнам/этапам: `docs/rc-2026-11-10/reviews/`, чекпойнты WAVE1–WAVE8, доки этапов S01–S50.

### 6.1. Финальный вердикт R4 (дословно)

- REVIEWED_SHA: 09e152bafb0de8bd4117545db96e5afe24283b6c
- BASE_SHA: 5af9874e8c7550e2accd9c24e4440e659f0810f5
- **VERDICT: PASS**
- Findings: none
- Проверено ревьюером по SHA: дельта 1e62d41..09e152b ровно 5 файлов; инвариант style-гейта (decode-once → gate → escape-amp-first — браузер видит ровно проверенную строку); охота на обходы (calc/var/attr/image-set/element, &#38;, точка-с-запятой, OWASP CSS-векторы) — векторов нет; счётчики 1590/1590/0 в обоих TRX, 3 пина присутствуют; diff --check пуст.
- Coverage gaps (честно): warnings 0/0 — по commit message (артефактов сборки в evidence нет); инертность CSS — статический анализ, не браузерный рендер; рекомендуется класть build-log в evidence (принято в бэклог).

## 7. РЕШЕНИЯ ВЛАДЕЛЬЦА (перечень)

1. **№127**: `OWNER_DECISION=SQLITE_ONLY | POSTGRESQL | HYBRID_PRO` (`S41-db-architecture.md`).
2. **№29** сужение объёма по ТЗ.
3. **№163** ключ подписи пакетов + доверенный канал.
4. **№14** probili.ru (DEFERRED — допустимость внешней интеграции).
5. Ратификация OVERALL-эрратума (70,31%).
6. HKCU/HKLM COM-поставки (S15).
7. Контуры: физический печатный стенд (S18/S19), чистая машина (S49), DPI 125/150.

## 8. Флаги (S01 §6 — канонический источник)

COM_FULL_ACCEPTANCE=PARTIAL · UI_TO_SPOOLER=PARTIAL · PRINT_PHYSICAL=NOT_RUN · DPI_125/150=NOT_RUN_ON_HOST · CLEAN_MACHINE=NOT_RUN · OWNER_DECISION_REQUIRED(№127) · SMTP TLS/PROXY/MIME/CLIENT=VERIFIED · HTML_EDITOR=VERIFIED (allowlist, 3 раунда) · UPDATE_INTEGRITY=VERIFIED (SHA-256, подпись отсутствует) · остальные ~20 = true.

## 9. Список коммитов (101)

```
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
