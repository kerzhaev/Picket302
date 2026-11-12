# RC S15 — Поставочная COM-модель

Дата: 2026-11-12. Код `b78c97b` (RC), Release x64. Выделенный цикл
регистрации/удаления на чистом состоянии (evidence/com-s15/regclean.json,
скрипт regclean-run.ps1 приложен).

## 1. Модель регистрации и соответствие поставке

Фактическая поставка в репозитории — **portable COM-сервер**
`Picket302.ComAutomation.exe` с ключами `--register` / `--unregister`
(`Program.cs`), плюс USB-развёртывание (`UsbInstaller`, ТЗ §10.6 — копирование
файлов/БД, без COM-регистрации). Полноценного машинного установщика
(MSI/Inno/WiX) в репозитории **нет**.

Регистрация — **per-user `HKCU\Software\Classes`** (`ComRegistration`,
ТЗ §9.3 функция 114), прав администратора не требует. Это **соответствует**
portable-поставке и правам обычного пользователя. Подтверждено циклом:
`isAdmin=False`, регистрация успешна.

```text
COM_DELIVERY_MODEL=HKCU per-user (matches portable delivery; no admin required)
HKLM_MACHINE_WIDE=not implemented; requires installer + admin → OWNER_DECISION (см. §4)
```

## 2. Цикл регистрации/удаления (regclean.json)

| Шаг | Результат |
|---|---|
| Предсостояние | все 5 ключей отсутствуют, процессов нет |
| `--register` exit | 0 |
| Созданные ключи (HKCU\Software\Classes) | `PEPro.Application`, `CLSID\{5715286E-…}`, `AppID\{5715286E-…}`, `Interface\{3A98FA89-…}`, `TypeLib\{22BC298D-…}` — все 5 present |
| LocalServer32 | путь к изолированной копии exe |
| `--unregister` exit | 0 |
| Ключи после удаления | все 5 **absent** |
| Процессов осталось | 0 |
| temp-каталог | удалён |
| isAdmin / user / arch | False / Nachfin / AMD64 |

**Вывод:** регистрация/удаление на чистом состоянии не оставляют ключей и
процессов; модель HKCU работает без админ-прав. Критерий S15 «без оставшихся
ключей и процессов» доказан.

## 3. Инструкции восстановления

**Штатная регистрация / снятие** (из каталога сервера):
```
Picket302.ComAutomation.exe --register
Picket302.ComAutomation.exe --unregister
```

**Если регистрация повреждена / orphaned-ключи** (например, после жёсткого
kill или смены пути exe):
1. Снять штатно: `Picket302.ComAutomation.exe --unregister` (идемпотентно).
2. Если ключи остались — удалить вручную ветки HKCU (только свои, НЕ HKLM):
   - `HKCU\Software\Classes\PEPro.Application`
   - `HKCU\Software\Classes\CLSID\{5715286E-F98F-4C1C-AC86-AB943AFAE004}`
   - `HKCU\Software\Classes\AppID\{5715286E-F98F-4C1C-AC86-AB943AFAE004}`
   - `HKCU\Software\Classes\Interface\{3A98FA89-27EE-4DDF-8F5D-ABC47EA942E6}`
   - `HKCU\Software\Classes\TypeLib\{22BC298D-DF0E-3C29-B589-7A50D3A8B871}`
3. Перерегистрировать из актуального каталога: `--register`.

**Если сервер «завис» / не активируется холодно** (симптом загрязнения SCM):
1. НЕ kill. Остановить штатно per-PID событием
   `Local\Picket302.ComAutomation.Shutdown.<pid>` (дождаться выхода +
   `CoRevokeClassObject` в журнале).
2. Если процесс уже мёртв без revoke — снять регистрацию (`--unregister`),
   убедиться что процессов `Picket302.ComAutomation.exe` нет, перерегистрировать.
   Это очищает кэш SCM (категория B′, см. S12/S13).

**Диагностика:** журнал `%AppData%\Picket302\com-automation.log`
(append-only, UTF-8): строки `Запуск … -Embedding`, `CoRegisterClassObject
hresult=…`, `CoRevokeClassObject выполнен …`. Проверка регистрации:
`reg query "HKCU\Software\Classes\CLSID\{5715286E-F98F-4C1C-AC86-AB943AFAE004}\LocalServer32"`.

## 4. Решение владельца (HKCU vs HKLM)

Текущая модель HKCU per-user соответствует portable-поставке и не требует
админ-прав. **Машинная установка (HKLM, для всех пользователей)** потребовала
бы: (а) полноценного установщика (в репозитории отсутствует), (б) прав
администратора, (в) регистрации в `HKLM\Software\Classes`. Это **не
реализовано и не выполнялось самовольно** — оформляется как решение владельца
(вместе с выбором типа установки). До решения поставочная модель остаётся
HKCU per-user.

## 5. Статус

```text
COM_DELIVERY_REGISTRATION_CLEAN=true (register/unregister, 0 keys/0 procs left, no admin)
COM_RECOVERY_INSTRUCTIONS=documented (§3)
COM_HKCU_HKLM_DECISION=OWNER_DECISION_REQUIRED (machine-wide install not implemented)
```

S15 критерий «регистрация/удаление на чистой среде без оставшихся ключей и
процессов; инструкции восстановления; модель соответствует поставке и правам»
— выполнен для HKCU-модели; HKLM-вариант — решение владельца.
