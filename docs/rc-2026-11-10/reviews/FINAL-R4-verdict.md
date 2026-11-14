REVIEW_STAGE: RC final review R4 (дельта 1e62d41..09e152b + санитизатор style)
REVIEWED_SHA: 09e152bafb0de8bd4117545db96e5afe24283b6c
BASE_SHA: 5af9874e8c7550e2accd9c24e4440e659f0810f5

Проверено фактически (по SHA, только чтение):

1. Дельта. `git diff --name-only 1e62d41..09e152b` = ровно 5 файлов: docs/ACCEPTANCE.md, docs/rc-2026-11-10/evidence/runtime-final4/final4-debug.trx.gz, …/final4-release.trx.gz, src/Picket302.App/UI/Editors/HtmlMailEditor.cs, tests/Picket302.Tests/App/HtmlMailEditorTests.cs. Единственный коммит после 1e62d41 — 09e152b. `rev-list --count 5af9874..09e152b` = 101.

2. Фиксы по SHA (HtmlMailEditor.cs):
- FINAL3-02: ЭкранироватьЗначениеСтиля (строки 635-643) — `.Replace("&","&amp;")` идёт ПЕРВЫМ, затем " ' < >. Инвариант подтверждён по всему конвейеру: парсер РазобратьАтрибуты (378-458) отдаёт СЫРОЕ значение → ДопустимыйАтрибут (516) декодирует WebUtility.HtmlDecode ровно один раз → гейт ДопустимыйСтиль проверяет декодированную строку → сериализация (326) пишет `style="…"`, экранируя &. Браузер декодирует сущности атрибута один раз и не рекурсивно → видит ровно проверенную гейтом подстроку; CSS-движок сущности не декодирует. Остаточные `&#101;`/`&#38;` инертны.
- FINAL3-03: гейт (604-610) отклоняет знак < 0x20 кроме \t\n\r и 0x7F — целиком стиль. \f (0x0C, CSS-whitespace) тоже отклоняется.
- FINAL3-01: docs/ACCEPTANCE.md строки 35-38 — разбивка DEFERRED теперь №10/№14/№125 + пометка «№35 переведён в DONE — a28b8f4, PrintOptionsLastParametersTests». Строка 83: `| 35 | … | **DONE (a28b8f4)** |`. Коммит a28b8f4 существует («S22 close function 35 … +7 tests»); tests/Picket302.Tests/Application/PrintOptionsLastParametersTests.cs присутствует в дереве SHA. Правка ЗАКОММИЧЕНА (не в рабочем дереве).

3. Охота на обходы (новый гейт): чёрный список {url(, expression, javascript, vbscript, behavior, @import, script, <} по lowercased-строке; отдельно отклоняются " ' /* */ \ и C0/DEL; whitelist свойств {color, text-align, font-weight, font-style, text-decoration, background-color, font-size, font-family}.
- (a) calc( не в чёрном списке, но в whitelisted-свойствах безвреден (нет fetch/exec). var/env/attr/image-set/cross-fade/paint/element: либо требуют url( (заблокирован), либо кавычек (заблокированы), либо зарегистрированного worklet (нужен JS), либо неприменимы к whitelisted-свойствам (element/image — только background-image, которого нет в whitelist). Вектора нет.
- (b) `&#38;` → decode `&` → escape `&amp;` → браузер `&` → CSS инертно; `&amp;#38;` → decode `&#38;` → escape `&amp;#38;` → браузер `&#38;` → CSS не декодирует → инертно. Безопасно.
- (c) `;` внутри значения: кавычки отклонены → `font-family:"a;b"` невозможен; без кавычек `;` дробит декларации, новое свойство обязано быть в whitelist, чёрный список применяется ко всей строке. Инъекции нет.
- (d) OWASP CSS: backslash-escape и комментарии заблокированы; C0/DEL заблокированы; регистр свёрнут; fullwidth/Unicode CSS не нормализует (инертно); C1 (0x80-0x9F) не CSS-whitespace (инертно). Trim не создаёт токенов (чёрный список проверен на суперстроке). `>` экранируется и не прорывает кавычный атрибут. Обходов не найдено.

4. Числа (runtime-final4/*.trx.gz, распакованы и прочитаны):
- final4-debug.trx: Counters total=1590 executed=1590 passed=1590 failed=0 error=0 timeout=0 aborted=0 inconclusive=0 notExecuted=0; outcome=Completed; UnitTestResult outcome=Passed = 1590, Failed = 0; assembly bin/x64/Debug/net8.0-windows; Times start 2026-11-14T10:37:22 finish 10:38:11.
- final4-release.trx: идентичные счётчики 1590/0/0; Passed=1590 Failed=0; assembly bin/x64/Release/net8.0-windows; start 10:38:12 finish 10:38:58. Оба прогона из C:\Users\Nachfin\p302-worktrees\rc-20261110, до коммита (10:39:49).
- 3 новых пина присутствуют и Passed в ОБОИХ TRX: Санитизатор_СтильДвойноеКодирование_СущностьНеВосстанавливается, Санитизатор_СтильЛегитимныйАмперсанд_Экранируется, Санитизатор_СтильСУправляющимиСимволами_Отбрасывается. 1590 = 1587 + 3 согласуется с «+3 pins».
- `git diff --check 5af9874...09e152b` — пусто, exit 0.

Findings: none

COVERAGE_GAPS:
- Warnings 0/0 (Debug+Release) принято по commit message 09e152b; в дельте и в docs/rc-2026-11-10/evidence/runtime-final4/ нет артефактов сборки/логов компилятора (только 2 trx.gz). Независимо не подтверждено — сборку не запускал (запрещено). Найденные «warning»-файлы в evidence — UI-скриншоты/stdout других этапов, не счётчики компилятора.
- Инертность CSS-значений проверена статическим анализом гейта и инварианта декодирования, а не реальным браузерным рендером (окна/процессы запускать запрещено). Пин FINAL3-02 проверяет отсутствие «expression» и наличие «&amp;#101», что корректно, но не является браузерным proof.
- Полнота охвата прочих HTML-входов вне дельты не перепроверялась (предмет предыдущих раундов R1-R3).

INTEGRATION_RISKS:
- Любой новый путь рендера не доверенного HTML обязан использовать ОчиститьОпасныйHtml; синтезированные style из команд редактора (НормализоватьЦвет/Выравнивание, строки 124/129) безопасны по построению (hex/константы), но при расширении команд нужен контроль, чтобы значения не обходили гейт.
- Расширение whitelist свойств стиля (особенно background-image/свойств, принимающих <image>) потребует пересмотра чёрного списка (element()/image-set() и т.п.) — текущая безопасность опирается на узкий whitelist.
- Отсутствие артефакта warnings означает, что регресс «0 warnings» в следующих этапах нечем автоматически подтвердить — рекомендуется класть build-log/binlog в evidence.

VERDICT: PASS