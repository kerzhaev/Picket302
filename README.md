# Picket302

Настольное Windows-приложение для подготовки, вёрстки, верификации, массовой печати
и регламентного учёта почтовых отправлений, стандартных конвертов, сопроводительных
бланков и реестров.

Требуемые документы: `SPEC_INPUT/IMPLEMENTATION_SPEC.md` (техническое задание),
`00_FUNCTION_INDEX.txt` (перечень 163 функций с приоритетами),
`EVIDENCE/` (подтверждающие выжимки, только для чтения).

> Текущее состояние репозитория — **этап S1 (каркас решения)**: собираемый скелет
> решения, проекты слоёв, скелет тестов, зелёные `dotnet build` и `dotnet test`.
> Карта слоёв и правила зависимостей — `docs/ARCHITECTURE.md`.

## Требования

- Windows 10/11 x64;
- .NET SDK 8.0.100 или новее (требования зафиксированы в `global.json`,
  допускается любой патч уровня 8.0.x);
- Visual Studio 2022 (17.x) — для разработки; сборка из командной строки не требует VS.

## Структура решения

```
Picket302/
├── Picket302.sln                  решение (платформа решения — x64)
├── global.json                    версия SDK
├── Directory.Build.props          общие параметры сборки: C# 12, nullable, implicit usings, x64
├── Directory.Packages.props       версии пакетов NuGet (Central Package Management)
├── .editorconfig                  соглашения оформления и правила анализа
├── docs/
│   ├── ARCHITECTURE.md            карта слоёв и допустимые зависимости (ТЗ §2.1, §2.2)
│   └── OPEN_QUESTIONS_LOG.md      допущения, принятые при реализации
├── src/
│   ├── Picket302.Domain/          доменный слой: сущности и инварианты, без зависимостей
│   │   ├── Common/Result.cs       результат доменной операции
│   │   └── ValueObjects/Address.cs адрес «индекс + 6 строк» (ТЗ §3.1.1, функция 4)
│   ├── Picket302.Application/     прикладные сценарии
│   ├── Picket302.Infrastructure/  СУБД (SQLite/PostgreSQL), файлы, сеть
│   ├── Picket302.Printing/        движок печати: GDI+ / winspool
│   └── Picket302.App/             оболочка WinForms: главное окно оператора
│       ├── MainShell.cs           главное окно, схема «3+2» (ТЗ §7.1)
│       └── Program.cs             точка входа
└── tests/
    └── Picket302.Tests/           юнит-тесты (xunit)
        ├── App/                   каркас главного окна: состав меню и шрифт оболочки
        ├── Architecture/          контроль допустимых зависимостей между слоями
        ├── Domain/                правила домена (адрес: индекс + 6 строк)
        └── Infrastructure/        стек «SQLite + Dapper»
```

Подробное описание слоёв и правил зависимостей — `docs/ARCHITECTURE.md`.

## Сборка

```powershell
dotnet restore Picket302.sln
dotnet build Picket302.sln
```

Сборка всех проектов решения выполняется в конфигурации `Debug` (или `Release`)
и в платформе `x64`; выходные файлы — `bin\x64\<Конфигурация>\...`.

## Тесты

```powershell
dotnet test Picket302.sln
```

Тестовый проект `tests/Picket302.Tests` ссылается на все проекты решения.
Состав тестов на текущем этапе:

- `Domain/AddressTests.cs` — доменное правило «почтовый индекс + 6 адресных строк»:
  создание адреса, отказ при неверном индексе, отказ при числе строк, отличном от 6,
  обрезка строки длиннее 255 знаков с предупреждением;
- `App/MainShellMenuTests.cs` — каркас главного окна: семь верхних пунктов меню
  по §7.1 и плотный шрифт оболочки 8 pt Tahoma;
- `Architecture/LayerDependencyTests.cs` — тест проекта ссылается на все сборки решения,
  ни один слой не ссылается на сборку вне допустимых зависимостей, доменный слой
  не зависит ни от кого;
- `Infrastructure/SqliteStackTests.cs` — стек «SQLite + Dapper» на базе в памяти.

## Запуск приложения

```powershell
dotnet run --project src\Picket302.App\Picket302.App.csproj
```

Либо напрямую: `src\Picket302.App\bin\x64\Debug\net8.0-windows\Picket302.App.exe`.
При старте открывается главное окно оператора со строкой меню
«Файл / Правка / Вид / Сервис / Печать / Рассылки / Справка» (ТЗ §7.1).

## Зависимости

Все версии пакетов вынесены в `Directory.Packages.props` и применяются централизованно:

| Пакет | Версия | Назначение | Слой |
|---|---|---|---|
| `Microsoft.Data.Sqlite` | 8.0.33 | SQLite 3 — локальный/переносимый режим | Infrastructure |
| `Dapper` | 2.1.66 | доступ к данным репозиториев | Infrastructure |
| `System.Drawing.Common` | 8.0.33 | GDI+ / `System.Drawing.Printing` | Printing |
| `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` | 2.9.3 / 2.8.2 / 17.14.1 | тесты | Tests |

## Правила оформления кода

- C# 12, .NET 8, Windows x64;
- `nullable` включён, `ImplicitUsings` включён;
- интерфейсные строки, сообщения домена и комментарии предметной области — на русском языке;
- версии пакетов указываются только в `Directory.Packages.props`;
- тесты — xunit, имена тестов на русском языке.
