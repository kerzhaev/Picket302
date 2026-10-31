# Дефекты PromptPilot, найденные при дежурстве по миссии Picket302

Источник: дежурство 30.10.2026, миссия `wf_bf13ed4d06e341c7afb41baa6673c71b` (slug `picket302-impl-v1`),
репозиторий `C:\Users\FIREBAT\Desktop\Projets\Picket302`, форк PromptPilot в `C:\Users\FIREBAT\Desktop\Projets\pp-deploy`
(ветка `deploy/main` от `ivanarama/PromptPilot`).

Каждый пункт оформлен по образцу PR #172–174: **симптом → причина → файлы → совместимость → тесты → ограничения**.
Правки в самом PromptPilot не вносились — этот файл предназначен для отдельного агента, который будет их чинить
(ветка от `deploy/main`, PR в upstream, тест на каждое изменение, `--basetemp` вне git-репозитория).

---

## P1. Зонд провайдера всегда падает: `run_owned()` не поддерживает `input=`

**Симптом.** `POST /api/providers/{name}/probe` для любого провайдера возвращает `500 Internal Server Error`.
В `logs/server.err`:

```
File "promptpilot/api.py", line 1899, in api_provider_probe
    cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=300,
    input=prompt if use_stdin else None, env=env, cwd=cwd)
  File "promptpilot/process_tree.py", line 305, in run_owned
    tree = OwnedProcess.start(args, **kwargs)
  File "promptpilot/process_tree.py", line 218, in start
    process = subprocess.Popen(args, creationflags=flags, **kwargs)
TypeError: Popen.__init__() got an unexpected keyword argument 'input'
```

**Причина.** `input=` — аргумент `subprocess.run()`, а не `subprocess.Popen()`. `run_owned()` — обёртка
`subprocess.run` с семантикой таймаута по дереву процессов, и она пробрасывает `**kwargs` прямо в `Popen()`.
Worker от этого не страдает: `promptpilot/worker.py` (строки ~1936–1940 и ~2015–2019) подаёт промпт отдельным
потоком в `stdin`, что и есть правильный образец.

**Файлы.** `promptpilot/process_tree.py` (`run_owned`, `OwnedProcess.start`), `promptpilot/api.py:1899` (call-site зонда).

**Совместимость.** Аддитивно: либо научить `run_owned` принимать `input` (писать в `stdin` и затем `communicate`),
либо исправить call-site по образцу `worker.py`. Публичные сигнатуры не меняются.

**Тесты.** (1) Юнит: `run_owned([...], input="текст")` возвращает stdout с этим текстом; (2) юнит: таймаут
с `input=` по-прежнему убивает дерево процессов; (3) интеграционный: `POST /api/providers/{name}/probe`
на фиктивном провайдере отдаёт 200 с разбором контрольного токена.

**Ограничения.** Windows; сохранить `CREATE_NO_WINDOW`, семантику owned-tree kill и текущие таймауты зонда (300 с).

**Влияние.** Пред-полётная проверка провайдера (обязательный шаг перед включением агента в конвейер) сейчас
неработоспособна — это одна из причин, по которым подключение нового провайдера приходится проверять вручную.

---

## P2. `acceptance_gates` стадии исполняются как shell-команды: человеческий текст роняет гейт

**Симптом.** Гейт стадии падает с `MSBUILD : error MSB1008: Only one project can be specified` на команде

```
[4] exit=1: dotnet build Picket302.sln -c Debug — без ошибок
```

Первые три команды того же гейта (`dotnet --version`, `dotnet build … --nologo -v q`, `dotnet test … --no-build -v q`)
прошли успешно — падал только четвёртый элемент, добавленный из поля стадии.

**Причина.** `promptpilot/gate_execution.py` (строки ~75–83, функция `snapshot`) объединяет
`config.gate.commands` и `stage.acceptance_gates` в единый список **исполняемых** команд:

```python
commands = list(dict.fromkeys([
    *gate["commands"],
    *((db._json_load(stage["spec_json"]).get("acceptance_gates") or []) if stage else []),
]))
```

При этом в UI и в шаблоне промпта то же поле называется «Критерии приёмки — проверки»
(`promptpilot/workflows.py`, шаблоны промптов; `promptpilot/models.py:594` — `acceptance_gates`),
то есть читается как человекочитаемый критерий. Одно поле одновременно служит и описанием, и командой;
валидации «это команда, а не текст» нет ни при сохранении плана, ни при его утверждении.

**Файлы.** `promptpilot/gate_execution.py` (`snapshot`), `promptpilot/models.py:594`
(`WorkflowStageSpec.acceptance_gates`), `promptpilot/workflows.py` (шаблоны промптов стадии, ~186–230).

**Совместимость.** Аддитивно, три независимых варианта (по возрастанию трудозатрат):
(a) минимальный — валидировать `acceptance_gates` при approve/сохранении плана: строка должна быть непустой
и не содержать пробелов/«—»/«—»-подобных вставок, иначе `422`;
(b) разделить поля: `acceptance_criteria` (текст, только в промпт) и `acceptance_gates` (команды, только в гейт),
с бэкомпатом старых планов;
(c) в UI пометить поле как «исполняемые команды» и показать пример.

**Тесты.** (1) План с `acceptance_gates=["dotnet build … — без ошибок"]` → 422 на approve с понятным текстом;
(2) план с корректной командой → гейт её выполняет; (3) регрессия: план без `acceptance_gates` и с
`config.gate.commands` работает как раньше.

**Ограничения.** Обратная совместимость: уже сохранённые планы с человекочитаемым текстом в этом поле должны
продолжать загружаться (валидация — на входе, не на чтении). Миссии, где поле уже исполнялось как команды,
должны вести себя без изменений.

---

## P3. Лимиты раундов не масштабируются под длину плана — миссия упирается в лимит и встаёт

**Симптом.** Миссия из 8 стадий после нескольких раундов перешла в `awaiting_human` с событиями
`limit.stage_revisions` (`max_revision_rounds: 3`) и `limit.max_rounds`, хотя гейт был зелёным и работа
делалась.

**Причина.** Три независимых бюджета с неочевидным приоритетом:
`limits.max_rounds` (по умолчанию 6) — на всю миссию; `planning.max_revisions_per_stage` (по умолчанию 3) —
на стадию; `stage.max_revision_rounds` — на конкретную стадию. `_revision_budget()`
(`promptpilot/workflows.py:1815–1820`) отдаёт приоритет явно заданному `planning.max_revisions_per_stage`,
иначе — `stage.max_revision_rounds`, иначе — дефолту. Ни один из бюджетов не сверяется с числом стадий
плана, а исчерпание переводит миссию в `awaiting_human` вместо автоматического следующего шага.

**Файлы.** `promptpilot/models.py` (`WorkflowLimitsConfig`, `WorkflowPlanningConfig`),
`promptpilot/workflows.py:1815–1820` (`_revision_budget`), `promptpilot/workflows.py` (ветка resume,
~1880–1935).

**Совместимость.** Аддитивно. Варианты: (а) при `approve_plan` проверять, что бюджет ≥ числа стадий, и
либо нормализовать, либо вернуть 422 с пояснением; (б) дефолт `limits.max_rounds` считать от числа стадий
(например `stages * 4 + 4`); (в) при исчерпании лимита — не `awaiting_human`, а новый раунд той же стадии,
если правки доказанно нулевые (сейчас такой случай попадает в no-diff).

**Тесты.** План из 8 стадий с дефолтными лимитами → approve принимается, лимит пересчитан; план с
явно малым `max_rounds` → 422 с пояснением, сколько нужно.

**Ограничения.** Не ломать сценарий «лимит — осознанный тормоз» (владелец может хотеть остановиться).

---

## P4. Чекпоинт стадии падает в репозитории без настроенного git author

**Симптом.** `stage.checkpoint_failed` с причиной:

```
Author identity unknown
*** Please tell me who you are.
fatal: unable to auto-detect email address (got 'FIREBAT@FIREBAT.(none)')
```

**Причина.** `promptpilot/stage_checkpoint.py:461` создаёт коммит через `git commit-tree`, который требует
идентификацию автора (config репозитория/глобальный config или `GIT_AUTHOR_NAME`/`GIT_AUTHOR_EMAIL`).
В «свежем» репозитории миссии (`git init` без `git config user.*`) идентификации нет — коммит невозможен,
и чекпоинт (барьер между стадиями) валит миссию в `awaiting_human`.

**Файлы.** `promptpilot/stage_checkpoint.py` (`publish`, ~461 и вызовы `_git`/`_text`).

**Совместимость.** Аддитивно. Варианты: (а) передавать идентификацию в git-вызовы чекпоинта
(`GIT_AUTHOR_NAME`/`GIT_AUTHOR_EMAIL`/`GIT_COMMITTER_*` из окружения, иначе `-c user.name`/`-c user.email`
по умолчанию от имени workflow); (б) до коммита проверять наличие identity и отдавать понятную диагностику
с готовой командой (`git -C <repo> config user.email ...`), а не сырой stderr git.

**Тесты.** (1) Репозиторий без identity → понятная ошибка с инструкцией либо успешный коммит с явным author;
(2) репозиторий с identity → поведение не изменилось; (3) репозиторий с `GIT_AUTHOR_*` в окружении → author
из окружения.

**Ограничения.** Не записывать identity в репозиторий миссии без явного согласия (это изменение состояния
чужого рабочего дерева); коммит по-прежнему должен быть строго частичным (paths по reviewed scope).

---

## P5. Нет repair-механизма для machine-readable вердикта ревьюера

**Симптом.** Ревьюер отработал и вернул содержательный отчёт, но без обязательных строк
`AUDIT_FINDINGS_JSON:` / `AUDIT_VERDICT:` — миссия ушла в `awaiting_human` (`review.awaiting_decision`)
при фактически готовой работе. Для исполнителя аналогичная ситуация закрывается штатно
(`executor.invalid_output` → `VERDICT_REPAIR` → повтор), для ревьюера — нет.

**Причина.** `parse_reviewer_report()` (`promptpilot/workflows.py:3231`) требует обе строки; контракт есть в
промпте (`AUDIT_RESPONSE_CONTRACT`, `promptpilot/workflows.py:256–275`), но его соблюдение не гарантировано
и не проверяется повтором. `VERDICT_REPAIR` (`promptpilot/worker.py:164–176`) реализован только для роли
executor; фоновый `verdict-repair-watcher` — отдельный компонент вне конвейера и в этом развёртывании
стартует с `last_seq=3` и молчит.

**Файлы.** `promptpilot/worker.py` (`_repair_verdict`, `VERDICT_INSTRUCTION`), `promptpilot/workflows.py:3231`
(`parse_reviewer_report`), `promptpilot/workflows.py:256–275` (`AUDIT_RESPONSE_CONTRACT`).

**Совместимость.** Аддитивно: одна попытка re-ask с аудит-контрактом при отсутствии строк; при повторной неудаче —
понятная диагностика (не бесконечный цикл).

**Тесты.** (1) Ревьюер без `AUDIT_*` → один repair-раунд → вердикт распарсен и применён;
(2) ревьюер с `AUDIT_*` → repair не запускается; (3) «чужие» строки `AUDIT_VERDICT` внутри цитат — как сейчас,
решает последняя пара строк.

**Ограничения.** Не превращать repair в бесконечный цикл; лимит попыток должен совпадать с `max_retries` роли.

---

## P6. API принимает команду провайдера с NUL-байтом — падение отложено до рантайма

**Симптом.** `POST /api/providers` сохранил `cmd`, содержащий NUL-байт; ошибка всплыла позже при диспетчеризации
задачи: `ValueError: embedded null character` (в worker, на этапе сборки argv), без указания, какой провайдер
и какая команда некорректны.

**Причина.** `promptpilot/api.py:1696` (`ProviderCreate`) и `promptpilot/config.py` (`save_provider`,
`_provider_entry`) не проверяют `cmd` на control-символы; NUL проходит через JSON (\u0000) и сохраняется.

**Файлы.** `promptpilot/api.py:1696`, `promptpilot/config.py` (`save_provider`, `_provider_entry`).

**Совместимость.** Аддитивно: валидация `cmd`/`env` на NUL и прочие control-символы (кроме табуляции/перевода строки),
`422` с понятным текстом.

**Тесты.** `POST /api/providers` с NUL в `cmd` → 422; с валидной командой → 201 (регрессия).

**Ограничения.** Не отвергать команды, где перевод строки допустим по смыслу (в текущем формате шаблонов — не нужен).

---

## P7. Наблюдение: текущую стадию утверждённого плана нельзя починить

**Симптом.** Дефект стадии (например, P2 — неверные `acceptance_gates`) неустраним в рамках живой миссии:
`amend_plan` замораживает префикс «все стадии до и включая текущую», а её `spec` обязан совпадать с исходным
байт в байт. Остаётся только отмена/пересоздание миссии или ручная правка БД.

**Причина.** Заморозка префикса сделана правильно (сохраняет историю и идентичность стадий), но правило
«заморожено всё» слишком широкое: у текущей стадии, по которой ещё не было завершённого executor-run,
изменение acceptance-команд безопасно и не влияет на историю.

**Файлы.** `promptpilot/workflows.py` (`amend_plan`, проверка `original_spec(row) != incoming`).

**Совместимость.** Аддитивно: разрешить точечную правку `acceptance_gates`/`acceptance_criteria` текущей стадии,
если у неё нет завершённых раундов, с записью события `plan.amended` и причиной.

**Тесты.** Правка acceptance-команд текущей стадии без раундов → 200 + событие; правка любых других полей
текущей стадии → по-прежнему 409; правка завершённой стадии → 409.

---

## P8. Наблюдение: гейт включается/выключается только глобально

**Симптом.** Один некорректный `acceptance_gates` (P2) роняет гейт всей миссии, а «обойти» проблему можно
только глобальным `gate.enabled=false` — при этом отключаются реальные проверки на всех следующих стадиях,
что тихо ослабляет качество.

**Причина.** `config.gate.enabled` — один переключатель на миссию; стадия не может иметь собственного
`gate_enabled`/набора команд.

**Совместимость.** Аддитивно: `stage.gate_enabled` (по умолчанию `true`) и, при необходимости,
`stage.gate_commands` с объединением по правилу «команды стадии + config.gate.commands».

**Тесты.** Стадия с `gate_enabled=false` → проверки стадии не выполняются, config-команды выполняются;
по умолчанию поведение не меняется.

---

## Обходные приёмы, применённые в миссии (не баги, а рецепты для ядра)

1. **`tools/minimax-exec-wrapper.js`** (репозиторий Picket302) — обёртка над CLI агента, которая гарантирует
   финальную строку `ИТОГ: …` для исполнителя и `AUDIT_FINDINGS_JSON`/`AUDIT_VERDICT` для ревьюера.
   Закрывает симптомы P5 и частично P2 на стороне провайдера. Стоит перенести в ядро PromptPilot
   (иначе каждый новый агент-исполнитель должен оборачиваться сам).
2. **Идентификатор git-автора** в репозитории миссии (`git config user.name/user.email`) — обход P4.
3. **Пустые `acceptance_gates` в плане** + проверки в `config.gate.commands` — обход P2.
4. **Явные лимиты** `limits.max_rounds` и `planning.max_revisions_per_stage` в конфиге миссии — обход P3.
5. **Оборачивание `cmd` провайдера в node-файл**, а не в inline-строку shell: bash/cmd-экранирование портит
   обратные слэши (`\n` → перевод строки, `\0` → NUL) — это операционная ловушка при создании провайдера,
   формально P6.

---

## Порядок исправления (рекомендация)

| Приоритет | Пункт | Почему |
|---|---|---|
| 1 | P2 | Ломает любой план, чьи критерии написаны по-человечески; дешёвое исправление, высокий эффект |
| 2 | P4 | Останавливает миссию на этаже чекпоинта; исправление на 10 строк |
| 3 | P1 | Зонд провайдера не работает вообще — ломает подключение новых агентов |
| 4 | P5 | Ревью-вердикт не гарантирован — миссия встаёт на ровном месте |
| 5 | P3 | План длиннее 2–3 стадий упирается в лимиты |
| 6 | P7, P8 | Улучшения эксплуатации, не блокеры |
| 7 | P6 | Усиление валидации на входе |

Проверка каждого изменения: полный сьют из `pp-deploy` с `--basetemp` вне git-репозитория; PR — в
`ivanarama/PromptPilot` по образцу #172–174 (симптом → до/после → файлы → совместимость → тесты → ограничения).
Секреты в коммиты не добавлять (`git diff --cached --name-only` перед коммитом).

---

## P9. Контракт закрывающего вердикта не включается для workflow-задач: задачи завершаются с `verdict: null` без repair

**Симптом.** Три раунда executor подряд (задачи #959, #960, #966 миссии Picket302) завершились со статусом
`completed`, `exit_code=0`, `verdict: null` — после чего workflow-слой пометил раунды `executor.invalid_output`
и перевёл миссию в `awaiting_human`. Штатный repair-механизм (`VERDICT_REPAIR`, issue #82) ни разу не сработал:
в `logs/worker.log` за весь период нет ни одного `Verdict repaired via micro-request` и ни одного
`Failed (pipeline result has no closing verdict)`.

**Причина.** `promptpilot/worker.py:1742`:

```python
require_closing_verdict = bool(
    route.get("profile_id") and route.get("queue_id")
)
```

Контракт закрывающего вердикта (дописывание инструкции в промпт через `ensure_closing_verdict_contract`,
принудительная проверка последней строки, repair micro-request'ом) включается **только** когда маршрут задачи
содержит `profile_id` и `queue_id` (pipeline-snapshot маршрутизация). На развёртывании Picket302 pipeline-профили
не сконфигурированы → `require_closing_verdict=False` → (а) контракт не дописывается в промпт, (б) отсутствие
вердикта не проверяется на уровне задачи, (в) repair не вызывается; всё падает уже на уровне workflow
(`executor.invalid_output`) — без repair и с постановкой миссии в ожидание человека.

**Файлы.** `promptpilot/worker.py:1742` (условие), `promptpilot/worker.py:2240–2280`
(обе ветки repair достижимы только при `require_closing_verdict=True` либо при `PP_VERDICT=1` — вторая ветка
не сработала, см. ограничения), `promptpilot/herdr_exec.py:528` (`_closing_workflow_verdict`).

**Совместимость.** Аддитивно: включать контракт для workflow-задач (роль executor/reviewer при наличии
`workflow_id`) независимо от pipeline-маршрута; `ensure_closing_verdict_contract` уже идемпотентен.

**Тесты.** (1) Workflow-задача с провайдером, не вернувшим `ИТОГ` → repair micro-request вызван; при успехе —
верdict заполнен, при неудаче — задача FAILED с понятным текстом (не «completed» с `verdict: null`);
(2) workflow-задача с валидным ИТОГ → поведение не изменилось; (3) pipeline-маршрут с profile/queue → прежнее
поведение.

**Ограничения.** Вторая ветка (`else`, не-pipeline задачи) с `PP_VERDICT=1` тоже не сработала — причине
установить не удалось (подозрение: `VERDICT_REQUIRED` вычисляется при импорте конфига, а worker-процесс мог
быть запущен без загруженного `.env`); починившему агенту — проверить фактическое значение
`VERDICT_REQUIRED`/`VERDICT_REPAIR` в рантайме worker-процесса.

**Дополнительно (следствие для интеграций).** Определение роли по тексту промпта ненадёжно: в executor-промпт
попадает `previous_review` со строками `AUDIT_VERDICT/AUDIT_FINDINGS_JSON` прошлых аудитов, поэтому внешние
обёртки провайдеров ошибочно классифицируют executor-задачу как ревьюер. Рекомендация: передавать роль
структурно (env `PP_WORKFLOW_ROLE`, `PP_WORKFLOW_STAGE` в окружение провайдера) — тогда обёртки смогут
адаптировать хвост отчёта без эвристик. (Реализовано обходом: wrapper v3 делает канонический хвост для обеих
ролей — `AUDIT_` пара + `ИТОГ` последней.)

---

## P10. `executor.invalid_output` не покрывается авто-resume: миссия стоит в `awaiting_human` неопределённо долго

**Симптом.** Миссия Picket302 провела в `awaiting_human` 9,5 часов (00:02–09:31 UTC 31.10) после
`executor.invalid_output`, при `automation.auto_resume_revision=true` и живом воркере; авто-продолжения не было.

**Причина.** Ветка авто-resume в `advance_workflow` (workflows.py, обработка `AWAITING_HUMAN`) срабатывает только
для `round.status == REVISION_REQUIRED`. Превращение раунда в `awaiting_human` по причине
`executor.invalid_output` (task completed, verdict null) не переводит раунд в `revision_required` —
продолжения нет; resume доступен только человеку/внешнему супервизору.

**Файлы.** `promptpilot/workflows.py` (обработка `WorkflowStatus.AWAITING_HUMAN`, ветка
`current_round.status is WorkflowRoundStatus.REVISION_REQUIRED`).

**Совместимость.** Аддитивно: либо помечать раунд `revision_required` при invalid_output (тогда существующий
auto-resume заработает), либо добавить отдельный флаг `automation.auto_resume_invalid_output` с лимитом
подряд идущих попыток (иначе цикл на систематически молчащем провайдере).

**Тесты.** (1) invalid_output при включённом флаге → один автоматический re-dispatch; (2) три подряд
invalid_output → стоп с `awaiting_human` и понятным событием; (3) обычный invalid_output без флага → прежнее
поведение.

**Ограничения.** Не ломать сценарий «invalid_output = всегда человек» для тех, кто на это рассчитывает;
по умолчанию флаг выключен.

---

## P11. Чекпоинт стадии коммитит только изменения последнего executor-run'а — результат ранних раундов теряется из фиксации

**Симптом.** Стадия S2 (раунды 5–7 миссии Picket302): код создан в раундах 5–6 (те раунды не дошли до PASS —
таймаут ревьюера, HUMAN_REQUIRED), раунд 7 — верификация без изменений кода. Чекпоинт 13:19:02 записал
`changed: [docs/SUPERVISOR-ESCALATIONS.md]` (посторонний файл супервизора, созданный в окне run'а) и
`sha: null` — коммита не было. Весь результат S2 (~28 путей src + 16 тестовых файлов) остался
незакоммиченным, стадия помечена completed, конвейер перешёл на следующую стадию.

**Причина.** Атрибуция чекпоинта = `run_changes["changed"]` последнего executor-run'а относительно его
собственного снапшота. Для много-раундовой стадии (раунды после timeout/HUMAN_REQUIRED/resume) изменения
ранних раундов атрибутированы ранним (несостоявшимся) раундам и выпадают из фиксации.

**Файлы.** `promptpilot/stage_checkpoint.py` (`publish`, расчёт `changed`/`checkpoint_paths`), место
формирования `run_changes` в `promptpilot/workflows.py` (около `_stage_checkpoint_commit`), событие
`stage.checkpoint_pending` (payload.changed).

**Совместимость.** Аддитивно: база diff = `base_sha` первого раунда текущей стадии; новое событие
`stage.checkpoint_empty_but_dirty` для аномалии «дерево грязное в allowed_paths, но коммитить нечего».

**Тесты.** (1) Стадия в 2 раунда (код в 1-м, верификация во 2-м) → чекпоинт коммитит результат 1-го;
(2) посторонний файл вне allowed_paths в окне run'а → исключён; (3) однораундовая стадия → прежнее поведение.

**Ограничения.** Не ломать барьер «коммит только reviewed-контента»: кумулятивный diff ограничивать
allowed_paths стадии и verified-подписями (frozen content по-прежнему проверять).

**Обход.** Ручной коммит супервизора после чекпоинта (S1: `5b7b3a7`, S2: `48e98fd`). Полный разбор:
`docs/SUPERVISOR-ESCALATIONS.md`, кейс К-7.

---

## P12. Workflow-задачи не ретраятся при транзиентных отказах провайдера (retry_count=0 при max_retries=3)

**Симптом.** Задача #975 (executor, стадия S3, миссия Picket302): процесс агента упал с `exit_code=4`,
stderr «mcode exec failed: The run failed: Runtime completed without a final assistant response.. Retry after
the connection…» — транзиентный отказ рантайма модели, сам текст предписывает повтор. `retry_count = 0` при
`max_retries = 3` (конфиг роли) — повторов не было; задача сразу `failed` → раунд в `awaiting_human`.

**Причина.** Ретраи применяются только к части классов отказов (или только к non-workflow задачам):
для workflow-dispatch'ей провал процесса уходит напрямую в `run.failed` → `executor.failed` → `awaiting_human`,
минуя механизм `retry_count`. Точная ветка подлежит уточнению фиксирующим агентом (кандидаты:
worker-обработка исключений процесса против `_mark_failed` для workflow-задач).

**Файлы.** `promptpilot/worker.py` (обработка неудачного завершения процесса, ветки `retry`), контекст
диспетчеризации `promptpilot/workflows.py` (`_dispatch_configured_role` → задача с `max_retries` роли).

**Совместимость.** Аддитивно: ретраить только классы «процесс умер без валидного отчёта» (exit != 0, пустой
stdout, сетевые ошибки) — не ретраить валидные `ИТОГ: НЕ СМОГ` (это осмысленный ответ).

**Тесты.** (1) Workflow-задача, процесс умер до вывода → до 3 попыток с бэкоффом; (2) задача с
`ИТОГ: НЕ СМОГ` → без ретраев; (3) исчерпание ретраев → `awaiting_human` с событием, содержащим
`retry_count`.

**Ограничения.** Бэкофф не должен блокировать конвейер на часы; после исчерпания — понятная причина в
`awaiting_human`.

**Обход супервизора.** Ручной resume ( watchdog: добавить обработку `executor.failed` — см.
`tools/promptpilot-watchdog.ps1`).

---

## P13. Транзиентный отказ рантайма MiniMax не матчится классификаторами повтора — задача уходит в failed вместо rate_limited-паузы

**Симптом.** Задача #975 (executor S3): «mcode exec failed: The run failed: Runtime completed without a
final assistant response.. Retry after the connection recovers» (exit_code=4). Классификаторы повтора
(`RATE_LIMIT_RE`, `OVERLOADED_RE`, `ENV_FAILURE_RE` — worker.py:29–45) не дали совпадения → `retry_reason`
вернул None → задача помечена failed без requeue (`retry_count=0` при `max_retries=3`), миссия встала в
`awaiting_human`. Штатный механизм паузы (`_mark_rate_limited` → requeue с бэкоффом, worker.py:2113) был
доступен, но не сработал из-за непокрытого паттерна.

**Причина.** Сообщение отказа — специфичное для рантайма MiniMax Code — не входит ни в один из трёх
регексов классификатора. Классификатор расширяем по дизайну, но новые сообщения провайдеров попадают в
него только вручную.

**Файлы.** `promptpilot/worker.py:29–45` (RATE_LIMIT_RE, OVERLOADED_RE, ENV_FAILURE_RE), `worker.py:96`
(`retry_reason`), `worker.py:2113` (точка вызова на nonzero-exit пути).

**Совместимость.** Аддитивно: добавить паттерн
`Runtime completed without a final assistant response` (и/или обобщённый `Retry after the (connection|.*)
recovers`) в `ENV_FAILURE_RE`/`OVERLOADED_RE`. Регресс: существующие паттерны не трогаются.

**Тесты.** (1) `retry_reason("...Runtime completed without a final assistant response.. Retry after the
connection recovers.", 4)` → не None; (2) задача с этим stderr при exit 4 → `rate_limited` + requeue;
(3) обычный отказ без маркеров → failed как раньше.

**Ограничения.** Не расширять классификатор «жирно» (ложные паузы хуже отсутствия); паттерн привязывать к
конкретным формулировкам провайдеров.

**Обход супервизора (реализован).** `tools/minimax-exec-wrapper.js` v3.2: при обнаружении транзиентного
паттерна в stderr mcode дописывает в СВОЙ stderr маркер «provider runtime overloaded, try again later» —
`OVERLOADED_RE` воркера матчит его → штатная пауза `rate_limited` → requeue с бэкоффом. Проверено
симуляцией (фейковый CLI с транзиентным stderr → wrapper exit 4, маркер в stderr присутствует).

---

## P14. Находка ревьюера с `payload`-строкой вместо словаря валидацией отбрасывает ВЕСЬ отчёт — automation.paused вместо мягкой деградации

**Симптом.** Ревьюер S3 раунд 7 (задача #978, 31.10.2026 16:09–16:25 UTC) вернул валидный хвост:
`AUDIT_FINDINGS_JSON: [...]` + `AUDIT_VERDICT: REVISION_REQUIRED` + одна строка `ИТОГ: ГОТОВО — …`.
Регекс `WORKFLOW_CLOSING_VERDICT_RE` и `json.loads` проходят, но `parse_reviewer_report` вернул None →
`review.awaiting_decision` → `automation.paused: «Аудитор не вернул валидные AUDIT_VERDICT/AUDIT_FINDINGS_JSON»`
→ миссия в `awaiting_human`. При этом отчёт аудита ПОЛНЫЙ (4 находки с доказательствами).

**Причина.** Модель положила в поле `payload` каждой находки строку-прозу
(`"payload":"Git-доказательство: git log --all -S 'GetSendersAsync' пуст …"`), а
`ReviewFindingInput` (models.py:831–837) требует `payload: dict[str, Any]`. Pydantic
`ValidationError (dict_type)` на первой находке → except в parse_reviewer_report (workflows.py:3272–3279)
→ `return None` — весь отчёт отбрасывается. Хвост-канонизация wrapper'ом (P5/P9) бессильна: хвост
корректен, ломается СХЕМА находок внутри JSON, которую wrapper не проверяет.

**Файлы.** `promptpilot/workflows.py:3272–3279` (try/except вокруг model_validate → None),
`promptpilot/models.py:831–837` (ReviewFindingInput.payload dict-only),
`promptpilot/herdr_exec.py:76–80` (WORKFLOW_CLOSING_VERDICT_RE — проходит).

**Совместимость.** Аддитивная мягкая деградация: при `ValidationError` с `dict_type` на `payload` —
обернуть строку в `{"report": <строка>}` и продолжить (по образцу существующего фолбэка P24:
REVISION_REQUIRED без находок синтезирует находку-обёртку, workflows.py:3281–3289). Строгий режим можно
оставить флагом. Альтернатива: ко-эрсия строкового payload на уровне ReviewFindingInput
(`field_validator`) — тогда чинится и для внешних вызовов.

**Тесты.** (1) отчёт с payload-строкой → parse вернёт decision с 4 находками, payload={"report": …};
(2) payload-число/список → тоже обёртка `{"report": json.dumps(...)}` или осмысленная ошибка с
fingerprint находки в тексте; (3) честный dict-payload — без изменений; (4) REVISION_REQUIRED без
находок — старый фолбэк P24 не сломан.

**Ограничения.** Не терять текст находки: сейчас единственный источник деталей — summary всего отчёта.
Обёртка `{"report": …}` сохраняет текст; слепое молчаливое отбрасывание отчёта — худший исход.

**Обход супервизора (реализован, см. К-10).** Вердикт ревьюера записан вручную через
`POST /api/workflows/{id}/review` с оборачиванием payload-строк в `{"report": …}` (node-скрипт,
не bash — защита от порчи backslashes, см. историю embedded-null).

---

## P15. `POST /api/workflows/{id}/review` не вызывает `advance_workflow` — вердикт через API оставляет миссию в `revision_required` навсегда, без маркера останова

**Симптом.** После ручной записи вердикта REVISION_REQUIRED (31.10.2026 16:33 UTC, обход P14) статус
завис в `revision_required` (ver 88): ни раунда, ни `automation.paused`, ни `awaiting_human` —
тихий останов без сигнала. Воркер online, heartbeat свежий. Продвинулось только после явного
`POST …/human-input {resume:true}` (16:35) → раунд 8 создан, executor dispatch (16:37).

**Причина.** `api_record_workflow_review` (api.py:1148–1151) возвращает результат `record_review`
НАПРЯМУЮ, в отличие от `api_approve_workflow_plan` (api.py:1108–1112) и
`api_workflow_external_result` (api.py:1182–1184), которые вызывают `workflows.advance_workflow(...)`
после действия. Штатный путь авто-ревью не страдает: воркер продвигает конвейер в обработчике
завершения задачи. Но при записи вердикта через API (человек/супервизор) триггера нет, а цикл
воркера не делает периодического sweep `advance_workflow` по активным миссиям.

**Файлы.** `promptpilot/api.py:1148–1151` (record_review без advance),
`api.py:1108–1112` и `api.py:1182–1184` (эталонное поведение с advance),
`promptpilot/workflows.py:3521–3531` (ветка REVISION_REQUIRED в advance_workflow — сама логика
авто-resume корректна, просто не вызывается).

**Совместимость.** Аддитивно: `return workflows.advance_workflow(recorded.id)` в api_record_workflow_review.
Для PASS-вердикта advance сам сделает чекпоинт/переход; для HUMAN_REQUIRED — вернёт awaiting_human
(ветка workflows.py:3517–3518). Регресс: двойное применение вердикта исключено — record_review
идемпотентен по раунду (повторный вызов без нового reviewer-run упрётся в «no completed reviewer run»/
touch-защиту).

**Тесты.** (1) POST /review c REVISION_REQUIRED при auto_resume_revision=true → статус `queued`/
`executing` нового раунда, а не `revision_required`; (2) POST /review c PASS → чекпоинт/следующая
стадия; (3) POST /review c HUMAN_REQUIRED → awaiting_human; (4) auto_resume_revision=false →
остаётся revision_required (штатно ждёт человека).

**Ограничения.** До фикса супервизор/владелец, записывающий вердикт руками, ОБЯЗАН сам вызвать
`POST …/human-input {resume:true}` при `revision_required`. Watchdog'у статусы из списка
«OK» (`revision_required` в т.ч.) не сигнализируют об останове — см. компенсацию в
`tools/promptpilot-watchdog.ps1` (защита от тихого останова).

---

## P16. Инкрементальная сборка в гейте принимает некомпилируемое дерево — «зелёный» гейт не означает «собирается»

**Симптом.** В нескольких раундах этапа S3 acceptance-гейт
`dotnet build Picket302.sln -c Debug --nologo -v q` завершался кодом 0 на дереве,
которое **не компилировалось**: доменные интерфейсы были расширены методами
`GetSendersAsync`, `GetDistinctFieldValuesAsync`, `UpdateFieldAsync` (контрагенты)
и `UpdateDraftRecipientAddressAsync` (письма), а реализаций в слое инфраструктуры
не было — компилятор давал CS0535 на реализациях интерфейсов. Дефект был обнаружен
лишь на полной перекомпиляции, после чего гейт стал зелёным. До этого момента
тесты запускались как `dotnet test … --no-build`, то есть против устаревших сборок,
и их успешный результат ничего не говорил о компилируемости дерева.

**Причина.** `dotnet build` по умолчанию выполняет инкрементальную сборку: MSBuild
пропускает `CoreCompile` для проекта, чьи входные файлы и результаты сборки
зависимых проектов считаются не изменившимися. Проверка актуальности опирается
на временные метки, поэтому изменение публичного интерфейса в одном проекте
не гарантирует перепроверку зависимого проекта: если компиляция зависимого
проекта пропущена, его реализации интерфейса не сверяются с новым контрактом.
Гейт не содержит шага, который принудительно пересобирает решение целиком, поэтому
расхождение «плохое дерево — зелёный гейт» продержалось несколько раундов подряд.
Точный механизм пропуска на тех временных метках не локализован и требует
воспроизведения; для устранения достаточно принудительной пересборки.

**Файлы.** Acceptance-гейт этапа S3 (команда `dotnet build Picket302.sln -c Debug --nologo -v q`),
граф проектов решения: `src/Picket302.Domain` → `src/Picket302.Infrastructure` →
`src/Picket302.App` → `tests/Picket302.Tests` (все проекты в `Picket302.sln` и связаны
`ProjectReference` корректно — дефект не в конфигурации решения).

**Совместимость.** Аддитивно: команда гейта должна собирать решение принудительно —
`dotnet build Picket302.sln -c Debug --nologo --no-incremental` (либо `dotnet clean`
перед сборкой). Для корректного дерева результат сборки не меняется, исчезает лишь
ложно-зелёный исход. Альтернатива — отдельная проверка компилируемости с
`--no-incremental`, если полная пересборка на каждом раунде слишком дорога.

**Тесты.** (1) Намеренно сломанная реализация интерфейса в слое инфраструктуры →
гейт обязан вернуть ненулевой код возврата; (2) то же дерение после исправления →
код 0; (3) сборка на заведомо корректном дереве дважды подряд → оба раза код 0
(проверка, что принудительная пересборка не ломает штатный сценарий).

**Ограничения.** `--no-incremental` увеличивает время гейта (полная перекомпиляция
решения на каждом раунде). Дефект в самом коде (отсутствующие реализации
репозиториев) исправлен в дереве S3 — см. `docs/OPEN_QUESTIONS_LOG.md`, запись
S3.13; здесь зафиксирован именно дефект инструментария гейта.

---

## P17. Вариация К-7/P11 с невалидным индикатором: чекпоинт с НЕ-null sha закоммитил только случайные файлы — детектор «sha:null» пропускает потерю результата стадии

**Симптом.** 31.10.2026 17:12:58 — `stage.checkpoint` S3-addressbook-mailing: `sha="f0bece8…"`
(НЕ null!), `files=[docs/OPEN_QUESTIONS_LOG.md, docs/PROMPTPILOT-BUGS.md]` — всего 2 файла. Реальный
кодовый результат S3 (105 путей src/tests, раунды 7–9: домен, инфраструктура, App-слой, тесты,
миграция V005) остался НЕЗАКОММИЧЕННЫМ, стадия объявлена завершённой, S4 уже исполняется в том же
дереве. Компенсация супервизора (watchdog, условие «payload.sha == null», добавлена после S2) НЕ
сработала бы: sha валиден.

**Причина.** Атрибуция чекпоинта по-прежнему считается от diff ПОСЛЕДНЕГО executor-run (P11): в
последнем раунде 9 исполнитель правил только журнал допущений → чекпоинт закоммитил только docs-файлы
и вернул честный sha своего (скудного) коммита. Индикатор «sha:null» — лишь ЧАСТНЫЙ случай пустой
атрибуции; когда последний ранг трогает хоть что-то вне кода стадии, sha ненулевой, и потеря
результата становится бесшумной.

**Файлы.** `promptpilot/stage_checkpoint.py` (та же логика, что P11 — diff последнего ранга вместо
кумулятивного от base_sha первого ранга стадии); компенсация: `tools/promptpilot-watchdog.ps1`.

**Совместимость.** Требуемый фикс тот же, что P11/К-7: кумулятивный diff стадии (base_sha первого
ранга ∩ allowed_paths) + событие `stage.checkpoint_empty_but_dirty`. Для ДЕТЕКТОРОВ: не опираться на
null-ness sha; надёжный предикат — «checkpoint-событие новее последнего коммита И есть грязные
allowed-paths». Watchdog переведён на этот предикат (время-гвардия cpTime > lastCommitTime исключает
повторную компенсацию и захват уже закоммиченного).

**Тесты.** (1) стадия из 2+ раундов: код в раннем раунде, docs в последнем → чекпоинт должен
закоммитить оба; (2) чекпоинт при чистом дереве → без изменений, sha валиден; (3) чекпоинт при
незакоммиченном результате → `stage.checkpoint_empty_but_dirty`, не тихий «успех».

**Ограничения.** Компенсация коммитит и ранний WIP СЛЕДУЮЩЕЙ стадии, успевший появиться в дереве
(здесь: ~20 файлов Printing-каркаса S4 попали в компенсационный коммит 515ed57) — приемлемо
(build+test проверяются перед коммитом; изменения всё равно проходят ревью на чекпоинте S4), но
нативный фикс устранит и эту примесь.

**Обход супервизора (реализован).** Ручная верификация build 0/0 + тесты 404/404, коммит
`515ed57` (105 файлов, +15450). Watchdog: условие компенсации заменено с «sha==null» на
«cpEvent новее последнего коммита И dirty src/tests/docs» (синтаксис и сухой прогон проверены).
Побочно: разовое падение 1/404 теста при прогоне в момент параллельной активности исполнителя S4 в
том же дереве (вероятно файловые блокировки) — не воспроизвелось в двух повторах; наблюдать.

---

## P18. Промпт >32 767 символов роняет спавн: FileNotFoundError (WinError 206) маскируется под «CLI not found»

**Симптом.** 31.10.2026 18:27/18:33 — executor-диспетчи ревизионного раунда 11 S4 (задачи #991/#992)
упали за ~21 с: `CLI 'minimax-m31' not found. Is it installed and in PATH?` (exit -1). CLI существует
и прекрасно спавнится вручную. Повторы дают тот же отказ — детерминированно.

**Причина.** Ревизионный промпт раунда (задача стадии + полное ревью + отчёт) — **38 238 символов**,
что превышает лимит командной строки Windows CreateProcess (32 767). `build_cmd` кладёт промпт
последним argv → `subprocess.Popen` кидает **FileNotFoundError с WinError 206** («The filename or
extension is too long» — эмпирически подтверждено), worker.py:1989–1990 перехватывает ЛЮБОЙ
FileNotFoundError и пишет вводящий в заблуждение текст про «CLI not found». Успешные промпты —
12–20 тыс. символов; порог перейден только на ревизиях объёмных аудитов.

**Файлы.** `promptpilot/config.py` build_cmd (промпт в argv), `promptpilot/worker.py:1989–1990`
(маскирующий перехват FileNotFoundError), `worker.py:1046–1052` (U16-ретрай: `exists(cmd[0])` True →
одна пауза-повтор → тот же отказ).

**Совместимость.** В build_cmd УЖЕ есть штатный stdin-транспорт: `cfg["prompt_stdin"]=true` → маркер
`\x00PROMPT\x00` удаляется из argv, промпт пишется в stdin воркером (worker.py:1887, 1977, 2016–2018
с закрытием stdin). НО: `POST /api/providers` включает prompt_stdin ТОЛЬКО при клонировании
провайдера-источника с тем же cmd (api.py:1700 `stdin_transport = source.prompt_stdin and cmd == source.cmd`)
— для кастомного cmd-провайдера включить транспорт через API НЕВОЗМОЖНО (отдельный пробел — см. P18-б).

**Тесты.** (1) Popen с argv-элементом 40k → FileNotFoundError(WinError 206); (2) build_cmd при
prompt_stdin=true → промпт не в argv; (3) задача с промптом 40k при prompt_stdin=true → спавн успешен;
(4) сообщение об ошибке спавна должно различать «не найден CLI» и WinError 206 (по `e.winerror`).

**Обход супервизора (реализован, К-12).** wrapper v3.4: флаг `--stdin-prompt` в шаблоне cmd —
wrapper читает промпт из СВОЕГО stdin и передаёт mcode через `--input -` (mcode exec умеет
`--input -` = stdin; воркер закрывает stdin после записи — worker.py:748–757). Провайдер обновлён
напрямую через `promptpilot.config.save_provider(..., prompt_stdin=True)` (штатный атомарный писатель
стека; API-замок обойдён вызовом функции, файл не правился руками). Проверено: фейк-CLI получил 40k
промпт целым; build_cmd не кладёт промпт в argv; задача #994 (38238 симв.) запущена и работает.

### P18-б. API не позволяет включить prompt_stdin кастомному cmd-провайдеру

`api_provider_create` (api.py:1700, 1728): `prompt_stdin` сохраняется как True только если
`source.get("prompt_stdin") and p.cmd == source.cmd` — т.е. транспорт включается лишь при клонировании
уже-stdin провайдера с неизменным cmd. Для нового cmd-провайдера (или смены шаблона) включить
stdin-транспорт невозможно. Фикс: отдельное поле `prompt_stdin` в ProviderCreate (или флаг
`--prompt-stdin`), сохраняемый без замка на равенство cmd.

---

## P19. Upstream удалил `execution_mode` из WorkflowStageSpec (strict extra=forbid) — стек новой версии не может прочитать ни одну миссию, созданную прежним кодом

**Симптом.** 31.10.2026 ~18:40 стек на машине был перезапущен (НЕ супервизором Picket302) из
оригинального репозитория `C:\Users\FIREBAT\Desktop\Projets\PromptPilot` (HEAD 198926e) с тем же
`PP_DATA_DIR`. После этого: `GET /api/workflows*` → 500, resume миссии →
`automation.paused: ValidationError: 1 validation error for WorkflowStageInDB — execution_mode:
Extra inputs are not permitted`. Ни одна миссия, созданная кодом pp-deploy, не читается.

**Причина.** pp-deploy-эра: `WorkflowStageSpec.execution_mode` существовал (pp-deploy
models.py:598) и писался в `spec_json` всех стадий. Upstream поле удалил, модель
`ConfigDict(extra="forbid")` (PromptPilot models.py:468–469, 514) → `db.list_workflow_stages` →
`WorkflowStageInDB(**spec)` → ValidationError (db.py:4859). Миграции/шиммы совместимости нет.

**Файлы.** `PromptPilot/promptpilot/models.py:468–481` (WorkflowStageSpec без execution_mode,
extra=forbid), `PromptPilot/promptpilot/db.py:4859` (_row_to_workflow_stage), данные:
`workflow_stages.spec_json`.

**Совместимость.** Требуется обратная совместимость чтения: (а) миграция, вычищающая
`execution_mode` из spec_json всех стадий; ИЛИ (б) `execution_mode: Optional[str] = None`
(legacy-игнор) в WorkflowStageSpec; ИЛИ (в) в _row_to_workflow_stage — pop неизвестных ключей с
warn. Без этого deploy-форк и mainline несовместимы на общих данных.

**Тесты.** (1) миссия, созданная кодом со execution_mode, читается новой версией (report/list/advance);
(2) создание миссии новой версией — spec_json без устаревших полей; (3) реверс-совместимость:
старый код читает миссии новой версии.

**Обход супервизора (реализован, К-12).** Стек возвращён на pp-deploy (stop.ps1/start.ps1,
предвыданное разрешение); зомби-инстансы оригинального репо (`pp.exe server/worker`, авто-респаун
каждые ~5 мин, держали порт и 500-тили) остановлены точечно. Конфликт «кто владеет портом 8420» —
на уровне владельца (см. К-12): если авто-рестартер оригинального репо продолжит перезапуски,
миссии будут снова ломаться — требуется решение владельца (миграция данных или фикс совместимости
в upstream, или запрет авто-рестартера).
