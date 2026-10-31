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
