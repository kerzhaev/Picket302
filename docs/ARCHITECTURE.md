# Архитектура Picket302

Краткая карта слоёв и допустимых зависимостей решения.

Источник требований: `SPEC_INPUT/IMPLEMENTATION_SPEC.md`
(гл. 2 «Архитектура программного комплекса», §2.1 «Слои системы и допустимые зависимости»,
§2.2 «Архитектурные инварианты», §10.1 «Стек и версии»).

Приложение строится по принципам Domain-Driven Design, чистой слоистой архитектуры
(Clean Architecture) и инверсии зависимостей.

## 1. Слои

| Проект | Слой | Целевая платформа | Ответственность (ТЗ §2.1) |
|---|---|---|---|
| `src/Picket302.Domain` | Доменный | `net8.0` | Сущности (`Counterparty`, `Mail`, `Group`, `BlankField`), агрегаты (`Distribution`), правила целостности, инварианты адресации |
| `src/Picket302.Printing` | Печать | `net8.0-windows` | Двухэтапный валидатор метрик (metrics против DEVMODE), рендерер GDI+/DirectWrite, генератор кодового штампа, кэш растровых QR-кодов |
| `src/Picket302.Application` | Прикладных служб | `net8.0-windows` | Сценарии рассылок, конвейеры импорта/экспорта, управление печатными сессиями, калькулятор тарифов, диспетчер COM-автоматизации (`IPEProApplication`) |
| `src/Picket302.Infrastructure` | Инфраструктуры и данных | `net8.0-windows` | Репозитории (PostgreSQL / SQLite), файловый процессор бланков `.lbk`/ZIP, экспорт OpenXML/XSLT, адаптеры периферии, защищённое хранилище секретов |
| `src/Picket302.App` | Представления | `net8.0-windows` (WinForms) | Оболочка оператора: главное окно «3+2», визуальный дизайнер бланков, контроллеры диалогов и мастеров |
| `tests/Picket302.Tests` | Тесты | `net8.0-windows` | Юнит-тесты доменных правил и инфраструктурного стека (xunit) |

Порядок строк в таблице — от нижних слоёв к верхним. Оболочка и слой печати
собираются только под Windows: печать опирается на `System.Drawing.Printing` и
`winspool.drv` (§10.1). Доменный слой остаётся платформенно-независимым (`net8.0`).

## 2. Допустимые зависимости

Стрелка «A → B» означает: слой A вправе ссылаться на сборку B.

```
App ─────────────► Application ─────────► Domain
 │                       │                 ▲
 ├──────────► Infrastructure ─────────────┘
 └──────────► Printing ──────────────────►┘
```

| Слой | Может ссылаться только на |
|---|---|
| `Picket302.Domain` | — (зависимостей нет) |
| `Picket302.Printing` | `Picket302.Domain` |
| `Picket302.Application` | `Picket302.Domain`, `Picket302.Printing` |
| `Picket302.Infrastructure` | `Picket302.Domain`, `Picket302.Printing` |
| `Picket302.App` | `Picket302.Application`, `Picket302.Domain`, `Picket302.Infrastructure`, `Picket302.Printing` |
| `Picket302.Tests` | все проекты решения |

Обратные и «поперечные» зависимости запрещены:

- доменный слой не знает ни о приложении, ни о базе, ни о печати;
- слой инфраструктуры не вызывает прикладные сценарии (композиция собирается в `App`);
- слой печати не зависит от прикладного слоя;
- слой представления не содержит доменных правил — только вызовы сценариев.

Матрица зависимостей проверяется автотестом
`tests/Picket302.Tests/Architecture/LayerDependencyTests.cs`: тест читает реальные
ссылки собранных сборок и отклоняет любую ссылку вне таблицы.

## 3. Архитектурные инварианты (ТЗ §2.2)

1. **Изоляция слоя запросов.** Ни один исполняемый SQL-запрос для отчётов и бланков не
   зашивается в C#-код бинарных сборок: SQL-проекция хранится открытым текстом внутри
   структуры описателя отчёта/бланка (`report.xml` / `blank.xml`).
2. **Файловая независимость макетов.** Шаблоны бланков — версионируемые ZIP-пакеты `.lbk`
   в конфигурируемом файловом хранилище; БД хранит только метаданные, геометрию полей
   и относительные пути.
3. **Документируемость печати.** Слой печати не может отправить данные в `winspool.drv`
   в обход создания транзакции аудита в таблице `PrintJob`.
4. **Отказоустойчивость COM-интерфейса.** Исключение в COM-вызовах не завершает процесс
   сервера: ошибки транслируются в коды HRESULT и флаги `GetWarningType()`.

## 4. Соглашение об именах

Проекты и сборки называются по схеме `Picket302.<Слой>`, пространства имён — по схеме
`Picket302.<Слой>.<Подсистема>`. В ТЗ классы адресуются относительными путями
(`Domain.ValueObjects.Address`); таблица ниже задаёт соглашение на будущие классы
(на этом этапе реализован только `Picket302.Domain.ValueObjects.Address`):

| Класс в ТЗ | Полное имя в решении | Путь по соглашению |
|---|---|---|
| `Domain.ValueObjects.Address` | `Picket302.Domain.ValueObjects.Address` | `src/Picket302.Domain/ValueObjects/Address.cs` |
| `Domain.Entities.Counterparty` | `Picket302.Domain.Entities.Counterparty` | `src/Picket302.Domain/Entities/Counterparty.cs` |
| `Application.Services.*` | `Picket302.Application.Services.*` | `src/Picket302.Application/Services/*.cs` |
| `Infrastructure.Persistence.*` | `Picket302.Infrastructure.Persistence.*` | `src/Picket302.Infrastructure/Persistence/*.cs` |
| `UI.Forms.*` | `Picket302.App.Forms.*` | `src/Picket302.App/Forms/*.cs` |

Подсистемы внутри слоя: `Entities`, `ValueObjects`, `Common` — в домене;
`Services`, `Contracts` — в прикладном слое; `Persistence`, `Files`, `Security` — в инфраструктуре;
`Rendering`, `Metrics`, `Codes` — в слое печати; `Forms`, `Controls` — в оболочке.

Допущения по этому пункту зафиксированы в `docs/OPEN_QUESTIONS_LOG.md`.

## 5. Управление зависимостями

Версии пакетов вынесены в `Directory.Packages.props` (Central Package Management),
общие параметры сборки — в `Directory.Build.props`:

| Параметр | Значение | Основание |
|---|---|---|
| `LangVersion` | `12` | §10.1 |
| `Nullable` | `enable` | требование стадии |
| `ImplicitUsings` | `enable` | требование стадии |
| `InvariantGlobalization` | `false` | требование стадии (русская локализация, §10.8) |
| `Platforms` / `PlatformTarget` | `x64` | §10.1 (Windows x64) |
| `global.json` | SDK 8.0.100+, `rollForward: latestFeature` | §10.1 (.NET 8) |
