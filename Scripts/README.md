# Примеры вызова COM-сервера автоматизации Picket302

Примеры из ТЗ §8.9, функция 115: вызов локального COM-сервера автоматизации
из сценария Windows и из Excel.

| Файл | Назначение |
|------|------------|
| `printenvelope.js` | JScript (cscript): создание объекта `PEPro.Application`, чтение настроек печати из реестра, печать конверта, вывод кода результата. |
| `printregistry.vbs` | VBScript (cscript/wscript): чтение настроек печати из реестра, печать почтового реестра из XML-строки (метод 8 контракта), вывод кода результата. |
| `ExcelCallExample.bas` | Модуль VBA для Excel: `Option Explicit`, `CreateObject("PEPro.Application")`, вызовы `PrintEnvelope` и `PrintFromXmlRegistersWithoutForm`. |
| `ExcelCallExample.xls` | Книга Excel с листом «Пример»: текст модуля VBA и коды результата. |

## Подготовка

1. Зарегистрировать COM-сервер в ветке пользователя (права администратора
   не требуются):

   ```
   Picket302.ComAutomation.exe --register
   ```

   Регистрация создаёт ключи в `HKCU\Software\Classes`: `PEPro.Application`
   (ProgID), `CLSID\{5715286E-F98F-4C1C-AC86-AB943AFAE004}` (класс и
   `LocalServer32`), `AppID\{5715286E-F98F-4C1C-AC86-AB943AFAE004}`,
   `Interface\{3A98FA89-27EE-4DDF-8F5D-ABC47EA942E6}` и
   `TypeLib\{22BC298D-DF0E-3C29-B589-7A50D3A8B871}\2.2`.

   Удаление регистрации: `Picket302.ComAutomation.exe --unregister`.

2. Заполнить настройки печати в ветке `HKCU\Software\Picket302\PrintEnvelope`:
   `FromName`, `FromAddr`, `FromIndex`, `ToName`, `ToAddr`, `ToIndex`.
   Отсутствующий параметр читается как пустая строка и не порождает
   исключения (ТЗ §9.2).

## Запуск примеров

```
cscript //nologo Scripts\printenvelope.js
cscript //nologo Scripts\printregistry.vbs
cscript //nologo Scripts\printregistry.vbs --load
cscript //nologo Scripts\printregistry.vbs путь\к\файлу.xml
cscript //nologo Scripts\printregistry.vbs --load путь\к\файлу.xml
```

Параметр `--load` у скрипта `printregistry.vbs` дополнительно вызывает
`SafeLoadDatabase` — операции с базой требуют загруженной базы (код 1 иначе).
Путь к файлу XML необязателен: если он не задан, печатается встроенный
пример данных в формате `RegistersData`.

В Excel:

1. `ALT+F11` → `File` → `Import File...` → выбрать `Scripts\ExcelCallExample.bas`;
2. запустить макрос `PrintEnvelopeFromRegistry` (или `PrintEnvelopeSample`,
   либо `PrintRegisterToFile`).

## Коды результата (GetWarningType)

| Код | Значение |
|-----|----------|
| 0 | Печать реестра или получение списка шаблонов завершилось без ошибок. |
| 1 | База данных не загружена либо недоступна. |
| 2 | XML повреждён или не соответствует контракту обмена (ТЗ §9.4). |
| 3 | Ошибка подготовки документа (документ не удалось сформировать). |
| 4 | Файл шаблона не найден. |

Все методы контракта возвращают `S_OK`; ошибка операции кодируется значением
`GetWarningType`.

## Ограничения

* **Формат `ExcelCallExample.xls`.** Книга сохранена как Excel 2003 XML
  Spreadsheet (SpreadsheetML) с расширением `.xls`: бинарный формат BIFF8 с
  внедрённым проектом VBA невозможно создать без установленного Office.
  Excel открывает файл, при этом сообщает о несовпадении расширения и формата
  — это ожидаемо. Расширение `.xls` оставлено, чтобы пример открывался
  двойным щелчком в любой установленной версии Excel. Импорт модуля VBA
  выполняется из `ExcelCallExample.bas` (см. инструкцию на листе «Пример»).
* **Библиотека типов.** Бинарный файл `PrintEnvelopePro.tlb` собран
  компилятором MIDL из описания `src/Picket302.ComAutomation/idl/PrintEnvelopePro.idl`
  и поставляется вместе с программой. Регистрация ветки
  `TypeLib\{22BC298D-...}\2.2` (для 32- и 64-разрядных клиентов) и ссылки
  `TypeLib` в ветке интерфейса выполняется кнопками «Зарегистрировать COM-сервер»
  и «Отменить регистрацию» на странице интеграции настроек. Повторная сборка
  описания — `tools/build-typelib.ps1`.
* **Кодировка.** Файлы `printenvelope.js` и `printregistry.vbs` сохранены в
  UTF-16 LE с BOM — Windows Script Host читает такие файлы как Unicode на
  любой локали. UTF-8 с BOM использовать нельзя: движок сценариев читает файл
  в однобайтовой кодировке и встречает метку порядка байтов как символы,
  из-за чего файл не компилируется («JavaScript compilation error»).
  Модуль `ExcelCallExample.bas` сохранён в UTF-8 без BOM; редактор VBA
  читает файл в однобайтовой кодировке системы, поэтому при импорте на
  русской локали текст отображается корректно, а на иной локали его следует
  пересохранить в локальной однобайтовой кодировке (обычное сохранение в
  «Блокноте»).
