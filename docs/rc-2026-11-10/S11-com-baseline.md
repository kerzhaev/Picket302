# RC S11 — Чистый COM baseline

Дата: 2026-11-12 (прогон 22:24 UTC). Код: `46794c1` (worktree rc-20261110),
Release x64 сборка ComAutomation 0/0. Тихое окно подтверждено: процессов
`Picket302.ComAutomation.exe` нет до и после.

## 1. Baseline реестра (до прогона, evidence/com-s11/baseline-registry.json)

| Ключ | HKCU | HKLM | HKCR |
|---|---|---|---|
| `PEPro.Application` (ProgID) | отсутствует | отсутствует | — |
| `CLSID\{5715286E-F98F-4C1C-AC86-AB943AFAE004}` | отсутствует | отсутствует | отсутствует |
| `AppID\{5715286E-…}` | отсутствует | отсутствует | — |

Состояние ЧИСТОЕ (проверено `reg query` — однозначные пути; ранний
PowerShell-замер `Test-Path` дал ложноположительный результат из-за
экранирования `$clsid` в bash-heredoc — отброшен, baseline перезаписан
корректным reg.exe-замером). Уточнение по ревью F-W3-2: `capturedUtc`
файла — момент повторного reg.exe-замера (после отбраковки Test-Path);
фактическое ПРЕДсостояние прогона независимо доказано полем
`pre.registrationPresent=false` в `cold-s11-cold1.json`. Пользователь `Nachfin`, OS AMD64, .NET 8.0.420.
Модель регистрации — per-user `HKCU\Software\Classes` (ComRegistration.cs,
ТЗ §9.3 функция 114), прав администратора не требует.

## 2. Холодная активация (create4, evidence/com-s11/cold-s11-cold1.json)

Сервер — изолированная копия Release-сборки RC в
`Temp\p302-rc-com-<guid>` (SHA exe `C54D303B01E2B924…`); регистрация
штатным `--register` из копии; снятие в `finally` штатным `--unregister`.

| Шаг | Результат |
|---|---|
| `--register` exit | 0 |
| LocalServer32 | путь к изолированной копии (не к worktree) |
| `CLSIDFromProgID` | S_OK, CLSID совпадает |
| HKCR CLSID/ProgID видимы in-process | True |
| **Cold `CoCreateInstance` CLSCTX_LOCAL_SERVER(0x4)** | **hr=0x00000000 за 158 мс, instance created and released** |
| PID сервера | 93176 |
| `-Embedding` в дельте журнала | есть, `cwd=C:\WINDOWS\system32` (признак запуска SCM) |
| `CoRegisterClassObject` | hresult=0x00000000, cookie=16, pid=93176 |
| IDispatch на работающем сервере (dispatch-проба) | вызовы выполнены |
| Новые DCOM-события | нет |
| Остановка | штатная, `Local\Picket302.ComAutomation.Shutdown.93176` → GRACEFUL_EXIT_0 (CoRevoke) |

## 3. Очистка (postcondition)

`--unregister` exit 0; ProgID/CLSID/AppID исчезли (reg.exe-замер после
прогона: все false); процессов сервера не осталось; temp-каталог удалён.
Профиль пользователя: изменялся только append-only `com-automation.log`
(ожидаемо); `connection.json`/`picket302.db` не тронуты (методы БД не
вызывались). Kill не применялся.

## 4. Вывод

Прямой путь (регистрация + запуск) и холодная активация из документированного
чистого состояния работают на текущем коде RC. Симптом Stage4
(0x80040154 за ~43 мс без запуска) НЕ воспроизводится — согласуется с
выводом stage5-verify (категория B′: загрязнение SCM после kill без
CoRevoke, а не дефект приложения). Per-PID shutdown-событие и graceful
revoke уже реализованы; изменений кода для холодной активации не требуется.

```text
COM_DIRECT_VERIFIED=true (RC 46794c1, живой прогон)
COM_COLD_VERIFIED=true (1/1 на RC; полная приёмка 5×+UNC — S14)
COM_FULL_ACCEPTANCE_VERIFIED=false (PrintEnvelope через COM с записью в БД — S14)
```

Артефакты: evidence/com-s11/{baseline-registry.json, cold-s11-cold1.json,
probe-s11-cold1-create4.out, probe-s11-cold1-dispatch.out}. Клиент-проба —
x64 .NET 8 `P302ComProbe` (scratch `Temp\p302-rc-com-20261112\comprobe`,
исходник от проверенной серии stage5).
