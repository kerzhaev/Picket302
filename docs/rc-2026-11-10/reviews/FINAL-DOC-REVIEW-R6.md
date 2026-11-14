# Протокол независимого документального ревью (R6) — первичный отзыв

Источник: дословный ответ независимого ревьюера (qwen-reviewer), режим
только-чтение; предмет — документальная дельта
`c3547cd8acbc16d3d48afe4d0756a6b8dc4f3c61..4b146f9ec47c62be24329e2be71cd6869f34cf68`.
Файл сохранён без переписывания; ниже — полный текст отзыва.

- REVIEWED_SHA: `4b146f9ec47c62be24329e2be71cd6869f34cf68`
- Размер: 18848 байт
- SHA-256: `8412980ad59fba9d032f2408a5dc20f68b919f2f6196dba42b43084b2ada0489`

---

REVIEWED_SHA: 4b146f9ec47c62be24329e2be71cd6869f34cf68
BASE_SHA: 5af9874e8c7550e2accd9c24e4440e659f0810f5
VERDICT: CHANGES_REQUIRED
FINDINGS:

DOC-03 — P2 — docs/ACCEPTANCE.md:261 (строка №158) и docs/rc-2026-11-10/FINAL-REPORT-TO-OWNER.md §3.2 (строка 158, утверждение «Правило ACCEPTANCE … выполняется для всех шести»).
Что не так: строка №158 переведена в DONE для полного контракта ТЗ «Шаблоны, клипарт, шрифты и цвета: каталог, редактирование, библиотека картинок, выбор шрифта и цвета» (00_FUNCTION_INDEX.txt:200). Клипарт-часть (каталог/пикер/логотип/вставка в blank.xml: модель/XSD/сериализатор/рендер/редактор/V008 + ClipartInBlankTests 3) действительно закрыта — проверено. Выбор ШРИФТА в редакторе бланков есть (BlankFieldSpec.FontName/FontSizePt/Bold/Italic, XSD FontType, PropertyGrid-инспектор). Но выбор ЦВЕТА для полей бланка в пути редактора/сериализации не подтверждён: инспектор привязан к `_контроллер.GetSelected()`, который возвращает `BlankFieldSpec` (src/Picket302.Printing/Editor/CanvasController.cs:115), а в BlankFieldSpec НЕТ свойства цвета (полный список свойств BlankModel.cs:126–215 — color отсутствует), в BlankSchema.xsd атрибута цвета нет. ColorRgb существует только на доменной/БД-сущности src/Picket302.Domain/Entities/BlankField.cs:54 (репозиторий BlankFieldRepository, копия в FixA4Mode.cs:191) — это другой слой, не редактируемый через blank.xml-инспектор. ColorDialog в приложении есть только для HTML-редактора писем (HtmlMailEditor.cs:1220, функции 96/97) и настройки вида таблиц (FormGridAppearance.cs:370, FormTableSettings.cs:753, функции 12/131) — не для полей бланка.
Ожидаемое vs фактическое: по правилу ACCEPTANCE «реализация без названной в тексте функции составляющей → PARTIAL» именованная составляющая «выбор цвета» для шаблонов должна быть либо покрыта кодом+тестом, либо явно зарегистрирована как ограничение/решение владельца (как многоуровневая группировка для №10), либо статус должен остаться PARTIAL. Фактически строка заявляет DONE, обосновывая только клипарт-остаток, и не адресует цвет; §3.2 declares правило выполненным для всех шести без оговорки про цвет.
Доказательство (мои команды): `git show 4b146f9:src/Picket302.Domain/Serialization/BlankModel.cs` (свойства BlankFieldSpec — color нет); `git show 4b146f9:src/Picket302.Domain/Serialization/BlankSchema.xsd | grep -i color` → пусто; `git grep -n ColorRgb 4b146f9 -- src/` → только BlankField.cs/BlankFieldRepository.cs/FixA4Mode.cs; `git grep -ni ColorDialog 4b146f9 -- src/Picket302.App/` → HtmlMailEditor/FormGridAppearance/FormTableSettings (не бланки); CanvasController.GetSelected → BlankFieldSpec?.
Предлагаемое исправление: подтвердить у владельца/автора объём «выбор цвета» для №158; если требуется — показать код+тест выбора цвета поля бланка, либо зарегистрировать ограничение явной строкой (и при необходимости вернуть PARTIAL с пересчётом 156/5/2), либо документально обосновать, что цвет покрыт слоем ColorRgb/темизации и не входит в редактор blank.xml. Замечание предсуществующее (не регрессия правки): прежняя PARTIAL-строка тоже не адресовала цвет; правка закрыла клипарт-остаток, но цвет не рассмотрен.

ЗАКРЫТИЕ ПРЕДЫДУЩИХ НАХОДОК (подтверждено):
- DOC-01 (P1) — закрыто корректно: 6 строк (33/45/49/144/146/158) → DONE; для каждой проверены код, тесты, коммит и волновой документ (см. проверку 1). Пересчёт 157/4/2 сходится построчно.
- DOC-02 (P3) — закрыто: ACCEPTANCE.md:382 теперь «132/27/4 (сверка раунда S8 — историческое состояние на тот момент; текущая сводка — в разделе «Сводка» выше; правка по DOC-02)». Пометка достаточна, новой путаницы нет.

ФАКТИЧЕСКИЕ РЕЗУЛЬТАТЫ 9 ПРОВЕРОК:

1. Закрытие DOC-01 (6 строк по первичным источникам):
- №33 (ACCEPTANCE:91): DONE (04e91a3). ApplyAddressOrder встроен в конвейер (PrintSessionController.cs:316, метод :729); ApplyAddressOrderTests.cs существует, 5 [Fact]; коммит 04e91a3 «S21 close function 33 … +5 tests»; wave5 §S21 «ЗАКРЫТ». Контракт ТЗ (разбиение/реверс/перенос) покрыт. Чисто.
- №45 (ACCEPTANCE:108): DONE (e03b43e). FormPrinterCalibration.cs существует; ПолучитьСмещениеПринтера (PrintSessionController.cs:323/868); смещение в PrintPreviewEngine (OffsetXMm/OffsetYMm, строки 130–131); PrinterOffsetProfileTests.cs 4 [Fact]; коммит e03b43e «S20 close function 45 … +4 tests». Чисто.
- №49 (ACCEPTANCE:112): DONE (b428cb9). Меню «Специальные бланки» в MainShell.cs; SpecialFormsBuilder.cs; LbkPackageTests.cs:191 `Assert.Contains(… Name.Contains("Доверенность"))` + ExportBuiltinLibrary round-trip (Assert.All IsValid); подстановка onBeforePrint покрыта (BlankXmlSerializerTests/FormBlankEditorTests ссылаются на onBeforePrint). «6/6 зелёные» = BlankXmlSerializerTests.cs содержит ровно 6 [Fact]/[Theory], все входят в строгий набор 1590/0/0. Коммит b428cb9 «S23 close function 49». Нюанс: LbkPackageTests именованно проверяет «Доверенность», вторая форма («заявление») проходит через Assert.All(IsValid) без отдельного именованного ассерта; «6/6» — это общий набор сериализатора, а не 6 тестов именно спецформ. Утверждение доказуемо, не ложно; атрибуция слегка неточная (не блокирует).
- №144 (ACCEPTANCE:242): DONE (349198a). BuiltinTemplatesTests.cs:139 `Assert.True(библиотека.Count > 50)` + уникальность; коммит 349198a «catalog expanded to 52 real layouts … holiday cards». Чисто (перечень форматов ТЗ DL/C4/B4 поштучно не верифицирован — см. COVERAGE_GAPS).
- №146 (ACCEPTANCE:244): DONE (02343e8). AddressLabelsPdfExporter.cs существует; AddressLabelsPdfExporterTests.cs 3 [Fact]; коммит 02343e8 «A5/A4/A3 … PrintToFile PDF, cancellation +3 tests». Чисто.
- №158 (ACCEPTANCE:261): DONE (7376f64+8f05cbd+aac1ae0). ClipartId в BlankModel.cs:206, clipartId в BlankSchema.xsd:120, V008__add_blank_field_clipart_id.sql существует, ClipartInBlankTests.cs 3 [Fact]; коммиты существуют («S26 close function 158 remainder», «V008 migration», «wiring complete»). Клипарт-контракт закрыт. НЕ.addressована составляющая «выбор цвета» — DOC-03 (P2).

2. Пересчёт (построчный парсер строк функций ACCEPTANCE): rows=163, unique=163, диапазон 1..163 без пропусков и дублей; DONE 157 / PARTIAL 4 / DEFERRED 2, сумма 163; [К] 64 DONE + 1 PARTIAL = 65; [П] 75 DONE + 1 PARTIAL = 76; [Д] 18 DONE + 2 PARTIAL + 2 DEFERRED = 22. PARTIAL={110,121,127,163}, DEFERRED={14,125}. ПОЛНОСТЬЮ совпадает с заявленным 157/4/2 и [К]64+1/[П]75+1/[Д]18+2+2. Отличающихся строк нет.

3. Согласованность 157/4/2 по пакету: ACCEPTANCE сводка (строки 29–33) = 157/4/2; FINAL-REPORT §3 (строка 105) и §3.2 (итог) = 157/4/2; S50 §2 (строки 26–28) и §7 (строка 93) = 157/4/2; S01-baseline §5 — шесть строк 33/45/49/144/146/158 переведены в «**CLOSED (S.., см. git log)**» с «S.. ✓» и «перекрывает HIST», как у соседних закрытых. Остатки старых чисел: ACCEPTANCE:382 «132/27/4» — помечено историческое S8 (DOC-02); FINAL-REPORT:139 «151/10/2» — помечено «состояние реестра до исправления; источник — протокол R5, DOC-01»; FINAL-REPORT:264 «149/10/4» — внутри §9 снимка subject-ов коммитов (историческое). Все старые числа помечены историческими с источником; в финальных разделах старых чисел нет.

4. Закрытие DOC-02: ACCEPTANCE:382 — пометка «сверка раунда S8 — историческое состояние на тот момент; текущая сводка — в разделе «Сводка» выше» достаточна; оборот «совпадают со сводкой» удалён; новой путаницы нет.

5. Новые противоречия от исправления: §3.2 таблица (6 строк) — каждое утверждение «Что подтверждают первичные источники» проверено по коду/тестам/коммитам (см. проверку 1); недоказанных утверждений нет, КРОМЕ обобщения «правило выполняется для всех шести» в части цвета №158 (DOC-03). §6.1: R5 = CHANGES_REQUIRED (DOC-01 P1, DOC-02 P3), R6 = повторный прогон, вердикт в §10 — честно, R5 не выдан за PASS. §10: R5 REVIEWED_SHA c3547cd, VERDICT CHANGES_REQUIRED, протокол FINAL-DOC-REVIEW.md; R6-вердикт добавляется следующим коммитом; границы «только документальный слой», «чего ревью не означает» (физпечать/чистая машина/DPI/подпись №163/решение №127 открыты) — честно. Преамбула «Третий проход»: утверждение «код/тесты/конфигурации/бинарные артефакты ни в одном из проходов не менялись» подтверждено (проверка 6).

6. Неизменность кода: `git diff --name-only 09e152b..4b146f9` = 9 файлов, все под docs/ (ACCEPTANCE.md, FINAL-REPORT-TO-OWNER.md, S01-baseline.md, S50-final-report.md, BUILD-PROVENANCE.md, build-debug.log.gz, build-release.log.gz, FINAL-DOC-REVIEW.md, FINAL-R4-verdict.md). Ни одного файла под src/, tests/ или конфигураций сборки — подтверждено фактом. Дельта c3547cd..4b146f9 = 5 файлов (ACCEPTANCE, FINAL-REPORT, S01-baseline, S50, FINAL-DOC-REVIEW.md).

7. Реестр этапов (регресс-контроль): rows=50, unique=50, отсутствующие S01..S50=none, дубликаты=none, сгруппированных ID (диапазонов)=0; состояния COMPLETE 41 / BLOCKED_EXTERNAL 2 / OWNER_DECISION_REQUIRED 1 / NOT_STARTED 4 / NOT_RUN 1 / AWAITING_OWNER_ACCEPTANCE 1, сумма 50; 78 path-токенов существуют, MISSING=none.

8. Доказательства: SHA-256 пяти артефактов на 4b146f9 идентичны c3547cd и совпадают с отчётом: final4-debug=86a9c011d02c44b5f5c332faaed192de8b7d25e0c88af39d48c1536d1d77e099; final4-release=b9517ba9085e89037b93ec97ca6841faeba369aacdd1e34fcecd7726bf8d8517; build-debug=b49808007c2c890deb6c462fe459c033066732f19f7feefa73d9b7c77fb0625e; build-release=c0db7443a0eca9aff70494ba0622ffa03b48bdfc3902664a67fd51f3345b92b5; FINAL-R4-verdict=f79fb5fddcd7a45fb23dca76a9ba8cf09d3f4864f98eb8720340609a83187943 (7978 байт). final4 TRX оба: total=1590 executed=1590 passed=1590 failed=0 notExecuted=0. S50 §9 промежуточные (1385/1577/1582/1585/1587) помечены «промежуточные» (строки 110–160). FINAL-DOC-REVIEW.md: размер 17866 байт, SHA-256 c5299a092c5a21f664edbdf5880e62e9d72dd384c8191a379448a5ce907d855d — совпадают с §10; файл = шапка (строки 1–12) + дословный мой R5-ответ (строки 14–69), не переписан; внутренний текст отзыва: размер 17161 байт, SHA-256 00c7d84c85707bd473de9646273a39bd394302b25250bec81fa8614e39f68f6c — совпадают с шапкой файла (строки 9–10).

9. Гигиена: `git diff --check origin/main...HEAD` — пусто, exit 0; `git status --short --branch` — «## piscine/picket302-rc», чисто; HEAD=4b146f9; `git rev-list --count main..HEAD`=105. Самоссылающихся чисел коммитов нет: §1 FINAL-REPORT и §7 S50 заменены на команду `git rev-list --count main..HEAD` без фиксированного числа; §9 — снимок на d21ff23 (103, исторический, не устаревает). grep «104/105» в контексте счёта коммитов — none.

COVERAGE_GAPS:
- Сборку/тесты/UI/COM/печать/SMTP/сеть/реестр не запускал (запрещено); счётчики TRX и хеши читаны как данные из закоммиченных артефактов. Коды выхода сборки — по логам («Ошибок: 0») + BUILD-PROVENANCE.
- №158: цвет проверен по модели/XSD/инспектору/поиску ColorDialog; не проверял legacy-референс KonvertProNet на предмет того, входил ли per-field цвет в редактор blank.xml исторически — это контекст для решения владельца по объёму (DOC-03).
- №144: поштучное наличие именованных в ТЗ форматов DL/C4/B4 в каталоге не верифицировал (тест проверяет >50 + уникальность + праздничные; LbkPackageTests — C5); перечень форматов ТЗ трактуется как иллюстративный.
- №49: «6/6» отнесено к набору BlankXmlSerializerTests (6 тестов, зелёные в 1590); отдельного именованного ассерта на вторую форму («заявление») в LbkPackageTests нет (проходит через Assert.All IsValid).
- Полноту прочих 157 DONE-строк по коду выборочно не верифицировал (только 6 спорных + cited files); принято по наличию путей и агрегатам.
- S01-baseline §5 строка 35 остаётся «DEFERRED … S22» (при ACCEPTANCE №35 = DONE, S22 COMPLETE) — предсуществующее расхождение вне шести строк и вне дельты; не проверял глубоко, отмечаю как наблюдение.

INTEGRATION_RISKS:
- DOC-03: если «выбор цвета» для полей бланка входит в объём №158, текущий DONE и агрегат 157/4/2 завышены (фактически 156/5/2); при приёмке владельцем это даст ложный сигнал о полноте функции. Требуется решение объёма + честная строка (код+тест / ограничение / PARTIAL) до слияния.
- Протокол R6 и его вердикт добавляются следующим коммитом (§10); до этого пакет по собственной структуре неполон (HEAD=105, R6-протокол отсутствует).
- Предсуществующее расхождение S01 §5 строка 35 (DEFERRED) vs ACCEPTANCE №35 (DONE) может всплыть при следующей сверке реестров; рекомендуется отдельная реконсиляция.
- Атрибуция «6/6 сериализация форм» (№49) и перечень форматов №144 — Minor: при аудите могут потребовать уточнения источников; не блокируют.

Ключевые файлы (абсолютные пути): C:\Users\Nachfin\p302-worktrees\rc-20261110\docs\ACCEPTANCE.md; C:\Users\Nachfin\p302-worktrees\rc-20261110\docs\rc-2026-11-10\FINAL-REPORT-TO-OWNER.md; C:\Users\Nachfin\p302-worktrees\rc-20261110\docs\rc-2026-11-10\S01-baseline.md; C:\Users\Nachfin\p302-worktrees\rc-20261110\docs\rc-2026-11-10\S50-final-report.md; C:\Users\Nachfin\p302-worktrees\rc-20261110\docs\rc-2026-11-10\reviews\FINAL-DOC-REVIEW.md; C:\Users\Nachfin\p302-worktrees\rc-20261110\src\Picket302.Domain\Serialization\BlankModel.cs; C:\Users\Nachfin\p302-worktrees\rc-20261110\src\Picket302.Printing\Editor\CanvasController.cs; C:\Users\Nachfin\p302-worktrees\rc-20261110\src\Picket302.Domain\Entities\BlankField.cs; C:\Users\Nachfin\p302-worktrees\rc-20261110\00_FUNCTION_INDEX.txt. Временные файлы ревьюера удалены.
