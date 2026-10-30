ТЕХНИЧЕСКОЕ ЗАДАНИЕ (СПЕЦИФИКАЦИЯ РЕАЛИЗАЦИИ)
Система подготовки и пакетной печати почтовой корреспонденции и бланков отправлений
Целевой артефакт: SPEC_INPUT\IMPLEMENTATION_SPEC.md

Версия документа: 1.0.0-PROD

Статус: Утверждено к разработке

Архитектурный стандарт: .NET 8 (Windows x64), C# 12, PostgreSQL 16 / SQLite 3

1. Назначение и границы системы
1.1. Назначение системы
Система представляет собой настольное Windows-приложение корпоративного класса, предназначенное для автоматизации полного цикла подготовки, верстки, верификации, массовой печати и регламентного учета почтовых отправлений, стандартных конвертов, сопроводительных бланков и реестров в строгом соответствии с почтовыми нормативами (включая ГОСТ Р 51506-99).

Приложение автоматизирует:

Ведение многоаспектной адресной книги физических и юридических лиц с поддержкой множественных адресов (юридический, фактический, почтовый) и структурированным хранением адресных данных (индекс + 6 строк адреса);

Формирование списков рассылок с контролем четырехстадийного жизненного цикла отправления («Создано/Напечатано» 
→ «Отправлено» 
→ «Подтверждено» / «Отменено»);

Высокоточную векторную и растровую печать на конвертах стандартных форматов (C6, DL, C5, C4, B4, C3, E65/E4, K5, K6, K8, K65) и произвольных листовых носителях (A5, A4, A3) с миллиметровым позиционированием;

Печать унифицированных почтовых бланков (ф.112э, ф.112эф, ф.112эю, ф.112эп, ф.116, ф.117+ф.113, ф.119) и регламентных реестров/описей (ф.103, ф.107, журналы входящей/исходящей корреспонденции);

Пакетную печать адресных этикеток (наклеек) со спуском полос, метками обреза (crop marks) и технологическими вылетами под обрез (bleed);

Интерактивный импорт данных с гибким маппингом колонок, отсеиванием некорректных записей и эвристическим распознаванием компонентов адреса;

Экспорт реестров и документов в форматы OpenXML/SpreadsheetML/WordprocessingML без зависимости от наличия установленного пакета Microsoft Office;

Внешнюю интеграцию через COM-сервер автоматизации (ProgID PEPro.Application) и CLI-контракты.

1.2. Границы системы (System Boundaries)
Что система выполняет:

Управление адресными данными, классификация контрагентов по категориям и иерархическим папкам;

Машиночитаемую печать индексов специализированными шрифтами (OCR-B / кодовый штамп);

Формирование и динамическое кэширование двумерных кодов (QR-код стандарта ГОСТ Р 56042-2014);

Расчет сумм прописью на основе заменяемых языковых словарей (.lng);

Двухэтапную валидацию физических параметров печати (метрики документа против аппаратных возможностей драйвера принтера DEVMODE);

Двустороннюю печать (дуплекс) и программную сепарацию четных/нечетных страниц;

Автоматизированное резервное копирование базы данных по расписанию в ZIP-архивы с шифрованием;

Электронную почтовую рассылку текстовых и HTML-сообщений через SMTP с поддержкой SSL/TLS и прокси.

Что система осознанно НЕ выполняет (Out of Scope):

Не является системой бухгалтерского учета, ERP или CRM: в системе отсутствуют бухгалтерские проводки, учет взаиморасчетов, выставление счетов и товарный склад (реализован только легковесный склад вложений для почтовой описи);

Не содержит собственного драйвера виртуальной печати (работа ведется через стандартную подсистему Windows Spooler / GDI+ / DirectWrite);

Не выполняет оптическое распознавание текста (OCR) сканированных документов;

Не выполняет автоматическое устранение перекоса (deskew) и сшивку многостраничных сканов;

Не реализует модемную передачу факсов (поддерживается исключительно программный импорт и очистка растровых факсимильных файлов форматов TIFF/G3/G4);

Не использует агрессивные механизмы защиты процесса от отладки (Ring-0 хуки, порты ввода-вывода), способные вызывать конфликты с корпоративными средствами EDR/антивирусами.

1.3. Пользовательские роли и матрица доступа
Оператор печати:

Ведение и фильтрация адресной книги, просмотр карточек;

Формирование партий писем, объединение контрагентов в рассылки;

Запуск предпросмотра, калибровка смещений, тестовая и пакетная печать;

Регистрация отправки корреспонденции, печать реестров ф.103 и описей ф.107.

Технолог (Дизайнер шаблонов):

Создание и параметризация бланков и шаблонов конвертов во встроенном редакторе;

Привязка визуальных полей к словарю схемы данных;

Настройка правил переноса строк, автоподбора кегля, зон неперекрытия;

Импорт и валидация внешних массивов адресов (Excel, CSV, 1C), сохранение профилей сопоставления.

Системный администратор:

Конфигурация топологии БД (локальная SQLite или корпоративный кластер PostgreSQL);

Управление расписанием и политиками автоматического архивирования;

Регистрация COM-сервера в ОС (HKCU\Software\Classes);

Настройка SMTP-шлюзов и параметров прокси-серверов;

Управление лицензионными ключами и контроль параметров аудита.

1.4. Регламентные сценарии работы
Сценарий А: Пакетная подготовка периодической рассылки:

Технолог импортирует реестр начислений из учетной системы (CSV/XLSX);

Мастер импорта сопоставляет столбцы, валидирует индексы, проверяет ограничение длины полей (
≤255 символов) и раскладывает адреса по категориям;

Оператор выделяет целевую категорию, активирует функцию «Объединить в рассылку»;

Программа автоматически рассчитывает счетчик «Всего необходимо конвертов»;

Выполняется пробная печать одного листа из текущей полиграфической партии на выбранном принтере;

После визуальной сверки точности полей запускается пакетная печать партии с фиксацией задания в неизменяемом журнале PrintJob.

Сценарий Б: Автоматическая печать из внешней ERP/1С через COM-сервер:

Внешний процесс создает COM-инстанс PEPro.Application;

Вызывается метод PrintFromXmlRegistersWithoutForm или PrintEnvelope;

Метод OpenDummyFormToSetAsForeground перехватывает системный фокус ввода для исключения фонового зависания диалогов;

Происходит генерация и спулинг задания;

Создается учетная запись исходящего письма (CreateOutcoming), статус отправления инкрементируется;

COM-сервер возвращает целочисленный статус выполнения операции.

2. Архитектура программного комплекса
Приложение строится на базе принципов Domain-Driven Design (DDD), чистой слоистой архитектуры (Clean Architecture) и инверсии зависимостей (Dependency Inversion Principle).

2.1. Слои системы и допустимые зависимости
  ┌─────────────────────────────────────────────────────────────┐
  │                 Слой представления (UI / CLI)               │
  │   - WinForms/WPF Modern Shell (Плотная сетка, 8pt Tahoma)   │
  │   - Встроенный визуальный векторный дизайнер бланков        │
  │   - Контроллеры диалогов, мастера валидации импорта         │
  └──────────────────────────────┬──────────────────────────────┘
                                 │
  ┌──────────────────────────────▼──────────────────────────────┐
  │              Слой прикладных служб (Application)            │
  │   - Сценарии рассылок, конвейеры импорта/экспорта           │
  │   - Управление печатными сессиями, калькулятор тарифов      │
  │   - Диспетчер COM-автоматизации (IPEProApplication)         │
  └──────────────┬───────────────────────────────┬──────────────┘
                 │                               │
  ┌──────────────▼──────────────┐ ┌──────────────▼──────────────┐
  │    Доменный слой (Domain)   │ │      Слой печати (Print)    │
  │ - Сущности: Counterparty,   │ │ - Двухэтапный валидатор     │
  │   Mail, Group, BlankField   │ │   метрик (metrics vs DEVMODE│
  │ - Агрегаты: Distribution    │ │ - GDI+/DirectWrite рендерер │
  │ - Правила целостности,      │ │ - Генератор кодового штампа │
  │   инварианты адресации      │ │ - Кэш растров QR (ГОСТ)     │
  └──────────────▲──────────────┘ └──────────────▲──────────────┘
                 │                               │
  ┌──────────────┴───────────────────────────────┴──────────────┐
  │           Слой инфраструктуры и данных (Infrastructure)     │
  │   - Репозитории EF Core / Dapper (PostgreSQL / SQLite)      │
  │   - Независимый файловый процессор бланков (.lbk / ZIP)     │
  │   - Экспорт OpenXML / XSLT (без COM Interop MS Office)      │
  │   - Аппаратные адаптеры сканирования TWAIN / WIA            │
  │   - Защищенное хранилище секретов (Windows DPAPI)           │
  └─────────────────────────────────────────────────────────────┘
2.2. Архитектурные инварианты
Изоляция слоя запросов: Ни один исполняемый SQL-запрос для отчетов и бланков не зашивается в C#-код бинарных сборок. SQL-проекция хранится в открытом виде внутри структуры описателя отчета/бланка (report.xml / blank.xml). Это обеспечивает модификацию отчетов силами технологов без пересборки системы.

Файловая независимость макетов: Шаблоны бланков — это версионируемые ZIP-пакеты с расширением .lbk, расположенные в конфигурируемом файловом хранилище (локальном или UNC-пути сети предприятия). База данных хранит исключительно метаданные, геометрию полей и относительные пути к файлам.

Документируемость печати: Слой печати не может выполнить прямую отправку данных в подсистему winspool.drv в обход создания транзакции аудита в таблице PrintJob.

Отказоустойчивость COM-интерфейса: Никакое исключение в COM-вызовах не должно приводить к аварийному завершению процесса сервера; ошибки транслируются в возвращаемые коды HRESULT и флаги GetWarningType().

3. Целевая модель данных
3.1. Структура сущностей и правила целостности
Схема спроектирована для работы под управлением СУБД PostgreSQL 16 (с полной обратной совместимостью со встраиваемой СУБД SQLite 3 для переносных USB-инсталляций).

   ┌──────────────────────┐             1..* ┌──────────────────────┐
   │     Counterparty     │──────────────────│  CounterpartyField   │
   └──────────┬───────────┘                  └──────────────────────┘
              │ 1
              │
              │ 0..*
   ┌──────────▼───────────┐             0..* ┌──────────────────────┐
   │         Mail         │──────────────────│      Inventory       │
   └──────────┬───────────┘                  └──────────┬───────────┘
              │                                         │ 1
              │ 0..*                                    │ 1..*
   ┌──────────▼───────────┐                  ┌──────────▼───────────┐
   │     PrintHistory     │                  │    InventoryItem     │
   └──────────────────────┘                  └──────────────────────┘

   ┌──────────────────────┐             1..* ┌──────────────────────┐
   │    PrintTemplate     │──────────────────│      BlankField      │
   └──────────┬───────────┘                  └──────────────────────┘
              │ 1
              │ 0..*
   ┌──────────▼───────────┐
   │       PrintJob       │
   └──────────────────────┘
3.1.1. Таблица counterparties (Контрагенты и адресная книга)
Хранит базовые реквизиты юридических и физических лиц, а также 3 типа независимых адресов.

id (BIGSERIAL, PRIMARY KEY) — Уникальный суррогатный идентификатор;

type (SMALLINT, NOT NULL) — 0: Юридическое лицо, 1: Физическое лицо;

name (VARCHAR(255), NOT NULL) — Полное официальное наименование / ФИО;

short_name (VARCHAR(255)) — Сокращенное наименование;

inn (VARCHAR(12)) — ИНН (10 знаков для ЮЛ, 12 для ФЛ) / УНП;

kpp (VARCHAR(9)) — КПП юридического лица;

ogrn (VARCHAR(15)) — ОГРН / ОГРНИП;

okfs (VARCHAR(10)), okpo (VARCHAR(10)), okved (VARCHAR(20));

bik (VARCHAR(9)) — БИК банка;

ks (VARCHAR(20)) — Корреспондентский счет банка;

rs (VARCHAR(20)) — Расчетный счет контрагента;

rkc (VARCHAR(255)) — Наименование расчетно-кассового центра;

bank_name (VARCHAR(255)) — Наименование обслуживающего банка;

passport_series (VARCHAR(10)), passport_number (VARCHAR(20));

passport_issued_by (VARCHAR(255)), passport_issued_date (DATE);

birth_date (DATE), birth_place (VARCHAR(255));

position (VARCHAR(150)) — Должность руководителя / контактного лица;

director_fio (VARCHAR(255)) — ФИО первого лица;

email (VARCHAR(150)), phone (VARCHAR(100));

special_notes (TEXT) — Особые технологические отметки;

Адрес юридический: legal_zip (VARCHAR(6)), legal_str1..legal_str6 (VARCHAR(255)), legal_raw (VARCHAR(1000));

Адрес почтовый: post_zip (VARCHAR(6)), post_str1..post_str6 (VARCHAR(255)), post_raw (VARCHAR(1000));

Адрес фактический: fact_zip (VARCHAR(6)), fact_str1..fact_str6 (VARCHAR(255)), fact_raw (VARCHAR(1000));

address_main_type (SMALLINT, DEFAULT 1) — Тип адреса по умолчанию (0: Юридический, 1: Почтовый, 2: Фактический);

group_id (BIGINT, REFERENCES groups(id) ON DELETE SET NULL);

is_vip (BOOLEAN, DEFAULT FALSE) — Признак «Избранный адрес»;

is_sender (BOOLEAN, DEFAULT FALSE) — Флаг доступности в качестве глобального отправителя;

is_active (BOOLEAN, DEFAULT TRUE) — Флаг активности записи;

is_deleted (BOOLEAN, DEFAULT FALSE) — Мягкое удаление (Soft Delete);

created_at (TIMESTAMPTZ, DEFAULT NOW()), updated_at (TIMESTAMPTZ, DEFAULT NOW()).

3.1.2. Таблица groups (Категории и группы адресов)
id (BIGSERIAL, PRIMARY KEY);

name (VARCHAR(255), NOT NULL UNIQUE);

note (TEXT);

is_default (BOOLEAN, DEFAULT FALSE) — Ровно одна системная группа по умолчанию («Неразобранные»);

is_closed (BOOLEAN, DEFAULT FALSE) — Блокировка добавления новых записей;

sort_order (INT, DEFAULT 0).

3.1.3. Таблица mails (Почтовые отправления и рассылки)
id (BIGSERIAL, PRIMARY KEY);

counterparty_id (BIGINT, NOT NULL, REFERENCES counterparties(id));

sender_id (BIGINT, NOT NULL, REFERENCES counterparties(id));

distribution_id (BIGINT, REFERENCES distributions(id) ON DELETE SET NULL);

envelope_type (VARCHAR(50)) — Формат конверта (C6, DL, C5, C4 и др.);

notice_type (VARCHAR(50)) — Вид уведомления (ф.119, 112эп);

copy_count (INT, DEFAULT 1 CHECK (copy_count >= 1));

is_vip (BOOLEAN, DEFAULT FALSE);

note (VARCHAR(255));

declared_value (NUMERIC(12,2), DEFAULT 0.00);

weight (NUMERIC(8,3), DEFAULT 0.000);

fee (NUMERIC(12,2), DEFAULT 0.00);

status (SMALLINT, DEFAULT 0) — 0: Черновик, 1: Напечатано, 2: Отправлено, 3: Подтверждено, 4: Отменено;

need_notification (BOOLEAN, DEFAULT FALSE);

need_return_address (BOOLEAN, DEFAULT TRUE);

mail_category (SMALLINT, DEFAULT 0) — 0: Простое, 1: Заказное, 2: С объявленной ценностью, 3: 1-й класс;

date_created (TIMESTAMPTZ, DEFAULT NOW());

date_printed (TIMESTAMPTZ);

date_dispatched (TIMESTAMPTZ);

date_confirmation (TIMESTAMPTZ);

date_canceled (TIMESTAMPTZ);

incoming_number (VARCHAR(50)), outgoing_number (VARCHAR(50));

post_register_id (BIGINT, REFERENCES post_registers(id));

russian_post_id (VARCHAR(14)) — ШПИ / почтовый идентификатор (14 знаков);

russian_post_batch (VARCHAR(50)) — Номер партии Почты России;

inventory_id (BIGINT, REFERENCES inventories(id));

to_zip (VARCHAR(6), NOT NULL);

to_str1..to_str6 (VARCHAR(255)).

3.1.4. Таблица print_templates (Шаблоны конвертов и бланков)
id (BIGSERIAL, PRIMARY KEY);

name (VARCHAR(255), NOT NULL);

kind (VARCHAR(30), NOT NULL) — envelope, blank, report, label, notification;

format (VARCHAR(20), NOT NULL) — C6, DL, C5, C4, B4, CUSTOM;

width_mm (NUMERIC(6,2), NOT NULL);

height_mm (NUMERIC(6,2), NOT NULL);

orientation (SMALLINT, DEFAULT 0) — 0: Portrait, 1: Landscape;

file_path (VARCHAR(500), NOT NULL) — Относительный путь к .lbk архиву;

is_builtin (BOOLEAN, DEFAULT FALSE) — Запрет на физическое удаление встроенного макета;

country (VARCHAR(3), DEFAULT 'RUS') — Влияет на нормативную разметку полей.

3.1.5. Таблица blank_fields (Координатная сетка полей бланка)
id (BIGSERIAL, PRIMARY KEY);

template_id (BIGINT, NOT NULL, REFERENCES print_templates(id) ON DELETE CASCADE);

name (VARCHAR(100), NOT NULL);

kind (VARCHAR(20), NOT NULL) — text, post_index, qr, picture, barcode;

page (INT, DEFAULT 1);

x_mm (NUMERIC(6,2), NOT NULL);

y_mm (NUMERIC(6,2), NOT NULL);

length_mm (NUMERIC(6,2), NOT NULL);

height_mm (NUMERIC(6,2), NOT NULL);

font_name (VARCHAR(100), DEFAULT 'Tahoma');

font_height_pt (NUMERIC(4,1), DEFAULT 10.0);

font_height_min_pt (NUMERIC(4,1), DEFAULT 6.0);

font_height_max_pt (NUMERIC(4,1), DEFAULT 14.0);

is_bold (BOOLEAN, DEFAULT FALSE);

is_italic (BOOLEAN, DEFAULT FALSE);

color_rgb (INT, DEFAULT 0);

align (SMALLINT, DEFAULT 0) — 0: Left, 1: Center, 2: Right, 3: Justify;

max_chars (INT, DEFAULT 255);

is_visible (BOOLEAN, DEFAULT TRUE);

is_printable (BOOLEAN, DEFAULT TRUE);

zone_role (VARCHAR(30)) — TopSender, BottomRecipient, PostIndexZone, InfoZone.

3.1.6. Таблица print_jobs (Неизменяемый реестр сессий печати)
id (BIGSERIAL, PRIMARY KEY);

created_at (TIMESTAMPTZ, NOT NULL, DEFAULT NOW());

template_id (BIGINT, REFERENCES print_templates(id));

printer_name (VARCHAR(255), NOT NULL);

paper_size_name (VARCHAR(100), NOT NULL);

paper_width_mm (NUMERIC(6,2), NOT NULL);

paper_height_mm (NUMERIC(6,2), NOT NULL);

font_info (VARCHAR(255));

copies (INT, NOT NULL);

print_mode (VARCHAR(50), NOT NULL) — selected, batch_preview, batch_direct;

status (VARCHAR(20), NOT NULL) — SUCCESS, ABORTED, FAILED, BLOCKED_SIZE;

error_text (TEXT);

devmode_snapshot (BYTEA) — Бинарный снимок параметров печати.

3.1.7. Таблица print_history (Журнал напечатанных конвертов)
id (BIGSERIAL, PRIMARY KEY);

printed_at (TIMESTAMPTZ, NOT NULL, DEFAULT NOW());

mail_id (BIGINT, REFERENCES mails(id) ON DELETE SET NULL);

recipient_address_snapshot (TEXT, NOT NULL);

sender_address_snapshot (TEXT, NOT NULL);

copy_count (INT, NOT NULL);

is_vip (BOOLEAN, NOT NULL);

note (VARCHAR(255));

template_description (VARCHAR(255), NOT NULL);

print_job_id (BIGINT, REFERENCES print_jobs(id) ON DELETE CASCADE).

3.1.8. Таблица qr_cache (Кэш сгенерированных векторных/растровых кодов)
id (BIGSERIAL, PRIMARY KEY);

payload_hash (CHAR(64), NOT NULL UNIQUE) — SHA-256 хэш строки данных;

image_blob (BYTEA, NOT NULL) — Несжатый 1-bpp/8-bpp растр в исходном DPI без антиалиасинга;

created_at (TIMESTAMPTZ, DEFAULT NOW()).

3.1.9. Таблица counterparty_fields (Пользовательские поля контрагента)
id (BIGSERIAL, PRIMARY KEY);

counterparty_id (BIGINT, NOT NULL, REFERENCES counterparties(id) ON DELETE CASCADE);

field_name (VARCHAR(100), NOT NULL) — Имя ключа; уникально в пределах одного контрагента (UNIQUE(counterparty_id, field_name));

field_value (VARCHAR(1000));

field_type (SMALLINT, DEFAULT 0) — 0: Строка, 1: Число, 2: Дата;

updated_at (TIMESTAMPTZ, DEFAULT NOW()).

Пользовательское поле автоматически становится доступным в дизайнере бланков как тег [Custom_<имя>].

3.1.10. Таблица distributions (Рассылки)
id (BIGSERIAL, PRIMARY KEY);

name (VARCHAR(255), NOT NULL);

sender_id (BIGINT, REFERENCES counterparties(id)) — Отправитель рассылки;

kind (SMALLINT, DEFAULT 0) — 0: «Один ко многим», 1: «Многие к одному»;

note (VARCHAR(1000));

state (SMALLINT, DEFAULT 0) — 0: Черновик, 1: Сохранена, 2: Отправлена;

envelopes_required (INT) — Вычисляемый счетчик «Всего необходимо конвертов» (сумма copy_count входящих писем);

created_at (TIMESTAMPTZ, DEFAULT NOW()), dispatched_at (TIMESTAMPTZ).

3.1.11. Таблица folders (Папки писем)
id (BIGSERIAL, PRIMARY KEY);

name (VARCHAR(255), NOT NULL);

parent_id (BIGINT, REFERENCES folders(id)) — Корень: NULL;

note (VARCHAR(1000));

sort_order (INT, DEFAULT 0).

Ограничение: циклические ссылки parent_id пресекаются триггером/валидатором домена.

3.1.12. Таблица inventories / inventory_items (Опись вложений)
inventories: id (BIGSERIAL, PRIMARY KEY), name (VARCHAR(255), NOT NULL), counterparty_id (BIGINT, REFERENCES counterparties(id)), destination (TEXT) — «Куда», note (TEXT), created_at (TIMESTAMPTZ, DEFAULT NOW()).

inventory_items: id (BIGSERIAL, PRIMARY KEY), inventory_id (BIGINT, NOT NULL, REFERENCES inventories(id) ON DELETE CASCADE), name (VARCHAR(500), NOT NULL), quantity (INT, NOT NULL CHECK (quantity > 0)), cost (NUMERIC(12,2), DEFAULT 0.00), note (VARCHAR(500)), created_at (TIMESTAMPTZ, DEFAULT NOW()).

3.1.13. Таблица post_registers (Почтовые реестры для трекера отправлений)
id (BIGSERIAL, PRIMARY KEY);

register_number (VARCHAR(50), NOT NULL);

batch_number (VARCHAR(50)) — Номер партии Почты России;

sending_date (DATE), shipping_point (VARCHAR(255)) — Пункт отправки;

track_login (VARCHAR(150)), track_password_protected (BYTEA) — Учетные данные трекера; пароль хранится только в защищенном виде (DPAPI), никогда открытым текстом;

track_token (VARCHAR(255));

mail_ids (JSONB) — Список идентификаторов вошедших отправлений;

created_at (TIMESTAMPTZ, DEFAULT NOW()).

3.1.14. Таблица document_movements (Движение входящих документов)
id (BIGSERIAL, PRIMARY KEY);

mail_id (BIGINT, NOT NULL, REFERENCES mails(id) ON DELETE CASCADE);

moved_at (TIMESTAMPTZ, NOT NULL, DEFAULT NOW());

target_department (VARCHAR(255), NOT NULL), executor (VARCHAR(255));

action (SMALLINT) — 0: Приём, 1: Передача на исполнение, 2: В архив.

3.1.15. Таблица mail_documents (Связь писем с номерами первичных документов)
id (BIGSERIAL, PRIMARY KEY);

mail_id (BIGINT, NOT NULL, REFERENCES mails(id) ON DELETE CASCADE);

doc_number (VARCHAR(100), NOT NULL) — Номер связанного документа для вывода «через запятую» в реестре.

3.1.16. Таблица printer_profiles (Профили принтеров и калибровочные смещения)
id (BIGSERIAL, PRIMARY KEY);

printer_name (VARCHAR(255), NOT NULL);

envelope_format (VARCHAR(20), NOT NULL) — Калибровка хранится отдельно на каждую пару «принтер + формат» (UNIQUE(printer_name, envelope_format));

offset_x_mm (NUMERIC(5,2), DEFAULT 0.0), offset_y_mm (NUMERIC(5,2), DEFAULT 0.0) — Тонкий сдвиг всего изображения, шаг 0.1 мм;

feed_alignment (SMALLINT, DEFAULT 0) — 0: По центру, 1: По левому краю, 2: По правому краю;

fix_a4 (BOOLEAN, DEFAULT FALSE), duplex_capable (BOOLEAN, DEFAULT FALSE);

is_default (BOOLEAN, DEFAULT FALSE);

devmode_template (BYTEA) — Снимок DEVMODE для воспроизведения настроек.

3.1.17. Таблица parameters (Хранилище ключ-значение)
id (BIGSERIAL, PRIMARY KEY);

name (VARCHAR(100), NOT NULL UNIQUE) — Счетчики нумерации (LastOutgoingNumber, LastIncomingNumber), глобальный отправитель, шаблоны масок номеров и пр.;

value (VARCHAR(1000));

updated_at (TIMESTAMPTZ, DEFAULT NOW()).

3.1.18. Таблица schema_version (Версионирование схемы)
id (BIGSERIAL, PRIMARY KEY);

version (VARCHAR(20), NOT NULL), description (VARCHAR(500));

applied_at (TIMESTAMPTZ, NOT NULL, DEFAULT NOW()).

Миграции выполняются только вперед (forward-only); каждая миграция атомарна и пишет строку в schema_version. Откат схемы вручную не поддерживается — восстановление через архив БД.

3.1.19. Стратегия миграции и индексов
Все миграции поставляются как встроенные ресурсы сборки Infrastructure (формат: V<версия>__<описание>.sql для PostgreSQL и адаптированный эквивалент для SQLite);

Обязательные индексы: counterparties(name), counterparties(inn), counterparties(group_id), mails(counterparty_id), mails(sender_id), mails(status), mails(date_created), mails(post_register_id), print_history(printed_at), print_history(mail_id), inventory_items(inventory_id), counterparty_fields(counterparty_id);

Правило целостности адреса: адрес никогда не хранится одной строкой — zip + str1..str6 отдельными колонками; поле *_raw служит только для round-trip импорта;

Правило снимков: print_history хранит текстовые снимки адресов на момент печати; последующие изменения карточек контрагентов не изменяют исторические записи;

Правило мягкого удаления: is_deleted = TRUE исключает запись из рабочих выборок; окончательное удаление — отдельной командой с подтверждением.

3.2. Словарь полей: единая схема «БД ↔ Шаблоны ↔ Импорт»
Во избежание расхождений и повреждения данных при миграциях и сопоставлениях закрепляется строгая матрица имен полей. Никакие другие имена не допускаются к использованию в выражениях бланков и маппере импорта:

Имя колонки БД	Описание сущности	Тег привязки в шаблоне	Псевдоним маппера импорта
counterparties.id	Внутренний ID записи	[id]	Не импортируется
to_zip	Почтовый индекс получателя	[edtIndeks_to]	ИндексПолучателя, ind_to
to_str1	Строка получателя 1 (Организация/ФИО)	[edt_str_to1]	КомуСтрока1, str_to1
to_str2	Строка получателя 2 (Подразделение/Улица)	[edt_str_to2]	КомуСтрока2, str_to2
to_str3	Строка получателя 3 (Дом, корпус, строение)	[edt_str_to3]	КомуСтрока3, str_to3
to_str4	Строка получателя 4 (Квартира/Офис)	[edt_str_to4]	КомуСтрока4, str_to4
to_str5	Строка получателя 5 (Населенный пункт)	[edt_str_to5]	КомуСтрока5, str_to5
to_str6	Строка получателя 6 (Область/Край/Регион)	[edt_str_to6]	КомуСтрока6, str_to6
from_zip	Почтовый индекс отправителя	[edtIndeks_from]	ИндексОтправителя, ind_from
from_str1..from_str6	Адресные строки отправителя 1..6	[edt_str_from1]..[edt_str_from6]	ОтКогоСтрока1..6, str_from1..6
copy_count	Количество экземпляров	[copyes]	КоличествоКопий, copyes
is_vip	Признак приоритетного адресата	[vip]	Избранное, vip
note	Технологическое примечание	[edt_str_prim]	Примечание, prim
declared_value	Сумма объявленной ценности	[declared_value]	Ценность, declared_value
fee	Плата за пересылку	[fee]	Тариф, fee
weight	Вес отправления (кг)	[weight]	Вес, weight
post_index_machine	Машиночитаемый штрих-код индекса	[POSTINDEX]	Формируется вычислением
4. Слой запросов и динамическая проекция отчетов
4.1. Формат и архитектура файла запроса
Каждый печатный отчет или пакетный бланк содержит файл определения запроса данных query.xml, размещаемый внутри пакета .lbk либо в каталоге отчетов Templates/Reports/<ReportName>/query.xml.

XML
<?xml version="1.0" encoding="utf-8"?>
<ReportQueryDefinition id="Report_F103_Standard" version="1.0">
  <Name>Формирование почтового реестра ф.103</Name>
  <Parameters>
    <Parameter name="DateFrom" type="DateTime" required="true" default="Today-30d" />
    <Parameter name="DateTo" type="DateTime" required="true" default="Today" />
    <Parameter name="SenderId" type="Int64" required="true" />
    <Parameter name="MailCategory" type="Int32" required="false" />
  </Parameters>
  <ValidationRules>
    <Rule type="DateRange" start="DateFrom" end="DateTo" maxDays="90" />
    <Rule type="SqlInjectionGuard" mode="StrictParametric" />
  </ValidationRules>
  <Command dialect="PostgreSql">
    <![CDATA[
      SELECT 
        m.id AS mail_id,
        m.outgoing_number AS out_num,
        m.date_dispatched AS dispatch_date,
        c.name AS recipient_name,
        m.to_zip AS recipient_zip,
        CONCAT_WS(', ', m.to_str6, m.to_str5, m.to_str2, m.to_str3, m.to_str4) AS recipient_address,
        m.weight AS item_weight,
        m.declared_value AS item_value,
        m.fee AS item_fee,
        m.russian_post_id AS barcode
      FROM mails m
      JOIN counterparties c ON m.counterparty_id = c.id
      WHERE m.sender_id = :SenderId
        AND m.date_created BETWEEN :DateFrom AND :DateTo
        AND (:MailCategory IS NULL OR m.mail_category = :MailCategory)
        AND m.status != 4
      ORDER BY m.id ASC;
    ]]>
  </Command>
  <Command dialect="Sqlite">
    <![CDATA[
      SELECT 
        m.id AS mail_id,
        m.outgoing_number AS out_num,
        m.date_dispatched AS dispatch_date,
        c.name AS recipient_name,
        m.to_zip AS recipient_zip,
        (COALESCE(m.to_str6 || ', ', '') || COALESCE(m.to_str5 || ', ', '') || COALESCE(m.to_str2, '')) AS recipient_address,
        m.weight AS item_weight,
        m.declared_value AS item_value,
        m.fee AS item_fee,
        m.russian_post_id AS barcode
      FROM mails m
      JOIN counterparties c ON m.counterparty_id = c.id
      WHERE m.sender_id = :SenderId
        AND m.date_created BETWEEN :DateFrom AND :DateTo
        AND (:MailCategory IS NULL OR m.mail_category = :MailCategory)
        AND m.status != 4
      ORDER BY m.id ASC;
    ]]>
  </Command>
</ReportQueryDefinition>
4.2. Конкретные примеры запросов слоя данных
Запрос 1: Пакетная выборка данных для печати конвертов (qrReport_Envelopes)
SQL
SELECT 
  m.id AS mail_id,
  m.to_zip AS ind_to,
  m.to_str1 AS str_to1, m.to_str2 AS str_to2, m.to_str3 AS str_to3,
  m.to_str4 AS str_to4, m.to_str5 AS str_to5, m.to_str6 AS str_to6,
  s.post_zip AS ind_from,
  s.post_str1 AS str_from1, s.post_str2 AS str_from2, s.post_str3 AS str_from3,
  s.post_str4 AS str_from4, s.post_str5 AS str_from5, s.post_str6 AS str_from6,
  m.note AS prim,
  m.copy_count AS copyes,
  m.is_vip AS vip,
  NOW() AS date_print
FROM mails m
JOIN counterparties s ON m.sender_id = s.id
WHERE m.id IN (:SelectedMailIds)
ORDER BY m.id ASC;
Запрос 2: Выборка растра QR-кода адресата (qrQRcode_Fetch)
SQL
SELECT 
  qc.image_blob AS img_qrcode
FROM qr_cache qc
WHERE qc.payload_hash = :PayloadHash;
Запрос 3: Опись вложений ф.107 (qrReport_Inventory107)
SQL
SELECT 
  inv.name AS inv_name,
  c.name AS komu,
  CONCAT_WS(', ', m.to_zip, m.to_str6, m.to_str5, m.to_str2) AS kuda,
  it.name AS det_name,
  it.quantity AS kol,
  it.cost AS cost,
  (it.quantity * it.cost) AS total_item_cost,
  COUNT(*) OVER(PARTITION BY inv.id) AS count_konv
FROM inventories inv
JOIN mails m ON m.inventory_id = inv.id
JOIN counterparties c ON m.counterparty_id = c.id
JOIN inventory_items it ON it.inventory_id = inv.id
WHERE inv.id = :InventoryId
ORDER BY it.id ASC;
Запрос 4: Журнал регистрации входящей корреспонденции (qrReport_IncomingJournal)
SQL
SELECT 
  m.incoming_number AS inc_num,
  m.date_created AS reg_date,
  c.name AS sender_name,
  c.inn AS sender_inn,
  m.to_str1 AS subject,
  m.note AS execution_mark,
  m.status AS doc_state
FROM mails m
JOIN counterparties c ON m.counterparty_id = c.id
WHERE m.date_created BETWEEN :DateFrom AND :DateTo
  AND m.incoming_number IS NOT NULL
ORDER BY m.date_created DESC;
Запрос 5: Статистика почтовых рассылок по категориям (qrReport_MailingStats)
SQL
SELECT 
  g.name AS gr_name,
  COUNT(m.id) AS count_konv,
  SUM(m.fee) AS total_fee,
  SUM(m.declared_value) AS total_val,
  CAST(m.date_created AS DATE) AS date_stat
FROM mails m
JOIN counterparties c ON m.counterparty_id = c.id
JOIN groups g ON c.group_id = g.id
WHERE m.date_created BETWEEN :DateFrom AND :DateTo
GROUP BY g.name, CAST(m.date_created AS DATE)
ORDER BY date_stat DESC, gr_name ASC;
Запрос 6: Выборка групп конвертов для групповой обработки (qrReport_GroupEnvelopes)
SQL
SELECT 
  c.id AS contr_id,
  c.post_zip AS ind_to,
  c.post_str1 AS str_to1, c.post_str2 AS str_to2, c.post_str3 AS str_to3,
  c.post_str4 AS str_to4, c.post_str5 AS str_to5, c.post_str6 AS str_to6,
  c.special_notes AS prim,
  g.name AS group_name
FROM counterparties c
JOIN groups g ON c.group_id = g.id
WHERE g.id = :GroupId AND c.is_active = TRUE AND c.is_deleted = FALSE
ORDER BY c.name ASC;
Запрос 7: Исторический срез сессий печати (qrReport_PrintHistoryReport)
SQL
SELECT 
  ph.printed_at,
  ph.template_description,
  ph.recipient_address_snapshot,
  ph.sender_address_snapshot,
  ph.copy_count,
  pj.printer_name,
  pj.paper_size_name,
  pj.status
FROM print_history ph
JOIN print_jobs pj ON ph.print_job_id = pj.id
WHERE ph.printed_at BETWEEN :DateFrom AND :DateTo
ORDER BY ph.printed_at DESC;
Запрос 8: Реестр почтовых переводов ф.112эп (qrReport_MoneyTransfer112ep)
SQL
SELECT 
  m.id,
  m.declared_value AS transfer_amount,
  c.name AS recipient_fio,
  c.passport_series AS pass_ser,
  c.passport_number AS pass_num,
  c.passport_issued_by AS pass_whowhen,
  c.bik, c.rs, c.ks, c.bank_name,
  s.name AS sender_fio,
  s.inn AS sender_inn
FROM mails m
JOIN counterparties c ON m.counterparty_id = c.id
JOIN counterparties s ON m.sender_id = s.id
WHERE m.id = :MailId;
5. Формат бланка и встроенный визуальный редактор
5.1. Физическая структура пакета бланка (.lbk)
Пакет .lbk представляет собой ZIP-контейнер стандартной дефляции со следующей внутренней структурой:

Шаблон_Конверт_С5.lbk
│
├── manifest.json            # Описание пакета, версия, автор, совместимость
├── blank.xml                # Векторное координатное дерево полей и зон
├── query.xml                # Спецификация SQL-запроса проекции данных
├── background.png           # Опциональная растровая подложка (300/600 DPI)
└── assets/                  # Дополнительные статические растровые элементы (гербы, плашки)
    └── emblem.png
Листинг blank.xml:

XML
<?xml version="1.0" encoding="utf-8"?>
<CustomBlank version="2.0">
  <Name>Конверт C5 с логотипом ГОСТ</Name>
  <Country>RUS</Country>
  <StandardLayout>true</StandardLayout>
  <Geometry>
    <Width mm="229.0" />
    <Height mm="162.0" />
    <Orientation>Landscape</Orientation>
    <Bleed mm="0.0" />
    <CropMarks>false</CropMarks>
  </Geometry>
  <Pages>
    <Page number="1">
      <Background file="background.png" opacity="1.0" />
      <Fields>
        <Field name="edtIndeks_to" kind="post_index">
          <Bounds x="145.0" y="125.0" width="70.0" height="20.0" />
          <PostIndex mmDigitWidth="3.7" mmDigitHeight="5.5" mmLineWidth="0.45" mmSpacing="5.4" digitCount="6" showGrid="true" />
        </Field>
        <Field name="edt_str_to1" kind="text">
          <Bounds x="110.0" y="85.0" width="105.0" height="6.0" />
          <Font name="Tahoma" sizePt="9.0" minSizePt="6.5" maxSizePt="11.0" bold="false" />
          <Align horizontal="Left" vertical="Center" />
          <Wrap wordWrap="true" maxLines="1" />
        </Field>
        <Field name="qr_code" kind="qr">
          <Bounds x="180.0" y="20.0" width="35.0" height="35.0" />
          <Rendering antialiasing="false" center="true" transparent="false" />
        </Field>
      </Fields>
    </Page>
  </Pages>
</CustomBlank>
5.2. Визуальный редактор и правила компоновки
Координатная сетка и единицы измерения: Внутренний движок редактора рассчитывает позиции в сотых долях миллиметра (
0.01 мм); UI-панель позволяет налету переключать представление между миллиметрами, пунктами (pt) и пикселями экрана (96/120 DPI).

Семантическая разница Visible vs Printable:

Visible = false: Элемент полностью исключен из рендеринга (скрыт и на холсте экрана, и при спулинге на принтер).

Printable = false: Элемент выводится на экране для оператора (например, фоновые сетки разлиновки конверта, габаритные рамки зон, непечатаемые подсказки), но отсекается в потоке команд физического принтера.

Алгоритм детекции перекрытия полей:
Перед сохранением макета и перед стартом печати выполняется пространственная проверка пересечений 
AABB (Axis-Aligned Bounding Box) для каждого элемента 
A и 
B:

Intersect(A,B)=(A.x<B.x+B.w)∧(A.x+A.w>B.x)∧(A.y<B.y+B.h)∧(A.y+A.h>B.y)
В случае обнаружения пересечения несовместимых зон (например, зона qr пересекается с logo или текстовые блоки str_to накладываются друг на друга), редактор подсвечивает конфликтные контуры красным штрихом и выводит список предупреждений.

6. Движок печати и управление периферией
6.1. Архитектура конвейера печати
[Данные Mail] + [Бланк .lbk]
         │
         ▼
[Препроцессинг] ──► Автоподбор кегля (Font Fitting) ──► Формирование PostIndex
         │
         ▼
[Валидатор геометрии] ──► Сравнение metrics.PaperSize vs DEVMODE.dmPaperSize
         │
         ├─── Ошибка (Конверт больше бумаги) ──► Блокировка / Диалог "Зафиксировать А4"
         │
         ▼
[Генератор GDI+/Metafile] ──► Отрисовка текстовых зон, QR (1-bpp), PostIndex глифов
         │
         ├─── Режим F6 ──► Интерактивное векторное превью
         │
         ▼
[Диспетчер спулера] ──► DEVMODE (dmPaperSize, dmOrientation, dmDuplex)
         │
         ▼
[winspool.drv -> Принтер] (с поддержкой прерывания AbortDoc)
         │
         ▼
[Аудит] ──► Запись транзакции в PrintJob и фиксация в PrintHistory
6.2. Проверка метрик: metrics.PaperSize против DEVMODE
Драйверы лотковых офисных принтеров возвращают расходящиеся параметры при запросе физических границ. Движок выполняет двойную сверку:

Запрашивает аппаратный DEVMODE через Win32 API DocumentPropertiesW;

Считывает DeviceCapabilitiesW(DC_PAPERS, DC_PAPERSIZE);

Вычисляет дельту между размером бланка 
(W 
b
​
 ,H 
b
​
 ) и доступной печатной областью:

ΔW=W 
blank
​
 −W 
printable
​
 ,ΔH=H 
blank
​
 −H 
printable
​
 
Правила принятия решения:

Если 
ΔW>1.0 мм или 
ΔH>1.0 мм в режиме стандартной печати: операция прерывается с ошибкой BLOCKED_SIZE («Конверт слишком велик для выбранного формата бумаги принтера»);

Если активирован режим «Зафиксировать размер A4»: принудительно устанавливается DEVMODE.dmPaperSize = DMPAPER_A4, координаты элементов масштабируются с привязкой к верхнему левому углу, а пользователю выводится предупреждение: «Внимание: включена принудительная печать конверта в габаритах листа А4. Убедитесь в правильности физической ориентации конверта в лотке».

6.3. Параметры машиночитаемого индекса по ГОСТ Р 51506-99
Кодовый штамп для шестизначного индекса должен строго удовлетворять нормативным физическим параметрам:

Ширина кодового знака: 
3.7 мм±0.1 мм;

Высота знака: 
5.5 мм±0.1 мм;

Толщина линии элементов цифр: строго от 
0.20 мм до 
0.50 мм (по умолчанию 
0.45 мм, превышение свыше 
0.5 мм блокируется);

Шаг между знаками: 
5.4 мм;

При печати международных отправлений («Мн») поле кодового штампа формируется трехзначным с фиксированным индексом «555».

Программа проверяет наличие в ОС TTF-шрифта ZIPcode.ttf. В случае отсутствия шрифта запрещается скрытый фолбэк на Arial/Times — процесс приостанавливается с выводом требования переустановить системные шрифты из комплекта поставки.

6.4. Логика генерации и вывода QR-кода
QR-код формируется в соответствии с ГОСТ Р 56042-2014;

Отрисовка выполняется на монохромном битмапе (1 bit per pixel) без субпиксельного сглаживания и размытия границ (InterpolationMode.NearestNeighbor, SmoothingMode.None);

Модуль кода выравнивается по целочисленной сетке пикселей целевого разрешения печати (300/600 DPI) для предотвращения «замыливания» штрихов термоголовками или лазерными барабанами;

Сгенерированный растр кэшируется в таблице qr_cache по SHA-256 ключу сформированной строки.

6.5. Журналирование параметров печати
Каждая сессия печати формирует запись в структурированном логе (Serilog / файл аудита print_audit.log) строго следующего формата:

------------- Вывод на принтер: HP LaserJet Pro M402dn
------------- Параметры принтера: paper=229x162mm, source=Tray 1, duplex=Simplex
Шаблон: Envelope_C5_Custom.lbk, форма 229x162 мм, ориентация Landscape
Имя шрифта: Tahoma, Размер шрифта: 9.0pt, Шрифт индекса: ZIPcode (0.45mm)
Смещение: X=0.00мм Y=1.50мм
Объектов: 12, страниц: 1, копий: 150
Статус: УСПЕШНО, Затрачено времени: 4.2с
-------------
6.6. Таблица параметров печати и зон реакции
Параметр печати	Источник значения	Область реакции при некорректном значении
Имя принтера	Системный Spooler / PrinterProfile	Ошибка: «Принтер недоступен или отключен». Блокировка печати
Формат бумаги (dmPaperSize)	Структура DEVMODE драйвера	Блокировка задания с предложением режима «Зафиксировать А4»
Ориентация подачи	Бланк (blank.xml) / dmOrientation	Автоповорот полотна на 90 
∘
  / Предупреждение о ручной подаче
Толщина линии индекса	Свойства компонента PostIndex	Предупреждение: «Толщина линии вне ГОСТ (0.2-0.5мм)»
Наличие шрифта ZIPcode	Реестр шрифтов GDI32	Блокировка печати конверта до установки шрифта
Позиция логотипа	Свойства BlankField	Предупреждение об обрезании при выходе за Printable Area
Дуплексный режим	Драйвер принтера / свойства бланка	Фолбэк на ручную печать четных/нечетных страниц
7. Пользовательский интерфейс (UI/UX)
7.1. Главное окно приложения (Схема «3+2»)
Интерфейс спроектирован для высоконагруженной непрерывной работы оператора: плотный интерфейс без избыточной анимации, шрифт таблиц 8pt Tahoma, настраиваемая цветовая подсветка строк («зебра», маркеры VIP).

┌────────────────────────────────────────────────────────────────────────────────────────┐
│  Файл   Правка   Вид   Сервис   Печать   Рассылки   Справка                            │
├──────────────────────────┬──────────────────────────────┬──────────────────────────────┤
│ ОТПРАВИТЕЛЬ:             │ ПОЛУЧАТЕЛЬ:                  │ ОТОБРАЖЕНИЕ:                 │
│  Индекс: [220050] [▼]    │  Индекс: [101000] [▼] [x]VIP │  (•) С логотипом             │
│  Стр 1:  [ООО "Техно"]   │  Стр 1:  [ПАО "МосБанк"]     │  ( ) Без логотипа            │
│  Стр 2:  [Экспедиция]    │  Стр 2:  [Управление делами] │  (•) Адрес отправителя       │
│  Стр 3:  [ул. Ленина, 5] │  Стр 3:  [ул. Тверская, 12]  │  (•) Адрес получателя        │
│  Стр 4:  [оф. 210]       │  Стр 4:  [корп. 2, комн. 4]  │ ┌──────────────────────────┐ │
│  Стр 5:  [г. Минск]      │  Стр 5:  [г. Москва]         │ │ [Превью конверта C5]     │ │
│  Стр 6:  []              │  Стр 6:  []                  │ └──────────────────────────┘ │
├──────────────────────────┴──────────────────────────────┴──────────────────────────────┤
│ ПРИМЕЧАНИЕ: [Срочное уведомление о закрытии лицевого счета                           ] │
├──────────────────────────┬─────────────────────────────────────────────────────────────┤
│ СПРАВОЧНИК ОТПРАВИТЕЛЕЙ: │ АДРЕСНАЯ КНИГА / ПОЛУЧАТЕЛИ:                                │
│ [ + ] [ Edit ] [ Del ]   │ [ + ] [ Edit ] [ Del ] [ F5 Печать ] [ F6 Превью ] [ ⚙ ]   │
│ ┌──────┬────────────────┐│ ┌───┬──────┬──────────────┬─────────────┬──────┬─────┬───────┐│
│ │Индекс│Наименование    ││ │VIP│Индекс│Получатель    │Город        │Улица │Копий│Группа ││
│ ├──────┼────────────────┤│ ├───┼──────┼──────────────┼─────────────┼──────┼─────┼───────┤│
│ │220050│ООО "Техно"     ││ │[*]│101000│ПАО "МосБанк" │г. Москва    │Тверск│  1  │Банки  ││
│ │190000│АО "Север"      ││ │[ ]│191186│ООО "Ладога"  │г. С-Петербур│Невски│  2  │Общие  ││
│ └──────┴────────────────┘│ └───┴──────┴──────────────┴─────────────┴──────┴─────┴───────┘│
├──────────────────────────┴─────────────────────────────────────────────────────────────┤
│ Строка состояния: Всего: 1250 | Выбрано: 1 | Всего копий: 1 | База: PostgreSQL (Online) │
└────────────────────────────────────────────────────────────────────────────────────────┘
7.2. Диалог печати конверта (FormEnvelopePrint)
Диалог предоставляет расширенные настройки спуска на принтер:

Выбор целевого принтера с динамической отрисовкой доступных лотков и статуса готовности;

Группа «Порядок вывода адреса»: переключатель [Новый (ГОСТ: улица -> город)] / [Старый (город -> улица)];

Группа «Тип носителя»: [Чистый конверт] / [Размеченный типографский (печать только текста)];

Флаг [Зафиксировать размер A4] с цветовым предупреждением при активации;

Кнопка [Сохранить как PDF] прямого экспорта спуленного задания в файл PDF 1.4;

Кнопка [Прервать печать], активная в течение всего времени передачи задания в спулер.

7.3. Мастер сопоставления колонок импорта (FormImportWizard)
Пошаговый мастер гарантирует валидацию на входе:

Шаг 1: Выбор файла (CSV, XLSX, XML) и страницы таблицы;

Шаг 2: Интерактивный маппинг колонок: визуальное перетаскивание полей БД на столбцы файла;

Шаг 3: Построчный предпросмотр первых 100 записей с подсветкой ошибок валидации (неверная длина индекса, строка 
>255 символов);

Шаг 4: Выбор политики разрешения конфликтов:

[ ] Добавить только проверенные записи;

[ ] Добавить записи без ошибок (проблемные отбросить в CSV-лог);

[ ] Добавить все с принудительным усечением строк до 255 символов.

7.4. Системные горячие клавиши
F1: Открытие контекстной справочной системы (HTML Help / PDF);

F4: Раскрытие выпадающего дерева фильтрации категорий;

F5: Немедленная печать текущего выбранного конверта с показом диалога выбора шаблонов;

F6: Печать всех выделенных записей с формированием интерактивного предпросмотра;

F7: Пакетная печать всех выделенных записей без показа предпросмотра (прямой спулинг);

F8: Открытие текущего макета во встроенном визуальном дизайнере бланков;

Esc: Отмена текущего действия, сброс поисковой строки (при двойном нажатии);

Ctrl + Space: Глобальный системный хоткей (регистрируется через RegisterHotKey Win32 API) — сворачивание приложения в область уведомлений (System Tray) / разворачивание окна;

Ctrl + A: Выделение всех записей в активном гриде;

Ctrl + Alt + F8: Защищенная комбинация безвозвратного пакетного удаления выделенных записей.

7.5. Встроенный редактор бланков (FormBlankEditor, вызов F8)
Назначение: отдельная подсистема «продукт внутри продукта» для создания и доработки макетов без программиста. Новый шаблон создаётся копированием существующего, а не с нуля; при первом открытии редактор выводит подсказку, что поставляемые шаблоны — конструкторы, обычно требующие доработки под конкретный принтер и партию конвертов.

Дерево панелей:

Панель холста (центр) — интерактивный макет с сеткой, привязкой к сетке (Snap), линейками; выделение рамками с маркерами изменения размера; перетаскивание мышью; переключатель единиц отображения мм / pt / px (96/120 DPI) в меню «Вид → Настройки редактора»;

Панель объектов (слева) — «Текст» (пиктограмма A), «Почтовый индекс» (специализированный компонент), «Рисунок» (логотип/эмблема), «QR-код», «Прямоугольная зона»;

Инспектор свойств (справа) — Font, HAlign/VAlign, WordWrap, Rotation (0/90/180/270), Frame (рамка/подчёркивание), Visible, Printable, Bounds (x, y, ширина, высота), MinFontHeight/MaxFontHeight, MaxChars;

Вкладка «События» — редактор скрипта onBeforePrint для привязки компонентов к данным; имя компонента в скрипте обязано совпадать с именем на макете (например, ZipCode_to.text := <edtIndeks_to>;).

Семантика флагов (критично): Visible = false исключает элемент везде; Printable = false оставляет элемент на экране (рамки, подсказки, разлиновка) и отсекает при выводе на принтер.

Состояния: «Макет не загружен» (серый холст), «Загрузка», «Готов», «Конфликт зон» (красная штриховка пересечений и список предупреждений), «Сохранение запрещено» (элемент за границами печатной области).

Валидация: пересечение AABB любых двух полей; выход элемента за границы страницы; толщина линии индекса вне 0.2–0.5 мм; отсутствие шрифта ZIPcode в системе.

7.6. Диалог «Настройка таблицы» (три вкладки, вход — контекстное меню грида)
Вкладка 1 «Цвета и заголовки столбцов»: переименование заголовка по Enter, скрытие колонок снятием галочки;

Вкладка 2 «Атрибуты столбцов и строк»: ширина, выравнивание, порядок; глобальная галка «Полосатая таблица» — применяется сразу ко всем гридам программы;

Вкладка 3 «Сброс»: сброс к умолчаниям для конкретной таблицы или всей главной формы; сброс главной формы требует перезапуска — предупреждение выводится явно.

7.7. Три механизма поиска (все обязательны)
Форма поиска (Ctrl+F): последовательный перебор, кнопка «Найти далее», показывает одну следующую запись, не подсвечивает все совпадения;

Прямой набор с клавиатуры: курсор в таблице, записи не в режиме редактирования; по мере набора отображаются только совпавшие; сброс — Esc (допускается двойное нажатие Esc, так как условий поиска может быть несколько);

Фильтры: два раздельных механизма — встроенный «Избранное» (VIP) и пользовательские группы; сливать их в один нельзя.

Дополнительно: подбор значения поля из имеющихся записей — курсор в пустое поле, несколько нажатий BackSpace подставляют существующие значения этого поля.

7.8. Диалог «Архивирование и восстановление базы» (одна форма)
«Каталог для хранения архивов базы данных программы:» + кнопка обзора (любая папка Windows, включая сетевые и подключённые облачные диски);

«Выполнять архивацию ежедневно в:» — три поля времени (по умолчанию 09:00 / 13:05 / 16:05);

«Способ копирования»: (•) Хранить данные за каждый день (занимает больше места) / ( ) Хранить только последнюю сохранённую базу;

«Пароль к архиву:» с кнопкой-глазом (показать/скрыть); «Хранить дней:» со счётчиком и автоудалением старых копий;

Кнопки «Выполнить архивацию сейчас» и «Восстановить из архива», полоса прогресса, OK/Отмена.

Имя архива: Base_ГГММДДЧЧММ.zip (дата + время создания), упаковка штатным ZIP-архиватором, совместимым со «Сжатыми папками» Windows.

7.9. Экраны регистрации, обновлений и сервиса
Окно «Регистрация программы» — поле «Ключ:», статус (редакция, дата истечения), кнопки «Купить полную версию» / «Ввести ключ»;

NAG-экран пробного периода — «Пробная версия», «Осталось дней: N»;

Окно преимуществ редакции — при апгрейде показывается список получаемых возможностей, а не сообщение «недоступно в этой версии»;

Форма исключений — «В процессе работы программы обнаружена ошибка. Просьба сообщить о ней в службу поддержки» с готовым текстом для копирования и кнопкой «Служба поддержки»;

Диалоги сервиса: проверки обновлений (с кнопкой «Скачать обновление»), первого запуска (чистая база / демо-база), выбора путей, встроенные калькулятор и всплывающий календарь;

Диалог «Очистка базы» — выбор базы для очистки: «Категории контрагентов» / «База рассылок» / «База контрагентов» / «База отправленных писем».

7.10. Правила цветовой схемы и плотности
Плотность рабочего режима: кегль таблиц 8pt, высота строки ~17px, классический Windows-вид без анимации и «плоской» стилизации;

Три независимых настраиваемых цвета: фокус (focusedColor), выделение (selectedColor), подсказка (hintColor); значения по умолчанию: 4227327, 6205183, 3723516;

«Зебра» (чередование строк) — выключаемая глобально;

VIP-маркер — иконка в отдельной колонке; группировка — строки-разделители с иконкой папки;

Единый подход к оформлению на всю программу (либо классический вид, либо единый скин); смешивание стилей запрещено;

Сброс настроек отображения — из диалога «Настройка таблицы» с предупреждением о перезапуске.

8. Функциональные модули (Спецификация 163 функций)
8.1. Адресная книга и контрагенты
Функция 1. Карточка контрагента со всеми атрибутами
Модуль/Класс: Domain.Entities.Counterparty, UI.Forms.FormCounterpartyEdit (src/Domain/Entities/Counterparty.cs).

Сигнатура: public Result<Counterparty> UpdateDetails(CounterpartyDetailsDto dto)

Логика: Принимает DTO; валидирует ИНН (10 или 12 цифр по контрольным суммам), КПП (9 символов), БИК (9 цифр); сохраняет паспортные данные и банковские реквизиты.

Данные: Таблица counterparties. Если запись не найдена — генерация EntityNotFoundException.

Ошибки: «Некорректная контрольная сумма ИНН/БИК» — диалоговое предупреждение с подсветкой поля.

UI: Диалог редактирования контрагента, вкладки «Реквизиты», «Банк», «Паспорт».

Зависимости: Отсутствуют.

Проверка: Ввод заведомо ошибочного ИНН 7701234560 приводит к блокировке кнопки «Сохранить».

Функция 2. Разделение юридических и физических лиц
Модуль/Класс: Domain.Enums.CounterpartyType, UI.Forms.FormCounterpartyEdit

Сигнатура: public void SetCounterpartyType(CounterpartyType type)

Логика: При переключении типа на «Физическое лицо» скрываются поля КПП, ОГРН, БИК, активируются поля паспорта и даты рождения.

Данные: Колонка counterparties.type.

Ошибки: Попытка сохранить ЮЛ без КПП вызывает валидационное предупреждение.

UI: Радиокнопки (•) Юридическое лицо  ( ) Физическое лицо.

Зависимости: Функция 1.

Проверка: Выбор типа «ФЛ» деактивирует поле ввода КПП.

Функция 3. Три независимых адреса контрагента
Модуль/Класс: Domain.ValueObjects.AddressSet

Сигнатура: public void SetAddress(AddressType type, Address address, bool makeMain)

Логика: Сохраняет три независимых среза: юридический, фактический, почтовый. Флаг makeMain переключает целевой адрес для подстановки в конверты по умолчанию.

Данные: Колонки legal_*, fact_*, post_*, address_main_type.

Ошибки: Отсутствие основного адреса при отправке в печать — запрос на выбор альтернативного.

UI: Три независимые вкладки адресации в карточке контрагента.

Зависимости: Функция 1.

Проверка: Заполнение разных адресов в почтовой и юридической вкладках приводит к их раздельному сохранению в БД.

Функция 4. Структура адреса: индекс + 6 строк
Модуль/Класс: Domain.ValueObjects.Address

Сигнатура: public static Result<Address> Create(string zip, string[] lines)

Логика: Индекс проверяется на точное соответствие 6 цифровым символам. Массив строк ограничивается ровно 6 элементами длиной до 255 знаков каждый.

Данные: zip VARCHAR(6), str1..str6 VARCHAR(255).

Ошибки: Строка длиной свыше 255 символов обрезается с предупреждением.

UI: 6 отдельных текстовых полей TEdit со счетчиком символов.

Зависимости: Отсутствуют.

Проверка: Ввод 256 символов в поле «Строка 1» вызывает появление индикатора усечения.

Функция 5. Разбор индекса из строки адреса (parseZipIndexFromAddress)
Модуль/Класс: Application.Services.AddressParserService

Сигнатура: public ParsedAddress ParseRawString(string rawAddress, bool extractZip)

Логика: Регулярным выражением \b\d{6}\b извлекает почтовый индекс из произвольного неструктурированного текста адреса (сценарий интеграции с 1С), вырезает его из текста, остаток разбивает по запятым на строки 1..6.

Данные: Запись в структуру Address.

Ошибки: Индекс не найден — поле индекса остается пустым, адрес помечается флагом NeedsVerification.

UI: Чекбокс в диалоге импорта «Выкусывать почтовый индекс из строки адреса».

Зависимости: Функция 4.

Проверка: Текст «101000, г. Москва, ул. Мясницкая, д. 1» разделяется на индекс 101000 и строки адреса.

Функция 6. Нормализация и склонение русских ФИО и адресов
Модуль/Класс: Infrastructure.Text.RussianDeclensionEngine

Сигнатура: public string Declinename(string fio, GrammaticalCase gCase)

Логика: Выполняет грамматический анализ фамилии, имени, отчества и топонимов по падежам (Дательный для «Кому:», Родительный для «От кого:»).

Данные: Словари исключений и падежных окончаний.

Ошибки: Нераспознанное иностранное имя остается в исходном именительном падеже.

UI: Кнопка «Склонять» в карточке адреса.

Зависимости: Функция 1.

Проверка: «Иванов Иван Иванович» в дательном падеже преобразуется в «Иванову Ивану Ивановичу».

Функция 7. Пользовательские поля контрагента
Модуль/Класс: Application.Services.CustomFieldManager

Сигнатура: public void SetCustomValue(long counterpartyId, string fieldName, string value)

Логика: Обеспечивает сохранение динамических пар «ключ-значение» и вывод их тегов в печатные макеты.

Данные: Таблица counterparty_fields.

Ошибки: Дублирование имени ключа для одного контрагента — обновление существующего.

UI: Табличная сетка на дополнительной вкладке карточки контрагента.

Зависимости: Функция 1.

Проверка: Создание поля «КодКлиента» со значением «К-104» делает доступным тег [Custom_КодКлиента] в дизайнере.

Функция 8. Иерархические группы и категория «Неразобранные»
Модуль/Класс: Domain.Entities.Group, Application.Services.GroupManager

Сигнатура: public Result<Group> CreateGroup(string name, bool isDefault, bool isClosed)

Логика: Гарантирует наличие ровно одной дефолтной группы. Закрытая группа запрещает связывание с новыми адресами.

Данные: Таблица groups.

Ошибки: Попытка удаления группы «Неразобранные» блокируется.

UI: Дерево категорий в левой панели адресной книги.

Зависимости: Отсутствуют.

Проверка: Создание новой записи без указания категории автоматически присваивает group_id группы «Неразобранные».

Функция 9. Фильтры и сортировка по любой колонке
Модуль/Класс: Application.Queries.CounterpartyFilterQuery

Сигнатура: public IQueryable<Counterparty> ApplyFilter(IQueryable<Counterparty> baseQuery, FilterCriteria criteria)

Логика: Формирует динамический SQL WHERE/ORDER BY по выбранному столбцу, поддерживает сохранение именованных пресетов в parameters.

Данные: Таблица counterparties, индексы по name, inn, group_id.

Ошибки: Ввод некорректного формата даты в фильтре сбрасывает условие на дефолтное.

UI: Клик по заголовкам грида, диалог «Сохраненные фильтры».

Зависимости: Функция 1.

Проверка: Клик по столбцу «Индекс» переключает направление сортировки по возрастанию/убыванию.

Функция 10. Группировка строк в списке
Модуль/Класс: UI.Components.GroupableGrid

Сигнатура: public void GroupByColumn(string columnName)

Логика: Визуально схлопывает строки грида в раскрывающиеся секции по уникальным значениям поля с отображением количества элементов.

Данные: Клиентский кэш представления.

Ошибки: Нет.

UI: Перетаскивание заголовка столбца в верхнюю панель группировки.

Зависимости: Функция 9.

Проверка: Группировка по полю «Город» собирает контрагентов в узлы «г. Москва», «г. Минск».

Функция 11. Виртуальный постраничный просмотр (Pagination)
Модуль/Класс: Infrastructure.Persistence.VirtualDataBuffer<T>

Сигнатура: public PageResult<Counterparty> GetPage(int pageIndex, int pageSize)

Логика: Серверный пейджинг через LIMIT / OFFSET или Keysets. В память загружается только текущая отображаемая страница (50-200 записей).

Данные: Таблица counterparties.

Ошибки: Нет.

UI: Панель пагинации внизу грида: «Записи 1-100 из 150000».

Зависимости: Функция 1.

Проверка: На базе в 100 000 записей скроллинг грида не вызывает выделения более 150 МБ оперативной памяти.

Функция 12. Запоминание геометрии и состава колонок
Модуль/Класс: Application.Services.UiSettingsStorage

Сигнатура: public void SaveGridConfiguration(string gridId, GridConfigDto config)

Логика: Сериализует ширину, порядок и флаги видимости столбцов в JSON-файл профиля пользователя (AppData/Roaming/...).

Данные: Файл локальной конфигурации / таблица parameters.

Ошибки: Поврежденный конфигурационный файл автоматически заменяется дефолтным.

UI: Диалог «Настройка колонок таблицы».

Зависимости: Отсутствуют.

Проверка: Изменение ширины колонки сохраняется после полного перезапуска программы.

Функция 13. Поиск контрагента по ИНН/УНН в открытых реестрах
Модуль/Класс: Infrastructure.Integration.RegistryLookupClient

Сигнатура: public async Task<CompanyInfoResult> LookupByInnAsync(string inn, string countryCode)

Логика: Формирует запрос к открытым API агрегаторов (РФ) или ЕГР (РБ); разбирает полученный JSON/HTML и заполняет карточку контрагента.

Данные: Сетевой HTTP-вызов.

Ошибки: Таймаут или отсутствие сети — вывод сообщения: «Сервис проверки контрагента недоступен».

UI: Кнопка с пиктограммой лупы рядом с полем ИНН.

Зависимости: Функция 1.

Проверка: Ввод ИНН действующей организации заполняет наименование, КПП и адрес из открытых данных.

Функция 14. Поиск контрагента по внешним базам
Модуль/Класс: Infrastructure.Integration.ExternalDatabaseLookup

Сигнатура: public void ExecuteExternalSearch(string query)

Логика: Запуск внешней поисковой ссылки в системном браузере по конфигурируемому шаблону URL с подстановкой текущего ИНН.

Данные: Конфигурационный параметр URL.

Ошибки: Нет.

UI: Пункт меню «Сервис -> Найти в интернете».

Зависимости: Функция 1.

Проверка: Вызов команды открывает вкладку браузера с параметризованным запросом.

Функция 15. Массовое каскадное обновление адресов писем
Модуль/Класс: Application.Services.CascadeAddressUpdater

Сигнатура: public int UpdateUnprintedLettersAddress(long counterpartyId, Address newAddress)

Логика: Находит все записи mails в статусе 0 (Черновик), связанные с данным контрагентом, и заменяет строки адреса на актуальные.

Данные: Таблица mails.

Ошибки: Нет.

UI: Запрос-подтверждение: «Обновить адрес в 15 еще не напечатанных письмах?».

Зависимости: Функции 3, 20.

Проверка: После смены адреса контрагента адреса в напечатанных письмах остаются прежними, а в черновиках — обновляются.

Функция 16. Глобальный текущий отправитель
Модуль/Класс: Domain.Services.CurrentSenderContext

Сигнатура: public void SetGlobalSender(long senderId)

Логика: Сохраняет ID активного отправителя в реестре/сессии; подставляет его реквизиты во все вновь создаваемые документы.

Данные: Реестр HKCU\Software\PEPro\CurrentSender / таблица parameters.

Ошибки: Удаление текущего отправителя сбрасывает контекст на первую запись из справочника.

UI: Выпадающий список отправителей в левой верхней части главного окна.

Зависимости: Функция 1.

Проверка: Выбор отправителя сохраняется между перезапусками приложения.

Функция 17. Признак «Избранное» (VIP)
Модуль/Класс: Domain.Entities.Counterparty

Сигнатура: public void ToggleVipStatus(bool isVip)

Логика: Инвертирует булев флаг is_vip, что изменяет цвет маркера в таблице и позволяет применять быстрый фильтр F4.

Данные: Колонка counterparties.is_vip.

Ошибки: Нет.

UI: Чекбокс и иконка звездочки в гриде.

Зависимости: Функция 1.

Проверка: Включение флага отображает звездочку в строке таблицы.

Функция 18. Пакетное редактирование одного поля у выделенных записей
Модуль/Класс: Application.Services.BatchEditService

Сигнатура: public void BatchUpdateField(long[] ids, string propertyName, object newValue)

Логика: Выполняет массовое обновление указанного поля в рамках одной транзакции с предварительным подтверждением пользователем.

Данные: Таблица counterparties.

Ошибки: Ошибка блокировки строк транзакцией — откат операции.

UI: Диалог «Групповое изменение реквизита» (Ctrl+A -> Правка).

Зависимости: Функция 1.

Проверка: Выделение 50 строк и установка категории «VIP» изменяет значение у всех 50 контрагентов.

Функция 19. Мягкое удаление комбинацией Ctrl+Alt+F8
Модуль/Класс: Application.Services.CounterpartyManager

Сигнатура: public void FastDeleteMark(long[] ids)

Логика: По нажатию защищенного сочетания клавиш переводит флаг is_deleted = TRUE без отображения лишних промежуточных диалогов.

Данные: Таблица counterparties.

Ошибки: Нет.

UI: Исчезновение строк из грида с возможностью восстановления из меню «Сервис».

Зависимости: Функция 1.

Проверка: Нажатие клавиш помечает запись как удаленную, исключая ее из рабочих выборок.

8.2. Рассылки, письма, документооборот
Функция 20. Создание исходящего почтового отправления
Модуль/Класс: Domain.Entities.Mail, Application.Services.MailFactory

Сигнатура: public Mail CreateOutcoming(long counterpartyId, long senderId, string envelopeType)

Логика: Создает экземпляр отправления, фиксирует снимок адресов на момент создания, назначает статус «Создано».

Данные: Таблица mails.

Ошибки: Попытка создания без отправителя или получателя блокируется.

UI: Кнопка «Создать письмо» / пункт контекстного меню.

Зависимости: Функции 1, 16.

Проверка: В таблице mails появляется новая запись с status = 0.

Функция 21. Виды рассылок («1 ко многим» и «многие к 1»)
Модуль/Класс: Application.Services.DistributionService

Сигнатура: public void GenerateDistribution(DistributionPlan plan)

Логика: Создает пачку писем: либо от одного отправителя множеству контрагентов, либо от разных отправителей в адрес одного центрального органа.

Данные: Таблицы distributions, mails.

Ошибки: Пустой список адресатов — отмена генерации.

UI: Переключатель режимов в мастере рассылки.

Зависимости: Функция 20.

Проверка: Режим «многие к 1» генерирует N отправлений с разными sender_id и одинаковым counterparty_id.

Функция 22. Папки для писем
Модуль/Класс: Domain.Entities.MailFolder

Сигнатура: public void MoveToFolder(long mailId, long folderId)

Логика: Обеспечивает пользовательскую древовидную классификацию исходящей корреспонденции.

Данные: Таблица folders, колонка mails.folder_id.

Ошибки: Циклические ссылки в дереве папок пресекаются валидатором.

UI: Дерево папок во вкладке «Рассылки».

Зависимости: Функция 20.

Проверка: Перетаскивание письма в папку изменяет ее счетчик вложенности.

Функция 23. Классификация категории почтового отправления
Модуль/Класс: Domain.Enums.MailCategory

Сигнатура: public void SetCategory(MailCategory category)

Логика: Назначает тип: Простое, Заказное, С объявленной ценностью, 1-й класс. Влияет на набор доступных бланков и тарификацию.

Данные: Колонка mails.mail_category.

Ошибки: Нет.

UI: Выпадающий список «Категория отправления» в карточке письма.

Зависимости: Функция 20.

Проверка: Выбор «Заказное» делает обязательным печать отметок заказного письма.

Функция 24. Жизненный цикл отправления (4 даты)
Модуль/Класс: Domain.Entities.Mail

Сигнатура: public void TransitionStatus(MailStatus newStatus)

Логика: Контролирует последовательный переход состояний: date_created 
→ date_printed 
→ date_dispatched 
→ date_confirmation/date_canceled.

Данные: Временные метки mails.date_*.

Ошибки: Нельзя перевести в «Подтверждено», минуя «Отправлено».

UI: Вкладка истории статусов в карточке отправления.

Зависимости: Функция 20.

Проверка: Выполнение печати автоматически проставляет дату в поле date_printed.

Функция 25. Сквозная регистрационная нумерация
Модуль/Класс: Application.Services.RegistrationNumberGenerator

Сигнатура: public string GenerateNextOutgoingNumber(string templateMask)

Логика: Атомарно инкрементирует счетчик отправлений и форматирует строку по шаблону (например, ИСХ-{YYYY}/{SEQ:5}).

Данные: Таблица parameters.

Ошибки: Нет.

UI: Поле «Исходящий №» с автогенерацией.

Зависимости: Функция 20.

Проверка: Два параллельных запроса получают уникальные последовательные номера без коллизий.

Функция 26. Печать отметок «Заказное» / «С уведомлением»
Модуль/Класс: Printing.Elements.ServiceStampRenderer

Сигнатура: public void RenderStamps(IGraphicsContext g, Mail mail, BlankField field)

Логика: Если в отправлении активны соответствующие флаги, в отведенной зоне конверта выводится стандартный штамп в рамке (высота 10 мм, шрифт полужирный 12pt).

Данные: Флаги need_notification, mail_category.

Ошибки: Нет.

UI: Чекбоксы «Заказное», «С уведомлением».

Зависимости: Функции 23, 38.

Проверка: Включение флага выводит на предпросмотре рамку со словом «ЗАКАЗНОЕ».

Функция 27. Печать ценности, веса и тарифа в реестре ф.103
Модуль/Класс: Printing.Reports.RegistryF103Builder

Сигнатура: public void PopulateItemRow(F103Row row, Mail mail)

Логика: Форматирует вес в кг с тремя десятичными знаками, ценность и плату — в рублях с копейками; рассчитывает суммарные итоги по листу и реестру.

Данные: Колонки declared_value, weight, fee.

Ошибки: Отрицательные значения веса блокируются валидатором.

UI: Поля ввода сумм в карточке письма.

Зависимости: Функции 20, 50.

Проверка: Сумма ценностей всех строк реестра точно сходится с итоговой графой.

Функция 28. Редактируемая опись вложений (ф.107)
Модуль/Класс: Domain.Entities.Inventory, Domain.Entities.InventoryItem

Сигнатура: public void AddItem(string name, int count, decimal cost, string note)

Логика: Позволяет формировать построчный перечень предметов вложения с указанием количества и оценочной стоимости каждого.

Данные: Таблицы inventories, inventory_items.

Ошибки: Количество 
≤0 блокируется.

UI: Табличный редактор описи вложений с автосуммированием.

Зависимости: Функция 20.

Проверка: Итоговая сумма описи совпадает с суммой declared_value отправления.

Функция 29. Регистрация и сканирование входящей корреспонденции
Модуль/Класс: Application.Services.IncomingMailService

Сигнатура: public void RegisterIncoming(string docNumber, Counterparty sender, byte[] scannedImage)

Логика: Создает входящую карточку, сохраняет бинарный скан документа (TIFF/PDF) с привязкой к контрагенту.

Данные: Таблица mails (с заполненным incoming_number).

Ошибки: Превышение лимита размера файла скана (более 50 МБ) прерывается предупреждением.

UI: Форма «Регистрация входящего документа».

Зависимости: Функции 1, 48 (сканирование).

Проверка: Загруженный скан открывается во встроенном средстве просмотра документов.

Функция 30. Журнал движения документов
Модуль/Класс: Application.Services.DocumentMovementTracker

Сигнатура: public void RecordMovement(long mailId, string targetDepartment, string executor)

Логика: Ведет аудит передачи бумажных документов между отделами/исполнителями.

Данные: Таблица document_movements.

Ошибки: Нет.

UI: Подтаблица «Движение документа» в карточке входящего.

Зависимости: Функция 29.

Проверка: Передача документа сотруднику фиксирует дату и имя пользователя в истории.

Функция 31. Массовая генерация документов по шаблону
Модуль/Класс: Application.Services.DocumentBatchGenerator

Сигнатура: public void GenerateLettersBatch(long[] counterpartyIds, string templatePath)

Логика: Выполняет подстановку реквизитов каждого выбранного контрагента в шаблон документа (WordprocessingML / RTF) с сохранением готовых файлов.

Данные: Шаблоны документов, реквизиты контрагентов.

Ошибки: Ошибка в разметке тега шаблона приводит к пропуску тега без падения процесса.

UI: Пункт меню «Генерация документов по шаблону».

Зависимости: Функции 1, 65.

Проверка: Пакет из 10 контрагентов генерирует 10 корректно заполненных файлов DOCX/RTF.

Функция 32. Выбор вида уведомления о вручении
Модуль/Класс: Domain.Enums.NotificationType

Сигнатура: public void SelectNotificationForm(NotificationType type)

Логика: Позволяет оператору выбрать конкретную форму бланка уведомления: ф.119 (РФ), национальную форму или совмещенный перевод ф.112эп.

Данные: Колонка mails.notice_type.

Ошибки: Нет.

UI: Выпадающий список выбора типа уведомления.

Зависимости: Функция 20.

Проверка: Выбор ф.119 загружает двухсторонний макет уведомления 148х105 мм.

Функция 33. Формирование адресных строк и реверс порядка
Модуль/Класс: Application.Formatting.AddressLineFormatter

Сигнатура: public string[] FormatLines(Address address, AddressOrder order, int maxLines)

Логика: Выстраивает строки в порядке «От большего к меньшему» (РФ: Область 
→ Город 
→ Улица) или «От меньшего к большему» (Международный: Улица 
→ Город 
→ Страна); переносит длинные слова.

Данные: Структура Address.

Ошибки: Нет.

UI: Переключатель в диалоге печати.

Зависимости: Функция 4.

Проверка: Выбор европейского порядка ставит строку улицы на первое место.

Функция 34. Автоподбор кегля текста (Font Fitting)
Модуль/Класс: Printing.Engine.TextFitter

Сигнатура: public float CalculateOptimalFontSize(Graphics g, string text, Font font, SizeF bounds, float minPt, float maxPt)

Логика: Итеративно уменьшает кегль шрифта с шагом 0.5pt от maxPt до тех пор, пока текст с учетом переносов не впишется в bounds, либо пока не будет достигнут minPt.

Данные: Свойства поля бланка.

Ошибки: Если при minPt текст все равно не помещается, на превью выводится маркер переполнения.

UI: Свойства поля в редакторе бланков.

Зависимости: Функция 38.

Проверка: Длинное наименование организации автоматически уменьшается с 10pt до 7.5pt для сохранения в границах поля.

Функция 35. Запоминание последних введенных параметров
Модуль/Класс: Application.Services.RecentParametersStorage

Сигнатура: public void SaveRecentPrintParams(PrintParamsDto dto)

Логика: Сохраняет последние значения форматов конвертов, веса и выбранного принтера в локальный профиль для быстрой одиночной печати.

Данные: Реестр HKCU / parameters.

Ошибки: Нет.

UI: Автозаполнение полей диалога печати при повторном открытии.

Зависимости: Отсутствуют.

Проверка: После печати конверта DL следующее открытие формы сразу предлагает формат DL.

8.3. Бланки (Шаблоны печати)
Функция 36. Восемь встроенных эталонных бланков
Модуль/Класс: Infrastructure.Templates.BuiltinTemplatesCatalog

Сигнатура: public PrintTemplate GetBuiltinTemplate(BuiltinFormType form)

Логика: Содержит неизменяемые координатные разметки и векторные слои для: ф.112эп, ф.112э, ф.112эф, ф.112эю, ф.117+ф.113, ф.116, ф.119 (2011), C5 (2012).

Данные: Ресурсы сборок (.lbk).

Ошибки: Попытка перезаписи встроенного бланка сохраняет его как пользовательскую копию.

UI: Раздел «Стандартные бланки» в каталоге шаблонов.

Зависимости: Функция 39.

Проверка: Выбор ф.116 генерирует двухстраничный документ А4 с точным позиционированием 83 полей.

Функция 37. Библиотека пакетов бланков (не менее 20 штук)
Модуль/Класс: Infrastructure.Templates.TemplatePackageRepository

Сигнатура: public IEnumerable<PrintTemplate> LoadAllPackages(string directoryPath)

Логика: Сканирует каталог шаблонов, валидирует контрольные суммы и загружает реестр доступных конвертов (C4, C5, C6, DL, B4), доверенностей, описей и заявлений.

Данные: Каталог Templates/Packages/*.lbk.

Ошибки: Поврежденный пакет помечается как невалидный и исключается из списка без падения программы.

UI: Галерея бланков с миниатюрами.

Зависимости: Функция 39.

Проверка: Добавление файла .lbk в папку делает новый бланк доступным в UI без перезапуска приложения.

Функция 38. Встроенный визуальный редактор бланков
Модуль/Класс: UI.Forms.FormBlankEditor, Printing.Editor.CanvasController

Сигнатура: public void OpenEditor(PrintTemplate template)

Логика: Предоставляет интерактивный холст для манипулирования полями мышью: позиционирование, масштабирование, привязка к сетке, палитра свойств (шрифт, цвет, рамка, выравнивание).

Данные: Файл blank.xml.

Ошибки: Выход элементов за границы печатной страницы блокирует сохранение с предупреждением.

UI: Окно редактора (F8) с панелью инструментов и инспектором свойств.

Зависимости: Функции 39, 41.

Проверка: Перемещение поля «Кому» мышью сохраняет новые координаты x_mm, y_mm в XML.

Функция 39. Структурированный XML-формат бланка
Модуль/Класс: Domain.Serialization.BlankXmlSerializer

Сигнатура: public string SerializeBlank(CustomBlank blank); public CustomBlank Deserialize(string xml)

Логика: Строго валидирует структуру XML против встроенной XSD-схемы; сериализует размеры, страницы, фоновые пути и спецификации полей.

Данные: XML-файлы бланков.

Ошибки: Несоответствие XSD вызывает отказ загрузки с указанием строки ошибки.

UI: Нет.

Зависимости: Отсутствуют.

Проверка: Сохраненный XML открывается и полностью валидируется стандартным парсером XmlDocument.

Функция 40. Импорт/экспорт пакета .lbk (ZIP-контейнер)
Модуль/Класс: Infrastructure.IO.LbkPackageArchiver

Сигнатура: public void ExportPackage(PrintTemplate template, string targetZipPath); public PrintTemplate ImportPackage(string sourceZipPath)

Логика: Пакует blank.xml, query.xml и растровые подложки в единый ZIP-архив с расширением .lbk и распаковывает его с проверкой целостности.

Данные: Файловая система.

Ошибки: Попытка открытия поврежденного ZIP-архива — диалог «Ошибка структуры пакета».

UI: Кнопки «Экспорт бланка» / «Импорт бланка» в каталоге.

Зависимости: Функция 39.

Проверка: Экспортированный .lbk корректно импортируется на другой рабочей станции.

Функция 41. Почтовый индекс как специализированное поле
Модуль/Класс: Printing.Elements.PostIndexElement

Сигнатура: public void RenderPostIndex(IGraphicsContext g, string zipCode, PostIndexSpecification spec)

Логика: Отрисовывает кодовый штамп шрифтом ZIPcode.ttf с жестким контролем ширины реперных линий (
0.45 мм), шага знаков (
5.4 мм) и отрисовкой контурных сеток.

Данные: Значения индексов контрагентов.

Ошибки: Символы, отличные от цифр, отбрасываются; длина строки индекса строго проверяется на 6 знаков.

UI: Специфический тулбар настроек индекса в дизайнере.

Зависимости: Функция 38.

Проверка: Индекс «123456» выводится шестью отдельными трафаретными знакоместами по ГОСТ.

Функция 42. Поддержка полной номенклатуры форматов конвертов
Модуль/Класс: Domain.Enums.EnvelopeFormat

Сигнатура: public EnvelopeGeometry GetGeometry(EnvelopeFormat format, EnvelopeOrientation orientation)

Логика: Предоставляет точные метрики (мм): DL (220x110), C5 (229x162), C4 (324x229), C6 (162x114), B4 (353x250), E65, K-серия во всех ориентациях (вертикальная / горизонтальная / обратная).

Данные: Системный справочник геометрий.

Ошибки: Нет.

UI: Выпадающий список форматов в диалоге печати.

Зависимости: Отсутствуют.

Проверка: Переключение формата с C6 на C4 изменяет размеры рабочей области макета на 324х229 мм.

Функция 43. Обратная печать конверта
Модуль/Класс: Printing.Engine.EnvelopeInverter

Сигнатура: public void InvertAddresses(CustomBlank blank)

Логика: Меняет местами зоны расположения адреса отправителя и получателя для печати на клапане конверта или специализированных обратных отправлениях.

Данные: Экземпляр CustomBlank.

Ошибки: Нет.

UI: Чекбокс «Обратный конверт» в настройках печати.

Зависимости: Функция 38.

Проверка: Активация флага переносит блок «Кому» в левый верхний угол, а «От кого» — в правый нижний.

Функция 44. Печать нескольких конвертов/наклеек на одном листе
Модуль/Класс: Printing.Engine.SheetLayoutImposition

Сигнатура: public void LayoutGridOnSheet(PrintJobContext ctx, int rows, int cols, Margins mmMargins)

Логика: Выполняет электронный спуск полос для формирования сетки этикеток на листах формата A4/A3 с расчетом интервалов между блоками.

Данные: Список выбранных отправлений.

Ошибки: Если размеры наклеек превышают габариты листа, выводится ошибка разбивки.

UI: Диалог настройки раскладки адресных этикеток.

Зависимости: Функция 146.

Проверка: Сетка 3х7 на листе А4 формирует 21 адресную наклейку за один проход печати.

Функция 45. Индивидуальные калибровочные смещения
Модуль/Класс: Domain.Entities.PrinterOffsetProfile

Сигнатура: public void SaveOffset(string printerName, EnvelopeFormat format, float offsetXmm, float offsetYmm)

Логика: Сохраняет в профиле тонкую юстировку печатающей головки (шаг 0.1 мм) независимо для каждой связки «Принтер + Формат конверта».

Данные: Таблица printer_profiles.

Ошибки: Нет.

UI: Числовые счетчики «Смещение X (мм)», «Смещение Y (мм)» в диалоге печати.

Зависимости: Функция 78.

Проверка: Установка смещения X=+2.5 мм сдвигает весь печатный вывод вправо ровно на 2.5 мм.

Функция 46. Программное выравнивание подачи конверта
Модуль/Класс: Printing.Engine.FeedAlignmentCalculator

Сигнатура: public PointF CalculateOrigin(FeedAlignment alignment, SizeF sheetSize, SizeF envelopeSize)

Логика: Рассчитывает смещение начала координат в зависимости от конструкции лотка принтера: по центру, по левому краю, по правому краю.

Данные: Профиль принтера.

Ошибки: Нет.

UI: Иконки выбора способа подачи в лоток (Центр / Край).

Зависимости: Функция 45.

Проверка: Выбор центральной подачи конверта C6 в лоток А4 автоматически смещает изображение в центр оси X.

Функция 47. Наложение растрового логотипа
Модуль/Класс: Printing.Elements.LogoRenderer

Сигнатура: public void DrawLogo(IGraphicsContext g, byte[] logoBytes, RectangleF targetBounds, bool keepAspectRatio)

Логика: Загружает растр (PNG/BMP/JPEG), пропорционально масштабирует его в границы поля и накладывает с учетом прозрачности.

Данные: Таблица counterparties (колонка logo).

Ошибки: Поврежденный растровый файл замещается заглушкой с предупреждением в логе.

UI: Переключатель «(•) С логотипом  ( ) Без логотипа».

Зависимости: Функция 38.

Проверка: Выключение чекбокса исключает вывод логотипа из спулера.

Функция 48. Динамический QR-код на конверте
Модуль/Класс: Printing.Elements.QrCodeRenderer

Сигнатура: public void RenderQr(IGraphicsContext g, string payload, RectangleF bounds)

Логика: Извлекает или формирует монохромный QR-код без интерполяции с проверкой на отсутствие пересечений с логотипом.

Данные: Таблица qr_cache.

Ошибки: Превышение лимита символов для выбранной версии QR-кода вызывает ошибку генерации.

UI: Компонент «QR-код» на палитре дизайнера бланков.

Зависимости: Функция 38.

Проверка: Сканирование напечатанного конверта мобильным сканером считывает точную строку реквизитов платежа/адреса.

Функция 49. Стандартные прикладные бланки (Доверенность, Заявление)
Модуль/Класс: Infrastructure.Templates.SpecialFormsBuilder

Сигнатура: public void BuildSpecialForm(SpecialFormType type, Counterparty counterparty)

Логика: Заполняет текстовые поля форм «Доверенность на получение почты» и «Заявление кредитора» реквизитами организации и паспортом руководителя.

Данные: Шаблоны пакета Templates.

Ошибки: Отсутствие паспортных данных выводит диалог предупреждения.

UI: Меню «Печать -> Специальные бланки».

Зависимости: Функции 1, 37.

Проверка: Формирование доверенности генерирует заполненный лист А4 с реквизитами доверенного лица.

8.4. Реестры, описи, отчеты
Функция 50. Форма реестра почтовых отправлений ф.103
Модуль/Класс: Printing.Reports.Form103Report

Сигнатура: public ReportDocument GenerateF103(long[] mailIds, long senderId)

Логика: Формирует регламентный отчет Почты России: титульная часть с реквизитами отправителя, табличная часть начислений, расчет платы за пересылку, блок подписей почтовых работников.

Данные: Запрос qrReport_F103.

Ошибки: Письма от разных отправителей в один реестр включить нельзя (ошибка валидации выборки).

UI: Пункт меню «Печать реестра ф.103».

Зависимости: Функции 20, 27.

Проверка: Печатная форма полностью соответствует нормативному бланку Почты России.

Функция 51. Расширенный реестр отправлений
Модуль/Класс: Printing.Reports.ExtendedRegistryReport

Сигнатура: public ReportDocument GenerateExtendedRegistry(ReportFilter filter)

Логика: Формирует управленческий реестр с дополнительными графами: номер телефона, внутренний номер дела, примечание, статус доставки.

Данные: Таблицы mails, counterparties.

Ошибки: Нет.

UI: Выбор варианта отчета в мастере отчетов.

Зависимости: Функция 50.

Проверка: В отчете отображаются пользовательские примечания по каждому отправлению.

Функция 52. Стандартный сокращенный реестр
Модуль/Класс: Printing.Reports.SimpleRegistryReport

Сигнатура: public ReportDocument GenerateSimpleRegistry(long[] mailIds)

Логика: Облегченная версия реестра для курьерских служб (Список: №, Получатель, Адрес, Подпись в получении).

Данные: Таблица mails.

Ошибки: Нет.

UI: Пункт «Простой реестр передачи».

Зависимости: Функция 20.

Проверка: Документ выводится в 1 страницу при партии до 35 писем.

Функция 53. Стандартная опись вложений ф.107
Модуль/Класс: Printing.Reports.InventoryF107Report

Сигнатура: public ReportDocument GenerateF107(long inventoryId)

Логика: Генерирует 2 экземпляра бланка ф.107 на одном листе (для почты и для отправителя) со списком предметов, количеством и суммой ценности.

Данные: Запрос qrReport_Inventory107.

Ошибки: Опись без позиций вложения не формируется.

UI: Кнопка «Печать описи ф.107» в карточке письма.

Зависимости: Функция 28.

Проверка: На листе А4 печатаются два идентичных блока описи с отметками «Экземпляр отправителя» и «Экземпляр почты».

Функция 54. Журнал регистрации входящей корреспонденции
Модуль/Класс: Printing.Reports.IncomingJournalReport

Сигнатура: public ReportDocument BuildIncomingJournal(DateTime from, DateTime to)

Логика: Формирует сброшюрованный журнал учета входящих писем с номерами, датами, контрагентами и отметками об исполнении.

Данные: Запрос qrReport_IncomingJournal.

Ошибки: Нет.

UI: Меню «Отчеты -> Журнал входящих».

Зависимости: Функция 29.

Проверка: В журнал попадают все входящие документы за указанный период с сортировкой по дате.

Функция 55. Реестр входящей корреспонденции формата 1С
Модуль/Класс: Printing.Reports.OneCIncomingReport

Сигнатура: public ReportDocument BuildOneCCompatibleRegistry(DateTime date)

Логика: Проекция данных журнала входящих в структуру колонок конфигурации «1C:Документооборот».

Данные: Таблица mails.

Ошибки: Нет.

UI: Выбор формата отчета «Спецификация 1С».

Зависимости: Функция 54.

Проверка: Структура столбцов отчета совпадает с требованиями импорта 1С.

Функция 56. Пользовательские шаблоны реестров и описей
Модуль/Класс: Application.Services.CustomReportManager

Сигнатура: public void RegisterUserReport(string reportName, string templatePath, string queryXmlPath)

Логика: Позволяет технологу добавлять в систему произвольные печатные отчеты на базе связки «движок отчётов/XSLT + query.xml»: макет берется из пользовательского пакета, проекция данных — из прилагаемого query.xml.

Данные: Папка Templates/Reports/User/.

Ошибки: Синтаксическая ошибка в пользовательском шаблоне изолируется и выводится в диалоге валидации.

UI: Диалог «Пользовательские печатные формы».

Зависимости: Функции 38, 67.

Проверка: Новый пользовательский отчет появляется в общем меню отчетов.

Функция 57. Дополнительные почтовые формы (Форма 5, Форма 6)
Модуль/Класс: Printing.Reports.AuxiliaryPostReports

Сигнатура: public ReportDocument GenerateAuxForm(int formNumber, long[] mailIds)

Логика: Формирует специализированные ведомственные бланки сдачи партионной почты.

Данные: Таблица mails.

Ошибки: Нет.

UI: Подменю «Дополнительные формы Почты».

Зависимости: Функция 50.

Проверка: Печать Формы 5 выводит регламентную сопроводительную ведомость.

Функция 58. Сохранение промежуточных данных реестра во временный XML
Модуль/Класс: Application.Services.ReportDataDumper

Сигнатура: public string DumpReportDataToTempXml(object reportDataSet, string reportName)

Логика: Перед формированием визуального отчета сбрасывает датасет в %TEMP%\PEPro_Report_<GUID>.xml, позволяя пользователю вручную отредактировать данные перед спулингом.

Данные: Временная файловая система.

Ошибки: Ошибка записи во временную папку приводит к переходу на генерацию в памяти.

UI: Чекбокс в настройках отчетов «Сохранять промежуточный XML данных».

Зависимости: Отсутствуют.

Проверка: Во временном каталоге создается валидный XML-файл с данными текущего реестра.

Функция 59. Настраиваемая сортировка писем в реестре
Модуль/Класс: Application.Services.RegistrySorter

Сигнатура: public IEnumerable<Mail> SortMails(IEnumerable<Mail> mails, RegistrySortMode mode)

Логика: Сортирует список строк реестра: по исходящему номеру, по алфавиту получателей, по индексу места назначения.

Данные: Коллекция сущностей Mail.

Ошибки: Нет.

UI: Радиокнопки выбора порядка сортировки в мастере реестра.

Зависимости: Функция 50.

Проверка: Сортировка по алфавиту ставит получателя «Абрамов» выше «Борисов» независимо от даты создания письма.

Функция 60. Вывод номеров связанных документов через запятую
Модуль/Класс: Application.Formatting.DocumentNumbersAggregator

Сигнатура: public string AggregateDocNumbers(IEnumerable<string> numbers)

Логика: Склеивает множество номеров первичных документов в единую строку с разделителем ,  и контролем длины ячейки.

Данные: Таблицы mails, mail_documents.

Ошибки: Нет.

UI: Колонка «Номера документов» в таблице реестра.

Зависимости: Функция 50.

Проверка: Коллекция из номеров №12, №15, №18 выводится строкой №12, №15, №18.

Функция 61. Прямой экспорт текущей таблицы в MS Excel
Модуль/Класс: Infrastructure.Export.SpreadsheetMlExporter

Сигнатура: public void ExportGridToExcel(DataGridView grid, string filePath)

Логика: Формирует файл SpreadsheetML / OpenXML XLSX напрямую через поток без вызова OLE/COM Excel; открывает файл в ассоциированном приложении.

Данные: Текущее представление грида.

Ошибки: Если файл заблокирован другой программой, запрашивается альтернативное имя.

UI: Кнопка «Открыть список в Excel» над гридом.

Зависимости: Функция 69.

Проверка: Файл успешно открывается в Excel на машине, где не установлен пакет MS Office (через веб/сторонние вьюверы).

Функция 62. Печать реестра без показа формы (Тихий режим)
Модуль/Класс: Application.Services.SilentPrintService

Сигнатура: public void PrintRegistrySilently(long[] mailIds, string templateName, string printerName)

Логика: Выполняет сбор данных, рендеринг и прямую отправку реестра в Windows Spooler без инициализации GUI (для автоматизации из внешних систем).

Данные: Запрос реестра.

Ошибки: Ошибки спулера возвращаются кодом ошибки в вызывающий процесс.

UI: Нет (фоновый режим).

Зависимости: Функции 50, 78.

Проверка: Вызов метода отправляет задание на принтер, не создавая окон на рабочем столе.

Функция 63. Печатные формы статистики и групп
Модуль/Класс: Printing.Reports.StatsAndGroupReports

Сигнатура: public ReportDocument GenerateGroupReport(long groupId)

Логика: Формирует сводный отчет по адресам заданной группы с итоговым числом контрагентов и распределением по регионам.

Данные: Запрос qrReport_GroupEnvelopes.

Ошибки: Пустая группа формирует отчет с уведомлением «Нет данных».

UI: Пункт контекстного меню группы «Печать состава группы».

Зависимости: Функция 8.

Проверка: Отчет отображает всех членов выбранной категории с их индексами.

Функция 64. Неизменяемая история печати конвертов
Модуль/Класс: Domain.Entities.PrintHistory, Application.Services.HistoryTracker

Сигнатура: public void RecordPrintEvent(PrintHistoryRecord record)

Логика: При каждом физическом выводе конверта на принтер фиксирует неизменяемый снимок адресных строк получателя и отправителя, имя бланка и дату.

Данные: Таблица print_history.

Ошибки: Сбой записи в аудит отменяет транзакцию печати.

UI: Вкладка «Отправленные» / окно «История печати».

Зависимости: Функции 20, 88.

Проверка: Последующее изменение адреса контрагента в карточке не изменяет адрес в исторической записи print_history.

8.5. Шаблоны документов и экспорт
Функция 65. Генерация документов произвольного вида
Модуль/Класс: Application.Services.ArbitraryDocumentEngine

Сигнатура: public byte[] GenerateDocument(string templatePath, Dictionary<string, object> parameters)

Логика: Обрабатывает шаблоны свободной формы (уведомления, претензии, акты), заменяя маркеры #Field# на значения из модели данных.

Данные: Файловая система.

Ошибки: Отсутствующий файл шаблона вызывает FileNotFoundException.

UI: Меню «Сервис -> Сформировать документ».

Зависимости: Функция 1.

Проверка: Маркер #Counterparty_Name# в тексте шаблона заменяется на наименование организации.

Функция 66. Мастер редактирования шаблонов документов
Модуль/Класс: UI.Forms.FormDocTemplateWizard

Сигнатура: public void EditTemplateMarkers(string templatePath)

Логика: Предоставляет UI для предпросмотра структуры документа, автоматической вставки доступных маркеров БД и проверки структуры получаемого XML.

Данные: Шаблоны XSLT / OpenXML.

Ошибки: Нет.

UI: Окно мастера редактирования шаблонов.

Зависимости: Функция 65.

Проверка: Двойной клик по маркеру в списке вставляет его текст в позицию курсора редактора.

Функция 67. Шаблоны документов на связке XML + XSLT
Модуль/Класс: Infrastructure.Templating.XsltTransformEngine

Сигнатура: public string TransformToOutput(string xmlData, string xsltPath)

Логика: Выполняет XSL-преобразование потока промежуточных XML-данных через таблицы стилей XSLT в целевые форматы разметки.

Данные: 12 XML спецификаций + 16 XSLT файлов.

Ошибки: Ошибка парсинга XSLT прерывает трансформацию с указанием строкового смещения.

UI: Нет.

Зависимости: Отсутствуют.

Проверка: Преобразование XML данных рассылки через XSLT-таблицу выдает корректный документ WordprocessingML.

Функция 68. Экспорт в Microsoft Word без установленного MS Office
Модуль/Класс: Infrastructure.Export.WordprocessingMlExporter

Сигнатура: public void ExportToDocx(string sourceXml, string xsltTemplate, string outputDocxPath)

Логика: Генерирует стандартный файл Word (DOCX / WordprocessingML) путем потоковой упаковки XML-дерева в Open Packaging Conventions (OPC) ZIP без использования COM Interop.

Данные: Файловый вывод.

Ошибки: Недостаток места на диске вызывает IOException.

UI: Кнопка «Сохранить в Word».

Зависимости: Функция 70.

Проверка: Сформированный файл открывается в любом OpenXML-совместимом просмотрщике без наличия Microsoft Office в системе.

Функция 69. Экспорт в Microsoft Excel без установленного MS Office
Модуль/Класс: Infrastructure.Export.SpreadsheetMlExporter

Сигнатура: public void ExportToXlsx(DataTable table, string outputXlsxPath)

Логика: Собирает валидный архив XLSX (таблицы стилей, рабочий лист, книга, shared strings) с автоматическим форматированием типов данных (числа, даты, текст).

Данные: Файловый вывод.

Ошибки: Нет.

UI: Кнопка «Сохранить в Excel».

Зависимости: Функция 70.

Проверка: Числовые колонки импортируются в Excel как формульные числа, а не как текст с апострофом.

Функция 70. Полная независимость экспорта от Microsoft Office
Модуль/Класс: Infrastructure.Export.NativeOpenXmlPackager

Сигнатура: public bool ValidateOfficeAbsenceCompatibility()

Логика: Архитектурный запрет на подключение сборок Microsoft.Office.Interop.* в проект. Все конвертации выполняются на базе System.IO.Compression и XSLT.

Данные: Исполняемые сборки.

Ошибки: Нет.

UI: Нет.

Зависимости: Функции 68, 69.

Проверка: Запуск и выполнение экспорта на сервере под управлением Windows Server Core без компонентов Desktop Experience и Office выполняется успешно.

Функция 71. Импорт и экспорт адресных списков в форматах TXT/CSV/HTML/RTF/XLS/XLSX
Модуль/Класс: Application.Services.UniversalAddressExchange

Сигнатура: public void ExportData(IEnumerable<Counterparty> data, ExchangeFormat format, Stream output)

Логика: Универсальный транслятор данных; поддерживает разделители запятая/точка с запятой/табуляция, кодировки UTF-8 и CP1251.

Данные: Справочник counterparties.

Ошибки: Поврежденная структура файла вызывает ошибку импорта строки с логированием номера строки.

UI: Диалоги «Экспорт адресов» / «Импорт адресов».

Зависимости: Функция 1.

Проверка: Экспорт в CSV с последующим импортом в чистую базу полностью восстанавливает все поля адресов.

Функция 72. Импорт контрагентов из выгрузок 1С:Предприятие 7.7 и 8.x
Модуль/Класс: Application.Services.OneCImportParser

Сигнатура: public ImportBatchResult ImportFromOneC(string filePath, OneCVersion version)

Логика: Распознает стандартные XML и текстовые форматы экспорта справочника «Контрагенты» 1С 7.7/8.x; извлекает реквизиты, телефоны, банковские счета и составные строки адресов.

Данные: Входные файлы 1С.

Ошибки: Отсутствие тега контрагента пропускает блок с предупреждением.

UI: Пункт меню «Данные -> Импорт из 1С».

Зависимости: Функции 1, 5.

Проверка: Файл выгрузки контрагентов 1С 8.3 импортирует организации с их ИНН и адресами.

Функция 73. Гибкий редактор настроек импорта CSV любой конфигурации
Модуль/Класс: UI.Forms.FormCsvImportConfig

Сигнатура: public CsvProfile ConfigureCsvMapping(string previewText)

Логика: Интерактивное определение кодировки (ANSI/UTF-8), символа разделителя, символа экранирования кавычек, строки начала данных; сохранение профилей сопоставления.

Данные: Файлы конфигурации *.csvprof.

Ошибки: Несовпадение числа колонок в строке помечает строку как ошибочную.

UI: Форма настройки параметров разбора CSV с живым гридом предпросмотра.

Зависимости: Функция 71.

Проверка: Выбор
 разделителя «точка с запятой» и кодировки UTF-8 в профиле импорта позволяет загрузить файл, выгруженный европейской локалью, без ручной правки.

Функция 74. «Тонкое слияние» импортируемых данных с существующей базой
Модуль/Класс: Application.Services.ImportMergeService (src/Application/Services/ImportMergeService.cs)

Сигнатура: public MergeReport MergeImported(ImportBatch batch, MergePolicy policy)

Логика: Вход → поиск кандидата на слияние по естественному ключу (ИНН, при отсутствии — нормализованное имя + индекс). Проверки → совпадение ключа. Действия → по политике: (0) пропустить существующего; (1) дополнить пустые поля существующего импортными значениями; (2) перезаписать непустые значения. Результат → отчет MergeReport (создано / обновлено / пропущено / конфликтов).

Данные: Таблицы counterparties, counterparty_fields; ключи сравнения — inn, name+to_zip.

Ошибки: Конфликт непустых значений при политике «дополнять» фиксируется в отчете, значение не перезаписывается.

UI: Страница мастера импорта «Политика слияния» с тремя переключателями и сводкой после выполнения.

Зависимости: Функции 71, 73.

Проверка: Повторный импорт того же файла при политике «пропускать существующих» не увеличивает число записей в адресной книге.

Функция 75. Дедупликация импорта и источник данных каждого контрагента
Модуль/Класс: Application.Services.ImportDeduplicator

Сигнатура: public DeduplicationResult FilterDuplicates(IEnumerable<CounterpartyDraft> drafts)

Логика: Отбрасывает повторяющиеся записи внутри пакета (по ключу слияния) и уже существующие в БД (если политика не «обновлять»); каждому созданному контрагенту записывает признак source — строка «Импорт <формат> от <дата> из <файл>». По завершении выводит отчетную информацию: сколько добавлено, сколько пропущено как повтор, сколько обновлено.

Данные: Колонка counterparties.source (VARCHAR(500)).

Ошибки: Нет.

UI: Итоговый экран мастера импорта со сводкой; колонка «Источник» в карточке контрагента.

Зависимости: Функция 74.

Проверка: В карточке контрагента, созданной импортом, отображается источник и дата импорта.

Функция 76. Импорт контрагентов из отдельной внешней БД
Модуль/Класс: Infrastructure.Integration.ExternalDbImporter

Сигнатура: public ImportBatchResult ImportFromExternalDb(string connectionString, DbKind kind)

Логика: Подключается к указанной внешней реляционной БД, читает таблицу контрагентов по настраиваемому соответствию колонок, прогоняет пакет через общий конвейер валидации/слияния импорта.

Данные: Внешняя БД (строка подключения вводится пользователем, пароль — в DPAPI).

Ошибки: Недоступность сервера → «Не удалось подключиться к внешней базе данных» с детализацией исключения подключения; пароль не пишется в лог.

UI: Диалог «Импорт из внешней БД»: строка подключения, кнопка «Проверить соединение», выбор таблицы, запуск мастера соответствия полей.

Зависимости: Функции 71, 73, 74.

Проверка: Контрагенты из подключенной внешней БД появляются в адресной книге с источником «Внешняя БД».

Функция 77. Собственный формат данных для печати EnvelopesData
Модуль/Класс: Domain.Exchange.EnvelopesDataSerializer (src/Domain/Exchange/EnvelopesDataSerializer.cs)

Сигнатура: public XDocument WriteEnvelopes(IEnumerable<PrintableEnvelope> list); public List<PrintableEnvelope> ReadEnvelopes(Stream xml)

Логика: Сериализует и читает XML-контейнер <EnvelopesData> → <EnvelopeData> → <From>/<To> с элементами Name, Address, ZipCode, Phone и атрибутами Number, Note. Запись выполняется потоково в три фазы (открыть элемент → записать текст → закрыть элемент) без построения полного DOM, что позволяет обрабатывать десятки тысяч конвертов без роста памяти. Чтение принимает недоверенный XML: санитайзер CleanXmlString экранирует управляющие символы, ExtractZip извлекает почтовый индекс из строки адреса при установленном флаге, вариант чтения ReadListFromXmlAndFixForPrint приводит данные к печатному виду (усечение строк до 255 символов, выравнивание 6 адресных строк).

Данные: Формат обмена см. EVIDENCE/automation_data_formats.txt; содержит тела писем, но не хранится в БД.

Ошибки: Некорректный XML → возвращаемый код ошибки уровня 2 («ошибка чтения XML-данных»), позиция сбоя — в журнал ошибок; NULL-элементы заменяются пустыми строками, исключение не выбрасывается наружу.

UI: Нет (программный контракт; используется COM-сервером и командной строкой).

Зависимости: Функции 4, 5.

Проверка: Круговое преобразование «печать → XML → чтение → печать» воспроизводит идентичный вывод; malformed-файл возвращает код 2 без падения процесса.

8.6. Печать
Функция 78. Выбор и настройка принтера
Модуль/Класс: Printing.Devices.PrinterManager (src/Printing/Devices/PrinterManager.cs)

Сигнатура: public IReadOnlyList<PrinterDescriptor> EnumeratePrinters(); public PrinterState QueryState(string printerName)

Логика: Перечисляет принтеры через EnumPrintersW с отрисовкой состояния (готов / ошибка / офлайн) в выпадающем списке диалога печати. При отсутствии принтера, назначенного по умолчанию, выводит понятное сообщение «Принтер по умолчанию не установлен» и блокирует печать. Кнопка «Настройки принтера» открывает системный диалог DocumentPropertiesW с последующим чтением измененного DEVMODE.

Данные: Список принтеров Win32 Spooler; выбранный принтер — в parameters/профиле пользователя.

Ошибки: Принтер недоступен или отключен → «Принтер недоступен или отключен», блокировка печати; системный спулер не запущен → сообщение с предложением запустить службу диспетчера печати.

UI: Выпадающий список принтеров с иконками состояния в FormEnvelopePrint.

Зависимости: Отсутствуют.

Проверка: Отключение сетевого принтера меняет его иконку в списке, попытка печати к нему блокируется с сообщением.

Функция 79. Три режима печати: F5 / F6 / F7
Модуль/Класс: Printing.PrintSessionController

Сигнатура: public PrintResult PrintSelected(long mailId); public PrintResult PrintSelectionPreviewed(IReadOnlyList<long> mailIds); public PrintResult PrintSelectionDirect(IReadOnlyList<long> mailIds)

Логика: F5 — печать одного выбранного конверта с обязательным показом диалога выбора шаблона и выводом предпросмотра; F6 — печать всех выделенных записей с выбором шаблона и интерактивным предпросмотром; F7 — печать всех выделенных записей напрямую в спулер без предпросмотра. Опция «Показывать список шаблонов перед печатью» управляет появлением диалога выбора шаблона для F6/F7.

Данные: Выборка mails по идентификаторам; фиксация результата в print_jobs (print_mode = selected / batch_preview / batch_direct).

Ошибки: Пустое выделение → «Не выбрано ни одной записи»; ошибка спулера → статус FAILED с текстом в журнале.

UI: Кнопка-дропдаун «Печать» с четырьмя пунктами (F5/F6/F7/F8) и галкой «Показывать список шаблонов перед печатью».

Зависимости: Функции 20, 78, 80, 82.

Проверка: F7 на выделении из 100 записей отправляет задание без появления окон предпросмотра; каждая из трех операций отражается в журнале с корректным print_mode.

Функция 80. Предпросмотр печати
Модуль/Класс: Printing.Preview.PrintPreviewEngine

Сигнатура: public PreviewDocument RenderPreview(PrintJobContext ctx)

Логика: Формирует векторный предпросмотр тем же конвейером рендеринга, что и физическая печать (единый генератор метафайла), с точным показом: всех полей и их координат, калибровочных смещений X/Y, границ печатной области принтера (опция «Показывать границы печати»), рамок зон индекса и разлиновки. Масштабирование предпросмотра не влияет на координаты печати. Допускается постраничная навигация и зум.

Данные: PrintJobContext (бланк, данные mails, профиль принтера со смещениями).

Ошибки: Расхождение метрик > 1.0 мм → поверх предпросмотра выводится баннер-предупреждение; блокирующее несоответствие — предпросмотр недоступен до устранения.

UI: Окно предпросмотра: холст, линейка, зум, панель «Прервать», кнопка «Печать».

Зависимости: Функции 38, 83.

Проверка: Распечатанный конверт совпадает с предпросмотром с точностью ±1 мм на трех принтерах разных типов.

Функция 81. Открытие шаблона в дизайнере (F8)
Модуль/Класс: UI.Forms.FormTemplateDesigner

Сигнатура: public void OpenTemplateDesigner(string templateName)

Логика: Открывает список печатных шаблонов с возможностью редактирования; двойной щелчок загружает выбранный шаблон во встроенный редактор бланков (см. §7.5). Если шаблон встроенный (is_builtin) — правки сохраняются только в пользовательскую копию.

Данные: Каталог шаблонов + таблица print_templates.

Ошибки: Шаблон-файл отсутствует в хранилище → «Файл шаблона не найден» (код уровня 4), предложение переустановить библиотеку бланков.

UI: Список шаблонов с миниатюрами, кнопки «Создать копию», «Редактировать», «Удалить».

Зависимости: Функции 37, 38.

Проверка: Нажатие F8 открывает каталог; выбор шаблона и правка поля сохраняются в .lbk без перезапуска приложения.

Функция 82. Опция «Показывать список шаблонов перед печатью»
Модуль/Класс: Application.Services.PrintOptions

Сигнатура: public bool GetShowTemplateList(); public void SetShowTemplateList(bool value)

Логика: Управляет появлением диалога выбора шаблона перед операциями печати. Значение сохраняется в профиле пользователя (parameters) и применяется ко всем режимам F5/F6/F7.

Данные: parameters (ключ ShowTemplateListBeforePrint).

Ошибки: Нет.

UI: Галочка в кнопке-дропдауне «Печать» и на странице «Печать» настроек.

Зависимости: Функция 79.

Проверка: Выключение галки переводит F6 в печать последним использованным шаблоном без диалога выбора.

Функция 83. Проверка размера бумаги: шаблон ↔ принтер
Модуль/Класс: Printing.Validation.PaperSizeValidator (src/Printing/Validation/PaperSizeValidator.cs)

Сигнатура: public PaperCheckResult Validate(BlankMetrics blank, PrinterDeviceInfo printer)

Логика: Двойная независимая сверка: (1) размер шаблона (blank.xml) против физической бумаги принтера из DeviceCapabilitiesW(DC_PAPERS, DC_PAPERSIZE); (2) метрики документа metrics.PaperSize против DEVMODE.dmPaperSize/dmPaperWidth/dmPaperLength. Вычисляется дельта ΔW = W_blank − W_printable, ΔH = H_blank − H_printable. Если Δ > +1.0 мм — блокирующая ошибка BLOCKED_SIZE; если драйвер сообщает иной размер при вмещении — предупреждение с предложением режима «Зафиксировать A4».

Данные: blank.xml, DEVMODE принтера, профиль printer_profiles.

Ошибки: «Конверт слишком большой для выбранного принтера» — блокировка задания; запись в print_jobs со статусом BLOCKED_SIZE (журналируется и отклоненная попытка).

UI: Диалог с кнопками «Отмена», «Зафиксировать A4», «Выбрать другой принтер».

Зависимости: Функции 78, 84.

Проверка: Попытка печати C4 (324×229) на принтере с максимальным лотком DL блокируется сообщением; DL на лотке A4 проходит с предупреждением.

Функция 84. Режим «Зафиксировать размер A4»
Модуль/Класс: Printing.Engine.FixA4Mode

Сигнатура: public PrintJobContext ApplyFixA4(PrintJobContext ctx)

Логика: Принудительно устанавливает DEVMODE.dmPaperSize = DMPAPER_A4; координаты всех элементов масштабируются с привязкой к верхнему левому углу листа A4 без изменения взаимного расположения. Если бланк больше A4 — честное предупреждение «всё равно не поместится» с указанием превышения в мм; печать возможна, но обрезаемая зона подсвечивается в предпросмотре.

Данные: blank metrics; printer_profiles.fix_a4.

Ошибки: Превышение габаритов A4 — предупреждение перед печатью (не молча).

UI: Флаг «Зафиксировать размер A4» в диалоге печати, окрашивается предупреждением при активации.

Зависимости: Функция 83.

Проверка: Конверт C5 на листе A4 печатается в верхнем левом углу; конверт B4 при включенном режиме выводит предупреждение о непомещении до отправки задания.

Функция 85. Предупреждение о слишком большом конверте
Модуль/Класс: Printing.Validation.OversizeWarning

Сигнатура: public void ShowOversizeDialog(SizeF blank, SizeF paper)

Логика: Централизованная точка вывода блокирующего предупреждения «Конверт слишком большой для выбранного принтера» с числовым указанием обеих величин (мм) и списка доступных форматов выбранного принтера.

Данные: Результаты PaperSizeValidator.

Ошибки: Нет (сам является реакцией на ошибку).

UI: Модальное окно с данными о несоответствии.

Зависимости: Функция 83.

Проверка: Сообщение содержит точные размеры шаблона и бумаги; после смены принтера на совместимый печать проходит.

Функция 86. Двусторонняя печать уведомлений (дуплекс)
Модуль/Класс: Printing.Engine.DuplexController

Сигнатура: public void ConfigureDuplex(PrintJobContext ctx, DuplexMode mode)

Логика: Лицевая и оборотная стороны бланка уведомления (например, ф.119) — две страницы одного шаблона; дуплекс настраивается через DEVMODE.dmDuplex при аппаратной поддержке. Если принтер не поддерживает дуплекс, автоматически предлагается программная сепарация: печать всех лицевых, пауза с инструкцией перевернуть стопку, печать всех оборотных.

Данные: blank.xml (page 1/2), возможности драйвера (DC_DUPLEX).

Ошибки: Драйвер не поддерживает дуплекс → автоматический фолбэк на двухпроходную печать с диалогом-инструкцией.

UI: Группа «Двусторонняя печать»: ( ) Односторонняя / (•) Двусторонняя (если поддерживается) / ( ) Двухпроходная вручную.

Зависимости: Функции 36, 87.

Проверка: Двухстраничное уведомление печатается на одном листе с корректным совмещением сторон на дуплексном принтере; на недуплексном — последовательность лицевые → инструкция → оборотные.

Функция 87. Печать только чётных / только нечётных страниц
Модуль/Класс: Printing.Engine.PageParityFilter

Сигнатура: public IEnumerable<int> FilterPageNumbers(int totalPages, PageParity parity)

Логика: Перед формированием задания отфильтровывает номера страниц по чётности: All / OddOnly / EvenOnly. Применяется как к многостраничным бланкам (уведомления, ф.116), так и к выборке писем при двухпроходной печати.

Данные: Параметры печатной сессии.

Ошибки: Нет.

UI: Переключатель «Печать: ( ) всех страниц / ( ) только нечётных / ( ) только чётных» в диалоге печати.

Зависимости: Функции 79, 86.

Проверка: Для двухстраничного шаблона режим «только нечётные» выводит одну страницу (лицевую) на каждое отправление.

Функция 88. Журналирование параметров печати
Модуль/Класс: Printing.Audit.PrintAuditWriter (src/Printing/Audit/PrintAuditWriter.cs)

Сигнатура: public void WriteSessionRecord(PrintJobRecord record)

Логика: Каждая сессия (успешная и неуспешная) пишет запись в print_jobs и текстовый канал аудита print_audit.log строго формата §6.5: имя принтера, параметры бумаги (размер, лоток, дуплекс), шаблон и форма, имя и размер шрифта, смещения, число объектов/страниц/копий, статус и длительность. Ни одна печать не может быть выполнена в обход записи аудита (архитектурный инвариант слоя печати).

Данные: print_jobs, Serilog-канал «print».

Ошибки: Сбой записи аудита отменяет печатную транзакцию (печать без журнала запрещена).

UI: Просмотр — окно «История печати» с фильтром по датам/принтеру/статусу.

Зависимости: Функции 78, 111.

Проверка: После принудительного прерывания задания в журнале остается запись со статусом ABORTED и полным набором параметров.

Функция 89. Печать на разлинованные (типографские) конверты
Модуль/Класс: Printing.Engine.PreprintedMode

Сигнатура: public PrintJobContext StripBackground(PrintJobContext ctx)

Логика: Режим «Тип носителя: Размеченный типографский» исключает печать фоновых изображений, рамок и сетки — выводятся только адресные данные и индекс. Перед печатью выводится предупреждение о соответствии конверта требованиям ГОСТ Р 51506-99 и о риске попадания данных вне полей разлиновки при несовпадении партии; полный текст стандарта доступен из справки.

Данные: blank.xml (флаг фоновых слоёв), режим носителя в диалоге.

Ошибки: Предупреждение (не блокирующее) с чекбоксом «Больше не показывать это сообщение».

UI: Переключатель «Тип носителя: ( ) Чистый конверт / (•) Размеченный типографский».

Зависимости: Функции 36, 80.

Проверка: В режиме «размеченный» предпросмотр и печать не содержат фоновой графики; предупреждение показывается однократно до сброса галки.

Функция 90. Машиночитаемая печать почтового индекса
Модуль/Класс: Printing.Elements.PostIndexRenderer + Infrastructure.Fonts.ZipcodeFontGuard

Сигнатура: public bool EnsureZipcodeFontInstalled(); public void RenderMachineReadableIndex(IGraphicsContext g, string zip, PostIndexSpec spec)

Логика: Индекс выводится кодовым штампом по §6.3 (ширина знака 3.7 мм, высота 5.5 мм, шаг 5.4 мм, толщина линии 0.45 мм по умолчанию, контроль диапазона 0.2–0.5 мм, 6 знаков; для международных «Мн» — 3 знака с индексом «555»). Шрифт ZIPcode.ttf поставляется с программой и регистрируется через AddFontResourceW в HKCU. Допускается пропорциональное уменьшение кодового штампа, если индекс не помещается в поле (санкция стандарта).

Данные: Поле индекса из бланка; словарь форматов (территория Вн/Мн).

Ошибки: Отсутствие шрифта ZIPcode в системе — блокирующее предупреждение с требованием переустановить компонент (запрет скрытой подмены на Arial/Times); толщина линии вне нормы — предупреждение до печати.

UI: Флаг «Печать индекса» и поле редактирования индекса с живой валидацией в диалоге печати; тулбар параметров индекса в дизайнере.

Зависимости: Функции 41, 83.

Проверка: Напечатанный шестизначный штамп считывается почтовым сканером; при удаленном шрифте печать конверта блокируется с явным предупреждением.

Функция 91. Автоподбор кегля в поле бланка
Модуль/Класс: Printing.Engine.TextFitter (см. Функцию 34; редакторская обвязка)

Сигнатура: public FittingResult FitField(BlankField field, string text)

Логика: В контексте печати: итеративное уменьшение кегля от font_height_max_pt до font_height_min_pt шагом 0.5pt с переносом по словам; при невозможности вместить — маркер переполнения в предпросмотре и запись в предупреждения сессии. В редакторе — интерактивный индикатор «поле переполнено» у инспектора свойств.

Данные: Свойства blank_fields (min/max кегль, max_chars).

Ошибки: Переполнение при минимальном кегле — предупреждение, печать не блокируется (текст усекается по границе поля с фиксацией в журнале).

UI: Индикатор в предпросмотре; свойства поля в редакторе.

Зависимости: Функции 34, 38.

Проверка: Наименование из 120 символов в поле «Кому 1» автоматически уменьшается с 10pt до 7.5pt и вписывается в границы; строка свыше лимита дает маркер переполнения.

8.7. Электронная почта
Функция 92. Электронная рассылка (отдельно от бумажной)
Модуль/Класс: Application.Services.EmailDistributionService (src/Application/Services/EmailDistributionService.cs)

Сигнатура: public SendReport SendBatch(EmailDistribution distribution)

Логика: Формирует партию писем по выборке контрагентов (категория / фильтр / выделение), подставляет тему и тело (текст или HTML) с маркерами полей, последовательно отправляет через SMTP-клиент с прогрессом и возможностью отмены. Электронная рассылка не создает записей mails (это отдельный канал от бумажного), но пишет журнал отправки.

Данные: counterparties.email; журнал email_log.

Ошибки: Отсутствие адреса у части получателей — они исключаются с перечислением в отчете.

UI: Мастер «Электронная рассылка»: выбор получателей, редактор письма, отправка с прогрессом.

Зависимости: Функции 1, 93, 100.

Проверка: Рассылка на категорию из 30 адресов с 2 некорректными отправляет 28 писем и выводит отчет с 2 пропусками.

Функция 93. Настройка SMTP-учетной записи
Модуль/Класс: Infrastructure.Mail.SmtpOptions

Сигнатура: public ValidationResult ValidateSmtpSettings(SmtpSettings settings)

Логика: Хранит сервер, порт, логин, пароль; пароль защищается DPAPI (требование — не хранить открытым текстом). Перед сохранением выполняется пробное соединение. Если сервер не указан при попытке отправки — понятная ошибка «SMTP-сервер не указан».

Данные: parameters/настройки приложения; пароль — ProtectedData.

Ошибки: «SMTP-сервер не указан»; «Не удалось соединиться с SMTP-сервером» с детализацией (таймаут/отказ).

UI: Страница «Почта» настроек: сервер, порт, логин, пароль (с глазом), кнопка «Проверить соединение».

Зависимости: Отсутствуют.

Проверка: Очистка поля сервера и нажатие «Отправить» выводит «SMTP-сервер не указан» до начала отправки.

Функция 94. SSL/TLS-защищенное соединение
Модуль/Класс: Infrastructure.Mail.SmtpClientFacade

Сигнатура: public void EnableSsl(SslMode mode) // None | StartTls | ImplicitTls

Логика: Устанавливает защищенный канал SMTPS/STARTTLS; проверка сертификата — с возможностью пользователя принять корпоративный самоподписанный сертификат (решение сохраняется в профиле).

Данные: Настройки почты.

Ошибки: Ошибка сертификата → диалог с отпечатком сертификата и выбором «Доверять / Отклонить».

UI: Выпадающий список «Защита соединения: нет / STARTTLS / SSL».

Зависимости: Функция 93.

Проверка: Отправка через порт 465 с ImplicitTls проходит успешно; отклоненный сертификат прерывает отправку с сообщением.

Функция 95. Поддержка прокси-сервера
Модуль/Класс: Infrastructure.Mail.ProxySettings

Сигнатура: public WebProxy BuildProxy(ProxySettings settings)

Логика: Опциональный выход через HTTP-прокси: флаг «Использовать прокси», сервер, порт, опциональная авторизация (логин/пароль; пароль — в DPAPI).

Данные: Настройки приложения.

Ошибки: Недоступность прокси — сообщение с предложением проверить параметры или отключить прокси.

UI: Группа «Прокси-сервер» на странице «Почта».

Зависимости: Функция 93.

Проверка: При включенном корпоративном прокси отправка письма проходит через него (подтверждается журналом соединения).

Функция 96. Формат письма: простой текст / HTML
Модуль/Класс: Domain.Entities.EmailDistribution

Сигнатура: public void SetBodyFormat(BodyFormat format, string body)

Логика: Переключение между plain-text и HTML-телом; для HTML выполняется базовая санитизация разметки (удаление скриптов) перед отправкой и предпросмотром.

Данные: Тело письма в памяти/шаблоне.

Ошибки: Некорректный HTML — предупреждение с подсветкой места ошибки.

UI: Переключатель «Формат: ( ) Простой текст / (•) HTML» в редакторе письма.

Зависимости: Функция 92.

Проверка: Одно и то же сообщение в текстовом режиме приходит без тегов, в HTML — с форматированием.

Функция 97. Встроенный WYSIWYG-редактор HTML
Модуль/Класс: UI.Editors.HtmlMailEditor (мост к встраиваемому движку редактирования на базе HTML-компонента)

Сигнатура: public string EditHtml(string html)

Логика: Полноценный визуальный редактор: форматирование, списки, таблицы, вставка картинок/ссылок, шаблоны писем. Шаблон письма по умолчанию и стили (включая стиль «word» — эмуляцию оформления Word) хранятся файлами рядом с программой и редактируются. Используемая библиотека редактора поставляется по LGPL — файл лицензии обязан присутствовать в поставке.

Данные: Файлы emailedit/*.html, стили content.css / word.css.

Ошибки: Нет.

UI: Окно редактора с панелью инструментов и вкладками «Визуальный / HTML-код».

Зависимости: Функция 96.

Проверка: Созданное в редакторе письмо с таблицей и логотипом доставляется с сохранением форматирования.

Функция 98. Вложения письма
Модуль/Класс: Application.Services.AttachmentService

Сигнатура: public void AddAttachment(EmailDistribution dist, string filePath)

Логика: Прикрепляет произвольные файлы к письму; вложения копируются в папку хранилища отправителя перед отправкой, чтобы удаление исходного файла не ломало повторную отправку.

Данные: Файловое хранилище вложений.

Ошибки: Файл более 25 МБ — предупреждение о возможном отказе почтового сервера с подтверждением отправки.

UI: Список вложений с кнопками «Добавить» / «Удалить».

Зависимости: Функция 92.

Проверка: Повторная отправка сохраненного письма с вложением выполняется после удаления исходного файла-источника.

Функция 99. Точное указание ReplyTo и адреса отправителя
Модуль/Класс: Infrastructure.Mail.SmtpClientFacade

Сигнатура: public void SetEnvelopeFrom(string from, string replyTo)

Логика: Раздельные адреса From (конверт отправки) и Reply-To (адрес ответа); типовой сценарий — рассылка от служебного ящика с ответами на ящик исполнителя.

Данные: Поля письма.

Ошибки: Некорректный формат адреса — валидация до отправки.

UI: Поля «Адрес отправителя» и «Обратный адрес (Reply-To)» в мастере рассылки.

Зависимости: Функция 93.

Проверка: Ответ на полученное письмо уходит на адрес Reply-To, отличный From.

Функция 100. Отправка через встроенный SMTP-клиент
Модуль/Класс: Infrastructure.Mail.SmtpClientFacade (src/Infrastructure/Mail/SmtpClientFacade.cs)

Сигнатура: public SendReport Send(MailMessage message, CancellationToken ct)

Логика: Собственный SMTP-клиент поверх сокетов с поддержкой SSL/TLS и прокси (без внешнего почтового приложения); очередная отправка с паузой между письмами (защита от блокировки сервером), прогресс, отмена. Результат каждой отправки пишется в журнал email_log (адрес, статус, код ответа сервера, дата).

Данные: Журнал email_log.

Ошибки: Отказ сервера (4xx/5xx) — письмо помечается ошибкой, партия продолжает отправку; таймаут — одна повторная попытка.

UI: Прогресс-окно отправки с кнопкой «Прервать».

Зависимости: Функции 93, 94, 95.

Проверка: Отправка 50 писем проходит без открытия внешних приложений; каждое письмо отражено в журнале с кодом SMTP-ответа.

8.8. Администрирование и сервис
Функция 101. Клиент-серверный режим
Модуль/Класс: Infrastructure.Persistence.ServerRegistry (src/Infrastructure/Persistence/ServerRegistry.cs)

Сигнатура: public IReadOnlyList<ServerEntry> ListServers(); public ConnectionCheckResult CheckServer(ServerEntry entry)

Логика: Поддержка общей БД на сервере для нескольких рабочих мест: список серверов хранится в отдельном справочнике (аналог listIp.ini — файл servers.ini), подключение по TCP. На сервере размещаются БД и папка шаблонов; на клиенте — копия программы и копия папки шаблонов. Сервер без клиентов и клиент без сервера БД — поддерживаемые конфигурации (второй — локальный режим SQLite).

Данные: servers.ini (имя/IP, порт, описание); строка подключения — в защищенных настройках.

Ошибки: Сервер недоступен → «Не удалось подключиться к серверу БД <имя>:<порт>» с проверкой сети/брандмауэра (порт по умолчанию 5432).

UI: Диалог «Смены подключения»: выбор «Этот компьютер / Сервер из списка / Новый сервер».

Зависимости: Функция 102.

Проверка: Два рабочих места одновременно редактируют разные записи общей БД без конфликтов блокировок.

Функция 102. Выбор и смена подключения к БД
Модуль/Класс: Infrastructure.Persistence.ConnectionManager

Сигнатура: public void SwitchConnection(ConnectionDescriptor descriptor)

Логика: Хранит реквизиты подключения (тип СУБД, сервер, порт, база, пользователь); пароль — в DPAPI. Смена подключения применяется после перезапуска приложения; скрытый вход в диалог — Ctrl + двойной щелчок по ярлыку программы. Кириллица в имени БД и в пути к БД запрещена (валидация с понятным сообщением).

Данные: Файл настроек подключения + DPAPI-хранилище секрета.

Ошибки: Неверные реквизиты — сообщение с деталями отказа СУБД; кириллица в имени БД — блокировка сохранения.

UI: Диалог «Подключение к базе данных».

Зависимости: Функция 101.

Проверка: Смена «локальная → серверная» переносит приложение на общую БД после перезапуска; пароль в файле настроек не читается текстовым редактором.

Функция 103. Автоматическое архивирование по расписанию
Модуль/Класс: Infrastructure.Backup.ScheduledBackupService (src/Infrastructure/Backup/ScheduledBackupService.cs)

Сигнатура: public void RunScheduledBackup(); public BackupResult BackupNow(BackupOptions options)

Логика: Полный архив — вся папка БД (или файл SQLite) упаковывается штатным ZIP (совместимо со «Сжатыми папками» Windows) с опциональным шифрованием паролем; имя файла Base_ГГММДДЧЧММ.zip. Расписание — до трех точек в сутки; автоудаление копий старше N дней; целевая папка — любая папка Windows, включая сетевые и подключённые облачные диски. Поддерживаются два режима хранения (см. Функцию 161).

Данные: Файловая система; настройки в parameters.

Ошибки: Недоступность целевой папки — повтор через 10 минут, затем предупреждение в журнал ошибок; архив не затирает предыдущий при неудаче.

UI: Диалог архивирования (см. §7.8).

Зависимости: Функция 111.

Проверка: В назначенные 09:00/13:05/16:05 создаются архивы; копии старше срока хранения удаляются автоматически.

Функция 104. Восстановление БД из архива
Модуль/Класс: Infrastructure.Backup.RestoreService

Сигнатура: public RestoreResult RestoreFromArchive(string archivePath, string password)

Логика: Проверяет целостность ZIP и пароль, распаковывает во временную папку, сверяет версию схемы с schema_version, выполняет восстановление атомарно (rename-старой → установка новой) и предлагает перезапуск. Перед восстановлением автоматически создается страховочная копия текущего состояния.

Данные: Архивный файл, каталог БД.

Ошибки: Неверный пароль / поврежденный архив — сообщение без изменения текущей БД; версия схемы новее программы — отказ с указанием требуемой версии.

UI: Кнопка «Восстановить из архива» в диалоге архивирования; мастер подтверждения.

Зависимости: Функция 103.

Проверка: Архив, созданный на другой машине, разворачивается на чистой установке и открывается без ручных правок.

Функция 105. Установка программы и БД на USB-флешку
Модуль/Класс: Infrastructure.Deployment.UsbInstaller

Сигнатура: public UsbInstallResult InstallToUsb(string usbRoot, UsbInstallMode mode)

Логика: Два варианта: (1) «Всё целиком» — программа + переносная СУБД + БД на флешке; (2) «Только база данных» (рекомендуемый) — на флешке размещается файл/папка БД, программа остается на рабочих станциях и подключает флешку как источник данных.

Данные: Файловая система.

Ошибки: Недостаточно места — расчет требуемого объема до копирования; извлечение флешки в процессе — откат partial-копирования.

UI: Мастер «Установка на USB-флешку» с выбором режима.

Зависимости: Функция 102.

Проверка: Переносная конфигурация запускается с флешки на другой машине без установки.

Функция 106. Интеграция с оболочкой Windows
Модуль/Класс: Infrastructure.Shell.ExplorerIntegration

Сигнатура: public void RegisterShellMenu(); public void UnregisterShellMenu()

Логика: Добавляет/удаляет пункт вызова программы в контекстном меню Проводника (регистрация в HKCU\Software\Classes\*\shell и Directory\shell). Команда открывает приложение с параметром импорта выбранного файла.

Данные: Реестр Windows (только HKCU, без прав администратора).

Ошибки: Отказ доступа — сообщение о необходимости запуска от имени пользователя с правом записи в HKCU.

UI: Флажок «Проводник Windows: пункт в контекстном меню» в настройках.

Зависимости: Функция 71.

Проверка: После включения пункт появляется в контекстном меню файла CSV и запускает мастер импорта с этим файлом.

Функция 107. Загрузка данных из предыдущей/бесплатной версии
Модуль/Класс: Infrastructure.Migration.LegacyUpgradeImporter

Сигнатура: public MigrationReport ImportFromLegacy(string sourceFolder)

Логика: Мастер указания папки БД предыдущей установки; чтение данных (контрагенты, группы, письма, настройки) и перенос в текущую схему через тот же конвейер слияния импорта. Переносимы: адресная книга, группы, пользовательские поля, история печати (как снимки), настройки подключения не переносятся.

Данные: Папка БД предыдущей версии.

Ошибки: Не найдена БД в указанной папке — сообщение со списком ожидаемых файлов; несовместимая версия — отказ с указанием поддерживаемых.

UI: Меню «Файл → Загрузить данные из старой версии», мастер.

Зависимости: Функции 71, 74, 117, 118.

Проверка: Адресная книга и группы из прежней установки переносятся без потерь; повторный запуск пропускает уже перенесенные записи.

Функция 108. Поиск адреса по ИНН/УНН в интернете
Модуль/Класс: Infrastructure.Integration.WebLookupLauncher (см. Функцию 13; конфигурационная обвязка)

Сигнатура: public void OpenWebLookup(string inn)

Логика: Открывает системный браузер по настраиваемому шаблону URL с подстановкой ИНН/УНН (например, https://<агрегатор>/search?q={INN}). Шаблон командной строки поиска задается в настройках; для Республики Беларусь по умолчанию используется прямой запрос ЕГР.

Данные: parameters (ключ WebLookupUrlTemplate).

Ошибки: Не задан шаблон — предложение настроить на странице «Сервис».

UI: Меню «Сервис → Найти это в интернете» и кнопка лупы у поля ИНН.

Зависимости: Функции 1, 13.

Проверка: Вызов команды открывает вкладку браузера с результатами по текущему ИНН.

Функция 109. Техническая поддержка: утилита удалённого доступа
Модуль/Класс: Infrastructure.Support.RemoteAssistantLauncher

Сигнатура: public void LaunchOrUpdateRemoteAssistant()

Логика: Запускает настроенную утилиту удалённого доступа; при отсутствии — скачивает указанную в настройках сборку и запускает. Параметры (URL/имя утилиты) задаются администратором.

Данные: parameters (ключ RemoteAssistantUrl).

Ошибки: Недоступность сети/URL — сообщение и предложение повторить.

UI: Меню «Сервис → Техническая поддержка».

Зависимости: Отсутствуют.

Проверка: Вызов пункта меню запускает утилиту удалённого доступа без дополнительных настроек пользователем.

Функция 110. Трекер отправлений на сайте Почты России
Модуль/Класс: Infrastructure.Integration.RussianPostTracker (src/Infrastructure/Integration/RussianPostTracker.cs)

Сигнатура: public TrackBatch RegisterBatch(RegisterBatchRequest request); public TrackStatus[] QueryStatuses(string batchNumber)

Логика: Регистрирует партию отправлений: идентификаторы (ШПИ, 14 знаков), номер партии, дата отправки, пункт отправки; учетные данные (логин/пароль или токен) — пароль/токен в DPAPI. Опрашивает статусы доставки по номеру партии/идентификатору и проставляет подтверждения доставки в mails (переход «Отправлено → Подтверждено» по факту вручения).

Данные: mails.russian_post_id, russian_post_batch; post_registers.

Ошибки: Отказ сервиса — повтор по расписанию; исчерпание попыток — запись в журнал ошибок, вручение подтверждается вручную.

UI: Диалог «Трекер Почты России»: учетные данные, список партий, кнопки «Зарегистрировать партию» / «Обновить статусы».

Зависимости: Функции 24, 25.

Проверка: Зарегистрированная партия из 10 отправлений через запрос статусов переводит все 10 писем в «Подтверждено» при получении вручений.

Функция 111. Журнал ошибок приложения + отдельный журнал печати
Модуль/Класс: Infrastructure.Logging.LogHub (Serilog)

Сигнатура: public ILogger GetChannel(LogChannel channel) // App | Print | Audit | Email

Логика: Раздельные каналы логирования с ротацией по размеру/дню: «ошибки приложения» (исключения с трассировкой, уровень Warning+), «печать» (все печатные сессии, формат §6.5), «аудит» (изменения справочников, входы). Просмотр журнала ошибок — отдельное окно с фильтром по уровню/дате и кнопкой «Скопировать для службы поддержки».

Данные: logs/app-.log, logs/print-.log, logs/audit-.log.

Ошибки: Сбой файловой системы логов — деградация в память с выводом в консоль отладки, приложение не падает.

UI: Меню «Файл → Журнал ошибок»; окно «История печати».

Зависимости: Отсутствуют.

Проверка: Искусственно вызванная ошибка появляется в журнале ошибок и не появляется в журнале печати; сессия печати — только в журнале печати.

Функция 112. Языковые файлы и переключение языка интерфейса
Модуль/Класс: Infrastructure.Localization.LanguageManager

Сигнатура: public void LoadLanguage(string code); public void SwitchLanguage(string code)

Логика: Локализация — расширяемый набор файлов словарей (интерфейсные строки + словари числительных/валют по §10.6), а не захардкоженный набор. Переключение языка применяется без перезапуска программы (повторная отрисовка открытых форм). Новый язык добавляется копированием шаблона словаря.

Данные: Файлы lang/<код>.xml и lng/<валюта>.lng.

Ошибки: Отсутствующий ключ — отображается ключ-имя и запись в журнал; отсутствующий файл языка — откат на русский с предупреждением.

UI: Страница «Язык» настроек; выпадающий список языков.

Зависимости: Функции 150, 151.

Проверка: Переключение RU→EN на открытом главном окно перерисовывает подписи без перезапуска; отсутствующий ключ отображается своим именем.

8.9. Интеграция и автоматизация
Функция 113. COM-сервер автоматизации
Модуль/Класс: ComAutomation.PEProApplication (обертка IPEProApplication; полный контракт — §9.1) (src/ComAutomation/PEProApplication.cs)

Сигнатура: public int PrintEnvelope(string from, string fromAddress, string fromZipCode, string to, string toAddress, string toZipCode); // и еще 22 опубликованных метода по EVIDENCE/automation_idl.txt

Логика: Out-of-proc COM-сервер с ProgID PEPro.Application. Четыре семейства документов (конверты, уведомления, описи, реестры) × два источника (XML-строка, файл); печать одного конверта из шести строковых параметров; печать с одновременным созданием учетной записи отправления; печать реестра без показа формы; импорт контрагентов; создание писем и входящих из файла/XML; управление интерфейсом; служебные методы БД, версий, флагов, диалогов подтверждения и активации окна. Публичный слой только готовит команду (паттерн Prepare*), исполнение — во внутреннем слое; ошибки транслируются в коды GetWarningType, процесс сервера не падает.

Данные: XML-контракты EnvelopesData / RegistersData / InventoriesData / EconomicMembers; настройки печати — в реестре HKCU\Software\<ключ>\PrintEnvelope (FromName, FromAddr, FromIndex, ToName, ToAddr, ToIndex).

Ошибки: Коды GetWarningType: 0 — успех; 1 — БД отсутствует/некорректна; 2 — ошибка чтения XML; 3 — ошибка подготовки документа реестра; 4 — отсутствует файл шаблона. Отсутствующий параметр реестра = пустая строка, а не исключение.

UI: Нет (автоматизация); поведение окон управляется флагом ShowFormsAsModal.

Зависимости: Функции 20, 36, 50, 53, 62, 77, 79.

Проверка: Скрипт печати конверта (JScript, CreateObject("PEPro.Application").PrintEnvelope(...)) печатает конверт без единого клика в UI и возвращает код 0.

Функция 114. Тип-библиотека и регистрация COM в HKCU
Модуль/Класс: ComAutomation.ComRegistration

Сигнатура: public void RegisterTypeLibrary(); public void UnregisterTypeLibrary()

Логика: Регистрация ProgID PEPro.Application и интерфейса IPEProApplication в HKCU\Software\Classes (без прав администратора); Includes-версия типилиба 2.2; DISPID назначаются последовательно без пропусков. Совместимость контракта — по версии API (целое, сквозная нумерация), см. §9.3.

Данные: Реестр Windows (HKCU).

Ошибки: Отказ записи — сообщение о запуске под учетной записью с правом записи в HKCU.

UI: Кнопки «Зарегистрировать COM-сервер» / «Отменить регистрацию» на странице интеграции настроек.

Зависимости: Функция 113.

Проверка: После регистрации `oleview` показывает типилибу и 23 метода интерфейса; вызов из 32- и 64-битных клиентов проходит через DCOM-маршалинг.

Функция 115. Примеры вызова из скрипта и из Excel
Модуль/Класс: Docs и поставляемые файлы примеров (Scripts/printenvelope.js, Scripts/printregistry.vbs, Docs/ExcelCallExample.xls)

Сигнатура: Поставляемые артефакты, не код приложения.

Логика: Поставляются рабочие примеры: JScript-скрипт печати конверта с безопасным чтением параметров из реестра (отсутствие настройки — пустая строка, не исключение), VBS-пример печати реестра из XML, книга Excel с макросом вызова PrintFromXmlRegistersWithoutForm. Примеры сопровождаются комментариями по конфигурации ключей реестра.

Данные: Файлы поставки.

Ошибки: Нет.

UI: Нет.

Зависимости: Функции 113, 114.

Проверка: Запуск printenvelope.js на машине с установленной программой печатает конверт с параметрами из реестра; макрос Excel формирует файл реестра по указанному пути.

Функция 116. Импорт и создание писем через API (интеграция с 1С)
Модуль/Класс: ComAutomation.ApiLettersFacade

Сигнатура: public int CreateFromXmlLetters(string xml); public int CreateFromFileLetters(string path); public int ImportContragents(string path); public int ImportContragentsFromXml(string xml)

Логика: Создание исходящих писем и входящих документов из внешних данных (файл/XML) с флагами контекста: обновлять ли существующих контрагентов (FlagUpdateContragents), выкусывать ли индекс из строки адреса (FlagParseZipIndexFromAddress), писать ли примечание при импорте (WriteToNoteOnImport). Письмам назначаются статусы и сквозные номера по правилам Функции 25.

Данные: XML-контракты EconomicMembers и EnvelopesData; таблицы mails, counterparties.

Ошибки: Код 1 при незагруженной БД (требуется SafeLoadDatabase); код 2 при битом XML.

UI: Нет.

Зависимости: Функции 5, 20, 25, 113.

Проверка: Вызов CreateFromXmlLetters с пакетом из 5 писем создает 5 записей mails со статусом «Создано» и последовательными исходящими номерами.

Функция 117. Импорт базы из исходной MySQL-схемы (8 таблиц)
Модуль/Класс: Infrastructure.Migration.LegacyMySqlImporter (src/Infrastructure/Migration/LegacyMySqlImporter.cs)

Сигнатура: public MigrationReport ImportFromMySqlDump(string host, int port, string db, string user, string password)

Логика: Перенос данных из схемы-предшественницы (8 таблиц: отправители addrfrom с логотипами, получатели addrto, история печати addrto_history, кэш QR addrblob, группы groups, описи inventory/inventory_detail, параметры params). Соответствие полей: ind_from/str_from1..6 → адрес отправителя; ind_to/str_to1..6, copyes, vip, prim, date_send, print_prim → mails; teplate_descr, addrfrom-снимок → print_history; fdefault/closed → groups. Кодировка cp1251 конвертируется в UTF-8.

Данные: Внешняя MySQL-БД (схема по EVIDENCE/schema_reference.sql).

Ошибки: Таблица отсутствует — пропуск с предупреждением в отчете; недопустимые значения (индекс не 6 цифр) — запись помечается NeedsVerification, не отбрасывается.

UI: Мастер миграции: параметры подключения, отчет соответствия, сводка.

Зависимости: Функции 74, 101.

Проверка: Выгрузка из прежней системы переносит адресную книгу, группы и историю печати; счетчики записей совпадают с отчетом мастера.

Функция 118. Импорт базы из исходной Access-схемы (14 таблиц)
Модуль/Класс: Infrastructure.Migration.LegacyAccessImporter

Сигнатура: public MigrationReport ImportFromMdb(string mdbPath)

Логика: Перенос из файловой БД-предшественницы (14 таблиц: контрагенты с полным набором реквизитов, письма, рассылки, бланки, пользовательские поля, входящие, движения документов, реестры, настройки). Соответствие: контрагент (53 колонки) → counterparties (+counterparty_fields для неканонических полей); письмо (39 колонок) → mails; бланк (6) → print_templates; настройки (39) — только переносимые ключи (текущий отправитель, флаги печати).

Данные: Файл .mdb через ODBC/OLEDB-провайдер.

Ошибки: Файл занят другим процессом — сообщение закрыть приложение-владельца; отсутствующая таблица — пропуск с предупреждением.

UI: Мастер миграции (общая оболочка с Функцией 117).

Зависимости: Функции 74, 102.

Проверка: Контрагенты с банковскими и паспортными реквизитами переносятся без потерь; пользовательские поля предыдущей системы доступны как [Custom_*] теги.

8.10. Защита, лицензирование, режимы работы
Функция 119. Режим trial с ограничением объёма и сроком
Модуль/Класс: Licensing.LicenseStateMachine (src/Licensing/LicenseStateMachine.cs)

Сигнатура: public LicenseState EvaluateState()

Логика: Состояние лицензии — один автомат, вычисляемый при старте и хранимый в одном месте: Trial (срок, по умолчанию 10 дней, и лимит числа контрагентов/документов) / Licensed / Expired. Проверки UI читают это состояние; при превышении лимита или истечении срока приложение деградирует корректно: чтение доступно, создание записей и печать партий блокируются с понятным сообщением и оставшимися днями.

Данные: Реестр HKCU + подписанный файл лицензии.

Ошибки: Истёкший trial — NAG-окно «Пробный период закончился», «Осталось дней: 0», кнопки «Купить сейчас» / «Ввести ключ».

UI: NAG-экран при старте в trial; индикатор дней в строке состояния.

Зависимости: Отсутствуют.

Проверка: Перевод системной даты за границу срока переводит приложение в режим истечения без потери данных; уменьшение лимита не удаляет существующие записи.

Функция 120. Привязка лицензии к оборудованию
Модуль/Класс: Licensing.MachineIdentity

Сигнатура: public string GetMachineId(MachineIdVariant variant) // VolumeSerial | BoardUuid | Composite

Логика: Вычисляет аппаратный идентификатор несколькими способами (серийный номер тома, UUID платы, составной отпечаток), результат — в реестр HKCU. Интегратор выбирает вариант при генерации ключа; на одном физическом сервере с несколькими ВМ это позволяет выдать ключи на каждую.

Данные: Реестр HKCU (MachineId), WMI.

Ошибки: Не удалось вычислить выбранный вариант — фолбэк на составной отпечаток с записью в журнал.

UI: Страница «Регистрация»: отображение текущего MachineID для обращения в поддержку.

Зависимости: Функция 119.

Проверка: Ключ, выписанный на MachineID машины A, отклоняется на машине B с сообщением о привязке.

Функция 121. Детекция виртуальной машины
Модуль/Класс: Licensing.VmDetector

Сигнатура: public bool IsVirtualMachine()

Логика: Отдельная проверка признаков виртуализации (по данным WMI/BIOS). Обязательное требование — мягкая деградация: детект ВМ вызывает предупреждение или демо-режим, но не блокирует запуск и не использует низкоуровневые приёмы (порты ввода-вывода, перехват отладчика). Явно запрещено воспроизводить аппаратные трюки защиты вроде обращения к портам — они конфликтуют с корпоративными EDR.

Данные: WMI (Win32_ComputerSystem, BIOS).

Ошибки: Определить не удалось — считать физической машиной, запись в журнал.

UI: Предупреждение в окне «О программе» при работе в ВМ без ключа на эту ВМ.

Зависимости: Функция 120.

Проверка: Запуск в Hyper-V/VMware выводит предупреждение, приложение продолжает работу.

Функция 122. Онлайн-активация и офлайн-файл запроса
Модуль/Класс: Licensing.ActivationService

Сигнатура: public ActivationResult ActivateOnline(string key); public string BuildOfflineRequest()

Логика: Онлайн-активация отправляет ключ + MachineID на сервер активации, получает подписанный ответ (подпись проверяется локально). При отсутствии сети формируется офлайн-файл запроса со строгим именем На_регистрацию_ПК!{uuid}_{date}-{n}-{n}, который пользователь передает вендору; ответный файл активации применяется локально с той же проверкой подписи.

Данные: Файл лицензии (подпись), сервер активации (URL в настройках).

Ошибки: Неверная подпись ответа — отказ с сообщением о повреждении; недоступен сервер — предложение офлайн-пути.

UI: Окно «Регистрация программы»: поле «Ключ:», кнопки «Активировать онлайн» / «Создать файл запроса» / «Применить файл ответа».

Зависимости: Функции 119, 120.

Проверка: Активация офлайн-цепочкой (запрос → ответ) переводит состояние в Licensed без сетевого доступа.

Функция 123. Статус ключа, снятие ключа, активация на нескольких управляющих
Модуль/Класс: Licensing.LicenseManager

Сигнатура: public LicenseStatus GetStatus(); public void RemoveKey()

Логика: Отображает редакцию, дату истечения, владельца; снятие ключа возвращает trial с сохранением данных. Поддерживается активация ключей на нескольких «управляющих» машинах (один ключ — несколько привязок по MachineID в пределах лимита лицензии).

Данные: Файл лицензии, реестр.

Ошибки: Снятие ключа — подтверждение с предупреждением о переходе в trial.

UI: Кнопка «Снять ключ» со статусной сводкой.

Зависимости: Функция 122.

Проверка: Снятие ключа возвращает NAG-экран; повторная активация восстанавливает редакцию без потери данных.

Функция 124. Шифрование данных лицензии
Модуль/Класс: Licensing.LicenseCrypto

Сигнатура: public byte[] Protect(byte[] plain); public byte[] Unprotect(byte[] cipher)

Логика: Локальные данные лицензии шифруются симметричным алгоритмом с ключом, производным от MachineID (AES-256; применение устаревшего TripleDES не допускается). Ключ шифрования не хранится рядом с данными и не выводится.

Данные: Реестр/файл лицензии.

Ошибки: Не удалось расшифровать — данные лицензии считаются отсутствующими, состояние trial.

UI: Нет.

Зависимости: Функция 120.

Проверка: Редактирование зашифрованного блока вручную переводит приложение в trial, а не в падение.

Функция 125. Защита процесса (архитектурный запрет)
Модуль/Класс: — (решение уровня архитектуры; отдельный модуль самозащиты НЕ создаётся)

Сигнатура: —; проверяется ревью сборки: в проекте отсутствуют вызовы низкоуровневой самозащиты.

Логика: Агрессивные приёмы самозащиты процесса (перехват отладчика, обращение к портам ввода-вывода для детекции отладки, анти-мониторинговые хуки) в системе НЕ реализуются — они конфликтуют с корпоративными антивирусами, EDR и политиками ИБ. Защита ограничивается: подписью лицензии (Функция 122), шифрованием данных (Функция 124) и привязкой к оборудованию (Функция 120). Дополнительно реализуется только проверка целостности собственных сборок (хеш при старте).

Данные: —

Ошибки: Нарушение целостности сборки — предупреждение с предложением переустановки.

UI: Нет.

Зависимости: Функции 119–124.

Проверка: Аудит проекта: отсутствие импортов низкоуровневых приёмов; запуск под отладчиком/антивирусом не вызывает ложных срабатываний.

Функция 126. Три редакции продукта
Модуль/Класс: Licensing.Editions

Сигнатура: public Edition Current { get; } // Free | Home | Professional

Логика: Редакции с разными лимитами: Free — базовые печать и адресная книга с лимитами trial; Home — без лимитов времени, печать всех форматов конвертов; Professional — без лимита адресов, импорт из Excel/CSV, автоматический разбор адресов, автоматизация, обновления и поддержка. При попытке использовать возможность старшей редакции показывается окно преимуществ со списком того, что даёт переход, а не сухое «недоступно».

Данные: Файл лицензии (поле Edition).

Ошибки: Нет.

UI: Окно апгрейда со списком преимуществ; пункты меню старших редакций помечены значком редакции.

Зависимости: Функции 119, 123.

Проверка: В Free пункты импорта Excel видны, но при выборе открывают окно преимуществ с перечнем получаемых функций Professional.

8.11. Операционные функции
Функция 127. Клиент-серверная топология
Модуль/Класс: Infrastructure.Persistence.ServerRegistry (расширение Функции 101)

Сигнатура: public TopologyInfo DiagnoseTopology()

Логика: Поддержка конфигураций: сервер с общей БД + N клиентов; клиент без сервера (локальная SQLite); сервер без клиентов (головной офис). Диагностика сообщает роль текущей машины и состояние связи. Клиент указывает IP сервера однократно; логин/пароль у клиента не настраиваются — используются единые реквизиты подключения из защищенного файла.

Данные: servers.ini, защищенное хранилище реквизитов.

Ошибки: Несовпадение версии схемы клиента и сервера — блокировка с указанием версий.

UI: Страница «Подключение» настроек; панель диагностики.

Зависимости: Функции 101, 102.

Проверка: Отключение сервера переводит клиентов в режим «только чтение кэша» с периодическими повторами соединения.

Функция 128. Архивирование три раза в сутки с паролем и сроком
Модуль/Класс: Infrastructure.Backup.ScheduledBackupService (расширение Функции 103)

Сигнатура: public void ConfigureSchedule(TimeOnly[] points, string folder, string password, int retentionDays)

Логика: Три настраиваемые точки архивации в сутки (по умолчанию 09:00, 13:05, 16:05), целевые папки — включая сетевые и «облачные», подключённые как обычные папки Windows (отдельной интеграции с облаками нет — это следствие выбора «папки»), ZIP с паролем и сроком хранения.

Данные: parameters (расписание, папка, срок).

Ошибки: Занятость файла предыдущей копии — имя дополняется суффиксом времени.

UI: Поля трех времен в диалоге архивирования.

Зависимости: Функция 103.

Проверка: Настройка трех времен выполняет три архивации в сутки; копии старше срока удаляются.

Функция 129. Восстановление из архива (операционный контур)
Модуль/Класс: Infrastructure.Backup.RestoreService (расширение Функции 104)

Сигнатура: public RestoreResult RestoreInteractive(string archivePath)

Логика: Восстановление из той же формы, что и архивирование: выбор архива, ввод пароля, предпросмотр содержимого (дата, версия схемы, число записей), страховочная копия текущего состояния перед применением.

Данные: Архивы Base_*.zip.

Ошибки: См. Функцию 104.

UI: Кнопка «Восстановить из архива» с мастером предпросмотра.

Зависимости: Функции 103, 104.

Проверка: Восстановление вчерашней копии откатывает изменения дня и создает страховочный архив текущего состояния.

Функция 130. Установка на USB-флешку (два варианта)
Модуль/Класс: Infrastructure.Deployment.UsbInstaller (расширение Функции 105)

Сигнатура: public UsbInstallResult InstallToUsb(string usbRoot, UsbInstallMode mode) // Full | DatabaseOnly

Логика: Вариант 1 «Всё целиком»: программа + переносная СУБД + БД. Вариант 2 «Только база данных» (рекомендуемый): флешка носит только БД; рабочие станции используют её как источник. Мастер показывает требуемый и доступный объем.

Данные: Файловая система.

Ошибки: Недостаточно места — блокировка до освобождения.

UI: Мастер с двумя режимами и сводкой объема.

Зависимости: Функция 105.

Проверка: Оба режима дают работающую конфигурацию на целевой машине без установки.

Функция 131. Настройка вида таблиц
Модуль/Класс: UI.Components.GridAppearanceManager (src/UI/Components/GridAppearanceManager.cs)

Сигнатура: public void ApplyAppearance(GridAppearance appearance); public void ResetToDefaults(Scope scope) // Grid | MainForm

Логика: Диалог с тремя вкладками (§7.6): цвета и заголовки столбцов; атрибуты столбцов и строк + глобальная «Полосатая таблица»; сброс к умолчаниям для конкретной таблицы или главной формы (сброс главной формы требует перезапуска — предупреждение выводится явно). Три независимых цвета (фокус/выделение/подсказка) и «зебра» применяются ко всем гридам программы единообразно.

Данные: Профиль пользователя (JSON в AppData).

Ошибки: Поврежденный файл профиля — автоматический сброс на умолчания.

UI: Контекстное меню грида → «Настройка таблицы».

Зависимости: Функция 12.

Проверка: Изменение цвета выделения применяется ко всем таблицам после закрытия диалога; сброс главной формы выводит предупреждение о перезапуске.

Функция 132. Три механизма поиска
Модуль/Класс: UI.Components.SearchController (src/UI/Components/SearchController.cs)

Сигнатура: public void IncrementalSearch(string keystrokes); public SearchResult FindNext(SearchQuery q); public void ApplyFilter(FilterKind kind, object value)

Логика: (1) Форма поиска Ctrl+F — последовательный перебор с кнопкой «Найти далее», показывает одну следующую запись; (2) прямой набор с клавиатуры при курсоре в таблице вне режима редактирования — живой отбор по начальным буквам, сброс по Esc (возможно дважды — действует несколько условий поиска); (3) фильтры — два раздельных вида: встроенный «Избранное» и пользовательские группы (сливать нельзя). Плюс подбор значения поля из имеющихся записей: пустое поле + несколько нажатий BackSpace.

Данные: Текущая выборка грида; counterparties.

Ошибки: «Совпадений не найдено» — сообщение в строке состояния без модальных окон.

UI: Форма поиска; панель фильтров F4; строка инкрементального поиска.

Зависимости: Функции 9, 17.

Проверка: Набор «Иван» в таблице последовательно сужает список; Esc (при необходимости дважды) возвращает полный список; фильтр «Избранное» и фильтр группы действуют независимо.

Функция 133. Подбор значения поля из имеющихся записей
Модуль/Класс: UI.Components.FieldAutoComplete

Сигнатура: public IEnumerable<string> SuggestValues(string fieldName, string prefix)

Логика: При курсоре в пустом поле редактора карточки нажатия BackSpace циклически подставляют существующие значения этого поля из БД (уникальные, по алфавиту) — быстрый ввод без справочников.

Данные: DISTINCT-выборка по колонке.

Ошибки: Нет значений — поле остается пустым.

UI: Поведение полей карточки контрагента.

Зависимости: Функция 1.

Проверка: В пустом поле «Город» повторные BackSpace перебирают существующие города по алфавиту.

Функция 134. Скрытый вход в настройки подключения
Модуль/Класс: UI.Shell.ShortcutInterceptor

Сигнатура: public bool HandleShortcutLaunch(LaunchArgs args)

Логика: Запуск с зажатым Ctrl и двойным щелчком по ярлыку программы открывает диалог настройки подключения к БД без загрузки главного окна — осознанный «скрытый» вход для администратора при недоступности сервера.

Данные: Параметры запуска процесса.

Ошибки: Нет.

UI: Диалог «Подключение к базе данных» до главной формы.

Зависимости: Функция 102.

Проверка: Ctrl+двойной щелчок по ярлыку открывает только диалог подключения; обычный запуск — штатную загрузку.

Функция 135. Пункт в контекстном меню Windows Explorer
Модуль/Класс: Infrastructure.Shell.ExplorerIntegration (расширение Функции 106)

Сигнатура: public void RegisterContextMenu()

Логика: Регистрирует пункт вызова программы для файлов данных (CSV/XLS/XLSX/XML) и папок в HKCU; активация передает выбранный путь как параметр импорта.

Данные: Реестр HKCU.

Ошибки: Отказ записи — сообщение (см. Функцию 106).

UI: Флажок в настройках.

Зависимости: Функции 71, 106.

Проверка: Пункт присутствует только при включенном флажке и исчезает после снятия.

Функция 136. Диагностика «сервер БД не запускается»
Модуль/Класс: Infrastructure.Persistence.DbServiceDoctor (src/Infrastructure/Persistence/DbServiceDoctor.cs)

Сигнатура: public DiagnosisReport DiagnoseDbService()

Логика: Диагностирует типовую причину отказа запуска сервера БД: остатки предыдущей установки (зарегистрированная служба СУБД от удалённой программы). Проверяет список служб, находит осиротевшую службу, сообщает пользователю и предлагает корректное удаление службы (sc delete / диалог uninstall) с подтверждением.

Данные: Диспетчер служб Windows.

Ошибки: Требуются права администратора для удаления службы — запрос повышения с пояснением.

UI: Диалог диагностики при ошибке старта сервера; пункт в настройках подключения.

Зависимости: Функция 101.

Проверка: На машине с осиротевшей службой СУБД диагностика находит её и после подтверждения удаляет, сервер БД стартует.

Функция 137. Демо-база при первом запуске
Модуль/Класс: Application.Setup.DemoDatabaseSeeder (src/Application/Setup/DemoDatabaseSeeder.cs)

Сигнатура: public void SeedDemoData(); public void ClearDatabase(ClearScope scope)

Логика: При первом запуске программа предлагает начать с чистой базы или с демонстрационной (готовый каталог контрагентов с адресами и категориями — отдельная копия данных, а не пометка записей). Экран «Очистка базы» позволяет очистить выборочно: категории контрагентов / базу рассылок / базу контрагентов / базу отправленных писем.

Данные: Файл демо-данных в поставке; таблицы БД.

Ошибки: Очистка требует подтверждения с точным числом удаляемых записей.

UI: Диалог первого запуска; экран «Очистка базы» с четырьмя областями.

Зависимости: Функции 1, 8, 153.

Проверка: Первый запуск предлагает выбор; очистка «базы контрагентов» удаляет записи, но сохраняет категории и рассылки.

Функция 138. Категория «Неразобранные»
Модуль/Класс: Application.Services.GroupManager (инвариант из §3.1.2)

Сигнатура: public Group EnsureDefaultGroup()

Логика: Автосоздаваемая категория по умолчанию: создается при инициализации БД; ровно одна группа с is_default = TRUE; новая запись адресной книги без указанной категории попадает в неё автоматически. Удаление группы по умолчанию блокируется.

Данные: groups (is_default, is_closed).

Ошибки: Попытка удалить/закрыть «Неразобранные» — блокировка с сообщением.

UI: Дерево категорий; метка «по умолчанию».

Зависимости: Функция 8.

Проверка: На чистой базе группа создается автоматически; контрагент без категории виден в «Неразобранные».

Функция 139. Флаг «Это отправитель» у записи адресной книги
Модуль/Класс: Domain.Entities.Counterparty (is_sender)

Сигнатура: public void MarkAsSender(bool value)

Логика: Отправитель — флаг у записи адресной книги: помеченные записи доступны в выпадающем списке отправителей главного окна и в мастерах рассылок. Модель единственная (флаг), альтернативная отдельная таблица отправителей не ведется — исключены два конкурирующих способа задать отправителя. Логотип и подпись привязываются к записи отправителя.

Данные: counterparties.is_sender; справочник отправителей = выборка по флагу.

Ошибки: Удаление текущего отправителя сбрасывает выбор на первую запись справочника.

UI: Чекбокс «Это отправитель» в карточке; списки выбора отправителя.

Зависимости: Функции 1, 16.

Проверка: Установленный флаг делает контрагента доступным в списке отправителей; снятие флага исключает из списка.

Функция 140. Drag & drop получателей в рассылку
Модуль/Класс: UI.Forms.FormDistributionEditor

Сигнатура: public void OnRecipientsDropped(IReadOnlyList<long> counterpartyIds)

Логика: Формирование рассылки перетаскиванием получателей из адресной книги (левая панель) в список рассылки (правая панель); во время перетаскивания доступны поиск и фильтрация по категориям; кнопка «Добавить всех активных» добавляет всех активных контрагентов текущего отбора. Счетчик «Всего необходимо конвертов» пересчитывается при каждом изменении состава.

Данные: mails.distribution_id; distributions.envelopes_required.

Ошибки: Дубликаты получателей игнорируются с подсветкой.

UI: Двухпанельный редактор рассылки с drag&drop.

Зависимости: Функции 21, 155.

Проверка: Перетаскивание 10 контрагентов добавляет 10 получателей и увеличивает счетчик конвертов на сумму их копий.

Функция 141. «Объединить в рассылку»
Модуль/Класс: Application.Services.DistributionService (контекстное действие)

Сигнатура: public Distribution CreateFromSelection(IReadOnlyList<long> counterpartyIds)

Логика: Контекстное действие над выделением в адресной книге: создает рассылку и немедленно открывает редактор с уже добавленными получателями — одним действием, без ручного перетаскивания. Текущий отправитель подставляется автоматически.

Данные: distributions, mails.

Ошибки: Пустое выделение — пункт меню неактивен.

UI: Пункт контекстного меню «Объединить в рассылку».

Зависимости: Функции 16, 21, 140.

Проверка: Выделение 25 контрагентов → «Объединить в рассылку» открывает редактор с 25 получателями и заполненным счетчиком.

Функция 142. Повторная отправка сохранённой рассылки
Модуль/Класс: Application.Services.DistributionService

Сигнатура: public Distribution CloneForResend(long distributionId)

Логика: Создает копию сохранённой/отправленной рассылки с тем же составом получателей (по текущим адресам контрагентов), новым идентификатором и статусом «Черновик»; исходная рассылка не изменяется. Поддерживаются правка состава перед повторной отправкой и печать рассылки.

Данные: distributions, mails.

Ошибки: Удаленные к этому моменту контрагенты пропускаются с перечислением в предупреждении.

UI: Кнопка «Переотправить» в списке рассылок.

Зависимости: Функции 21, 156.

Проверка: Повторная отправка рассылки из 40 получателей создает новую рассылку с тем же числом писем и не меняет историю первой.

Функция 143. Пробная печать шаблона
Модуль/Класс: Printing.PrintSessionController (режим TestPrint)

Сигнатура: public PrintResult PrintTestSample(string templateName)

Логика: Печать одного пробного экземпляра выбранного шаблона на текущем принтере с текущими настройками — без создания записей mails. Прямое назначение: сверка совпадения полей с партией конвертов перед пакетной печатью (в разных партиях конвертов поля смещены на 1–2 мм и более). После пробной печати программа подсказывает: «Убедитесь, что поля совпали, прежде чем запускать пакетную печать».

Данные: Тестовые данные текущей карточки или заглушки; журнал print_jobs (print_mode = test).

Ошибки: Стандартные проверки геометрии Функции 83 действуют и здесь.

UI: Кнопка «Пробная печать» на вкладке конвертов и в диалоге печати.

Зависимости: Функции 79, 83.

Проверка: Пробная печать не создает записей в mails и печатает один экземпляр; журнал содержит запись с print_mode = test.

Функция 144. Библиотека из более чем 50 готовых бланков
Модуль/Класс: Infrastructure.Templates.BuiltinTemplatesCatalog (расширение Функции 37)

Сигнатура: public IReadOnlyList<TemplateDescriptor> LoadCatalog()

Логика: Каталог содержит более 50 готовых бланков форматов DL, C6, C5, C4, B4, включая праздничные с отличающейся разметкой; каждый бланк имеет миниатюру, метку формата и страны. Праздничные — это отдельные пакеты с собственной разметкой, не копии стандартных.

Данные: Templates/Packages/*.lbk.

Ошибки: Поврежденный пакет исключается из каталога с записью в журнал (см. Функцию 37).

UI: Галерея бланков с фильтром по формату/тематике.

Зависимости: Функции 37, 40.

Проверка: В галерее доступно не менее 50 бланков; праздничные отображают собственную разметку в предпросмотре.

Функция 145. Пресет «стандартная разметка» для формата
Модуль/Класс: Printing.Templates.StandardLayoutPresets (src/Printing/Templates/StandardLayoutPresets.cs)

Сигнатура: public BlankLayout GetPreset(EnvelopeFormat format, string country); public BlankLayout CreateEmpty(EnvelopeFormat format)

Логика: При создании нового конверта пользователь выбирает формат и страну; галка «Использовать стандартную разметку» подставляет пресет полей по формату и стране (зоны «Кому»/«Куда», рамки индексов, служебные зоны; страна влияет на нормативную разметку). Снятая галка дает пустой холст — все поля добавляются вручную. Список форматов строится из таблицы форматов стандарта (включая варианты «/О» с окном), а не задается константами вручную.

Данные: Справочник геометрий и разметок (данные, не код).

Ошибки: Нет пресета для страны — холст с геометрией формата без адресных зон, предупреждение.

UI: Диалог «Новый конверт»: Формат, Ширина, Высота, Страна, галка «Использовать стандартную разметку».

Зависимости: Функции 38, 42, 149.

Проверка: Создание C6/RUS с галкой дает макет с заполненными адресными зонами и рамкой индекса; без галки — пустой макет 162×114 мм.

Функция 146. Печать адресных этикеток на A5/A4/A3 с сохранением в PDF
Модуль/Класс: Printing.Labels.LabelSheetComposer (src/Printing/Labels/LabelSheetComposer.cs)

Сигнатура: public SheetLayout Compose(IReadOnlyList<MailAddressRecord> records, LabelSheetOptions options); public void ExportToPdf(SheetLayout layout, string pdfPath)

Логика: Раскладывает адресные этикетки сеткой на листах A5/A4/A3: поля слева/справа и сверху/снизу в мм, число копий, качество, формат бумаги, ориентация, выпуск под обрез (bleed) и маркеры обреза (crop marks). Сформированный макет можно распечатать или сохранить в PDF (PDF 1.4) для последующей печати. Выбор шаблона — «Стандартные этикетки» или «Мои этикетки» (пользовательские).

Данные: Выборка получателей; параметры листа.

Ошибки: Этикетка не помещается в сетку листа — блокировка с указанием максимальной геометрии.

UI: Диалог печати этикеток (блоки «Шаблон», «Список получателей», «Отправитель», «Бумага»).

Зависимости: Функции 44, 79, 158.

Проверка: Сетка 3×7 на A4 выводит 21 этикетку за проход; сохраненный PDF при повторной печати идентичен прямому выводу.

Функция 147. Физическая подача «меньшей стороной»
Модуль/Класс: Printing.Warnings.FeedOrientationHint

Сигнатура: public void ShowFeedOrientationWarningIfNeeded()

Логика: Обучающее предупреждение при печати конверта: «Для корректной печати конверты следует загружать в принтер узкой стороной» с галкой «Больше не показывать это сообщение». Тот же совет — обязательный раздел справки (включая приём печати конверта на листе A4 с фиксацией скотчем для принтеров, плохо захватывающих конверты).

Данные: Флаг подавления в профиле пользователя.

Ошибки: Нет (предупреждение).

UI: Модальное окно с галкой подавления; раздел справки.

Зависимости: Функция 79.

Проверка: Предупреждение появляется однократно; после включения галки не появляется до сброса настроек; раздел справки доступен из F1.

Функция 148. Автоматическое распознавание адресов при импорте
Модуль/Класс: Application.Services.AddressHeuristicsService (src/Application/Services/AddressHeuristicsService.cs)

Сигнатура: public ParsedAddress Recognize(string rawLine)

Логика: При импорте Excel/CSV эвристика раскладывает свободную строку адреса на компоненты: индекс (\b\d{6}\b), регион (словарь топонимов «обл./край/Республика»), населенный пункт («г./пгт/с.»), улица-дом-квартиру («ул./пр./д./кв./корп./стр.»), ФИО (три слова с окончаниями отчества). Результат — заполненные строки str1..str6 с возможностью ручной правки в предпросмотре записи.

Данные: Словари топонимов и сокращений (поставляются файлами, расширяемы).

Ошибки: Не распознанный фрагмент целиком попадает в первую свободную строку; запись помечается NeedsVerification.

UI: Шаг предпросмотра записи мастера импорта (поля Регион / Населенный пункт / Улица, дом, кв. / Индекс / ФИО).

Зависимости: Функции 5, 72, 159.

Проверка: Строка «350000, г. Краснодар, ул. Красная, д. 35, оф. 4, Иванов Иван Иванович» раскладывается на индекс 350000, город, улицу-дом и ФИО без ручной правки.

Функция 149. Поддержка форматов конвертов России и Украины
Модуль/Класс: Domain.Postal.EnvelopeFormatCatalog

Сигнатура: public EnvelopeGeometry Get(string formatCode, string countryCode)

Логика: Справочник геометрий для территорий России и Украины: российская серия (C6/DL/C5/C4/C3/B4/E65-E4/K5/K6/K8/K65) и украинская серия (C6/DL/C5/C4/E65 с национальной разметкой кодового штампа). Страна — свойство бланка, влияющее на нормативную разметку адресных зон и подсказки полей; справочник — данные, а не код.

Данные: Файл справочника форматов (JSON).

Ошибки: Неизвестный код формата — предложение пользовательского размера.

UI: Выбор страны и формата в диалоге «Новый конверт».

Зависимости: Функции 42, 145.

Проверка: Выбор страны «Украина» меняет доступный список форматов и разметку пресета.

Функция 150. Сумма прописью с локализацией
Модуль/Класс: Infrastructure.Text.AmountInWords (src/Infrastructure/Text/AmountInWords.cs)

Сигнатура: public string ToWords(decimal amount, string lngFile)

Логика: Формирует сумму прописью по словарю .lng: числительные по падежам (One/Ten/Twen/Hndr/Thou/Mill/Bill/Thrill), валюта и копейки в трех формах (Currency, Cop), род существительного (CurrencyMale: 1 — мужской, 0 — женский/сокращение), десятичный разделитель (Comma), месяцы и дни недели в двух падежах (Month1/Month2, DayOfWeek). Склонение — матрица форм по последним цифрам числа и типу существительного. Используется в бланках почтовых переводов (ф.112э/эп) и реестрах.

Данные: lng/<валюта>.lng (Rub, Byn, Byr, Uah, usd; Kazakhstan — пустой, см. §13).

Ошибки: Неполный словарь (отсутствующий ключ) — сумма цифрами + предупреждение в журнал.

UI: Автоподстановка в бланках; кнопка предпросмотра в редакторе шаблонов.

Зависимости: Функции 36, 50.

Проверка: 21 рубль → «двадцать один рубль»; 1 грн → «одна грн» (род не применяется, CurrencyMale=0); «1 января 2024 г.» формируется через Month1.

Функция 151. Локализация как расширяемый набор файлов
Модуль/Класс: Infrastructure.Localization.LanguageManager (расширение Функции 112)

Сигнатура: public ILanguagePack LoadPack(string file)

Логика: Набор локалей — файлы, а не захардкоженный словарь: в поставке покрыты Россия/Беларусь/Румыния/Украина/доллар США; Каза­хстан поставляется пустым шаблоном (пробел покрытия исходных материалов). Новый язык/валюта добавляются копированием шаблона .lng и заполнением обязательных ключей; валидатор проверяет полноту ключей при загрузке.

Данные: lng/*.lng, lang/*.xml.

Ошибки: Отсутствие обязательного ключа — файл отклоняется с перечнем недостающих ключей.

UI: Диалог выбора языка/валюты; справка «Как добавить язык».

Зависимости: Функция 112.

Проверка: Копирование шаблона, заполнение ключей и перезапуск делают новую валюту доступной в суммах прописью без правки кода.

8.12. Функции почтовых приложений
Функция 152. Четыре вкладки главного окна
Модуль/Класс: UI.Shell.MainShell (src/UI/Shell/MainShell.cs)

Сигнатура: public void SwitchTab(ShellTab tab) // AddressBook | Envelopes | Distributions | Sent

Логика: Главное окно разделено на четыре вкладки: «Адресная книга» (таблица контрагентов, дерево категорий, поиск, сортировка, кнопки Добавить/Печать/Печать этикеток на левой боковой панели), «Конверты» (каталог бланков; двойной щелчок — просмотр бланка; кнопки Создать/Редактировать/Пробная печать), «Рассылки» (список рассылок: новая, печать, сохранённые и отправленные), «Отправленные» (информация об отправленных конвертах; запись появляется сразу после печати — печать является фактом учёта отправления). Состояние вкладки (выбранная категория, фильтры, сортировка) сохраняется между сеансами.

Данные: counterparties, print_templates, distributions, mails/print_history.

Ошибки: Нет.

UI: Корневой PageControl главного окна; строка состояния со счетчиками активной вкладки.

Зависимости: Функции 1, 36, 21, 64.

Проверка: После печати конверта запись появляется на вкладке «Отправленные» без перезагрузки; состояние вкладок переживает перезапуск.

Функция 153. Демо-база и очистка базы
Модуль/Класс: Application.Setup.DemoDatabaseSeeder (операционный контур Функции 137)

Сигнатура: public void SeedDemo(); public void ClearByScope(ClearScope scope)

Логика: Первый запуск предлагает чистую или демонстрационную базу (готовый каталог контрагентов и категорий для освоения). Экран «Очистка базы» выполняет выборочную очистку по типам данных: «Категории контрагентов», «База рассылок», «База контрагентов», «База отправленных писем» — каждая с подтверждением и числом затрагиваемых записей.

Данные: Таблицы БД по областям.

Ошибки: Очистка «Базы контрагентов» при активных рассылках — предупреждение о каскадном обнулении получателей.

UI: Диалог первого запуска; экран «Очистка базы» с четырьмя переключателями.

Зависимости: Функции 137, 138.

Проверка: Демо-база содержит категории и контрагентов; очистка «Базы рассылок» не трогает адресную книгу.

Функция 154. Категории адресатов: дерево и операции
Модуль/Класс: Application.Services.CategoryTreeService (src/Application/Services/CategoryTreeService.cs)

Сигнатура: public Result<Category> CreateCategory(string name, long? parentId); public Result RenameCategory(long id, string newName); public Result DeleteCategory(long id)

Логика: Дерево категорий адресатов: создание (включая вложенность), переименование, удаление; автосоздаваемая «Неразобранные» (см. Функцию 138). Удаление категории переносит её записей в «Неразобранные» (не удаляет контрагентов). Перемещение контрагента в категорию — отдельное действие «Переместить в категорию».

Данные: groups (дерево через parent_id).

Ошибки: Удаление «Неразобранные» блокируется; циклы вложенности пресекаются.

UI: Дерево категорий на вкладке «Адресная книга»; диалоги создания/переименования/перемещения.

Зависимости: Функции 8, 138.

Проверка: Удаление категории с 12 контрагентами перемещает их в «Неразобранные» и не уменьшает размер адресной книги.

Функция 155. Формирование рассылки по категориям
Модуль/Класс: UI.Forms.FormMailingWizard

Сигнатура: public Distribution BuildDistribution(MailingKind kind, IReadOnlyList<long> categoryIds)

Логика: Два режима: «Рассылка на категории» (выбор нескольких категорий) и «Рассылка на категорию»; получатели добавляются drag&drop (Функция 140) или кнопкой «Добавить всех активных»; доступен фильтр по категориям во время добавления. Поле «Всего необходимо конвертов» считается программой автоматически (сумма copy_count). Новая рассылка: Название, Отправитель, Примечание; кнопка «Создать отправителя»; действие «Печать рассылки».

Данные: distributions, mails, groups.

Ошибки: Рассылка без отправителя — предложение создать прямо в диалоге.

UI: Мастер рассылки с двумя панелями и счетчиком конвертов.

Зависимости: Функции 21, 139, 140, 141, 157.

Проверка: Рассылка на две категории суммирует их активных контрагентов; счетчик конвертов равен сумме копий.

Функция 156. Сохранённые и отправленные рассылки
Модуль/Класс: Application.Services.DistributionService (контур списков)

Сигнатура: public IReadOnlyList<DistributionView> ListDistributions(DistributionFilter filter)

Логика: Список рассылок с фильтром «Созданы с» (дата); по рассылке доступны: правка состава, повторная отправка (Функция 142), печать рассылки (открывает диалог печати с выбором формата и бланка). Состояния рассылки: Черновик / Сохранена / Отправлена — отображаются маркерами.

Данные: distributions (state, created_at).

Ошибки: Печать пустой рассылки блокируется с сообщением.

UI: Вкладка «Рассылки»: список, фильтр по дате, кнопки действий.

Зависимости: Функции 21, 142, 155.

Проверка: Фильтр «Созданы с» скрывает более старые рассылки; повторная отправка сохраняет историю первой.

Функция 157. Отправители (агенты)
Модуль/Класс: Application.Services.AgentService

Сигнатура: public Counterparty CreateAgent(AgentDto dto); public IReadOnlyList<Counterparty> ListAgents()

Логика: Отправитель оформляется как сущность «агент» (запись адресной книги с флагом отправителя — Функция 139): создание агента прямо из диалога рассылки кнопкой «Создать отправителя»; выбор агента при печати и в рассылках. У агента хранятся логотип (пара ориентаций), подпись, реквизиты.

Данные: counterparties (is_sender), логотипы.

Ошибки: Нет.

UI: Диалоги «Новый агент» и «Выбрать отправителя».

Зависимости: Функции 139, 47.

Проверка: Созданный из мастера агент сразу доступен в выпадающем списке отправителей рассылки.

Функция 158. Шаблоны, клипарт, шрифты и цвета
Модуль/Класс: UI.Forms.FormTemplateGallery + Infrastructure.Templates.ClipartLibrary (src/UI/Forms/FormTemplateGallery.cs)

Сигнатура: public void SaveAsTemplate(BlankLayout layout, string name); public byte[] PickClipart(); public FontInfo PickFont(); public ColorInfo PickColor()

Логика: Каталог шаблонов с сохранением собственных («Сохранить шаблон», «Мои конверты»/«Мои этикетки» — личные папки с подсказкой при пустоте «Вы ещё не создали конвертов данного размера»); библиотека клипарта (выбор картинки из поставляемой библиотеки растров с предпросмотром); диалоги выбора шрифта и цвета (включая всплывающий выбор цвета). Настраиваемые шрифты и цвета — первоклассная функция дизайнера.

Данные: Файлы шаблонов пользователя; clipart/*.png|jpg.

Ошибки: Поврежденная картинка клипарта — заглушка с предупреждением.

UI: Вкладка «Конверты»: галерея, кнопки «Новый шаблон», «Сохранить шаблон», «Выбор картинки», «Выбрать шрифт», «Выбрать цвет».

Зависимости: Функции 38, 40, 144.

Проверка: Сохраненный пользовательский шаблон появляется в «Мои конверты» и доступен для печати; клипарт вставляется в макет.

Функция 159. Импорт с валидацией (эталонный сценарий)
Модуль/Класс: UI.Forms.FormImportWizard + Application.Services.ImportValidator (src/UI/Forms/FormImportWizard.cs)

Сигнатура: public ImportSession Start(string filePath); public ValidationSummary ValidateAll(); public ImportResult Commit(ImportPolicy policy)

Логика: Пошаговый мастер: (1) выбор файла и листа (показ «Количество листов/записей/столбцов»); (2) «Начать импорт с» N-й строки, выбор категории назначения или создание новой; (3) сопоставление по столбцам (не по именам): список полей ФИО / Название организации / Почтовый адрес / Юридический адрес / Фактический адрес / Прочее / Название / Сокращенное название; (4) пошаговый предпросмотр КАЖДОЙ записи с кнопками «<<» / «>>» / «Пропустить» и редактированием полей до импорта; (5) фильтр «Показывать: Все записи / Записи с ошибками»; (6) финал с тремя вариантами политики: «Добавить только проверенных» / «Добавить только записи без ошибок» / «Добавить все записи» — с конкретными числами непроверенных и ошибочных записей и четвертым выходом «Вернуться к редактированию».

Данные: counterparties; журналы импорта.

Ошибки: Превышение 255 символов — предупреждение об усечении на шаге предпросмотра; файл поля «слит» без разделителей — предупреждение о невозможности корректной загрузки до начала процесса.

UI: Мастер импорта (7 экранов).

Зависимости: Функции 71, 72, 73, 74, 148.

Проверка: Импорт файла с 8971 записями с ошибками показывает точные числа и позволяет добавить только записи без ошибок; пропущенная на предпросмотре запись не импортируется при политике «только проверенные».

Функция 160. Экспорт с настройками и пост-действиями
Модуль/Класс: Application.Services.ExportWizard (src/Application/Services/ExportWizard.cs)

Сигнатура: public void Export(ExportRequest request) // + postAction: None | OpenFile | OpenFolder

Логика: Экспорт адресной книги/базы отправлений: выбор формата файла (TXT, CSV, HTML, RTF, XLS, XLSX) и настроек даты (формат представления дат в выгрузке); после сохранения — диалог «Сохранение файлов завершено, что вы хотите сделать?» с кнопками «Открыть файл» / «Открыть папку» / «Закрыть».

Данные: Выборка контрагентов/писем.

Ошибки: Файл занят — предложение альтернативного имени (см. Функцию 61).

UI: Мастер экспорта в 2 шага + финальный диалог пост-действия.

Зависимости: Функции 61, 69, 71.

Проверка: Экспорт в CSV с форматом дат ISO открывается корректно; финальный диалог открывает папку файла в Проводнике.

Функция 161. Резервное копирование: два режима хранения и очистка
Модуль/Класс: Infrastructure.Backup.BackupPolicyService (src/Infrastructure/Backup/BackupPolicyService.cs)

Сигнатура: public void Configure(BackupMode mode) // DailyHistory | LastOnly

Логика: Два режима хранения: «Хранить данные за каждый день (занимает больше места)» и «Хранить только последнюю сохранённую базу» — в первом копии группируются по дням с автоочисткой по сроку, во втором хранится единственная последняя копия (предыдущая удаляется после успешного создания новой). Совмещается с расписанием Функции 128 и паролем архива.

Данные: parameters; целевая папка.

Ошибки: Ошибка создания новой копии в режиме LastOnly не удаляет предыдущую (безопасный отказ).

UI: Группа «Способ копирования» в диалоге резервного копирования.

Зависимости: Функции 103, 128.

Проверка: В режиме LastOnly в папке не бывает двух копий одновременно; сбой создания сохраняет предыдущую.

Функция 162. Регистрация и редакции
Модуль/Класс: UI.Forms.FormRegistration + Licensing.LicenseManager (интерфейсный контур Функций 119–126)

Сигнатура: public void ShowRegistration(); public void ShowEditionBenefits(Edition target)

Логика: Ввод ключа («Регистрация программы» → поле «Ключ:»); пробный период с ограничением срока (NAG «Осталось дней:»); окно преимуществ редакции при апгрейде Free → Full → Профи со списком получаемых возможностей; ветки «Купить полную версию» и «Ввести ключ».

Данные: Состояние лицензии (Функции 119–126).

Ошибки: Неверный ключ — сообщение с проверкой формата до обращения к активации.

UI: Окна регистрации, NAG-экран, окно преимуществ.

Зависимости: Функции 119, 122, 123, 126.

Проверка: Ввод корректного ключа убирает NAG-экран; окно преимуществ открывается из пунктов «Купить полную версию»/«Апгрейд до версии Профи».

Функция 163. Обновления и сервисные диалоги
Модуль/Класс: Infrastructure.Update.UpdateChecker + UI.Forms.ServiceDialogs (src/Infrastructure/Update/UpdateChecker.cs)

Сигнатура: public UpdateInfo CheckForUpdates(); public void DownloadUpdate(UpdateInfo info)

Логика: Проверка наличия обновлений по HTTP («Проверка наличия обновлений...») со скачиванием и запуском установки; форма исключений с готовым текстом для службы поддержки («Просьба сообщить о ней в службу поддержки. Пожалуйста, скопируйте и отправьте сообщение»); диалоги первого запуска, выбора путей; встроенные калькулятор и всплывающий календарь; диалог выбора цвета. Обновление устанавливается от имени пользователя без требования отключать защитник Windows.

Данные: URL обновлений (updates.txt по протоколу HTTPS); параметры в settings.

Ошибки: Недоступность сервера обновлений — тихая деградация с сообщением в журнал; сбой загрузки — возможность ручного повторного запуска.

UI: Меню «Сервис → Проверить обновление», форма исключений, диалоги сервиса.

Зависимости: Функция 111.

Проверка: При недоступном сервере обновлений приложение работает штатно и пишет запись в журнал ошибок; форма исключения содержит стек и текст для копирования.

9. Интеграции и автоматизация
9.1. COM-сервер автоматизации: публичный контракт IPEProApplication
Сервер автоматизации публикуется под ProgID PEPro.Application и регистрируется в HKCU\Software\Classes (без прав администратора). Контракт — 23 опубликованных метода интерфейса IPEProApplication (полный перечень — см. EVIDENCE/automation_idl.txt); всё остальное (подготовка команд, управление процессами, активация окон) — внутренний слой, наружу не протекающий.

Таблица 9.1. Публикуемые методы (23)

№
Метод
Сигнатура (COM/IDL)
Назначение

1
PrintFromFileEnvelopes
HRESULT PrintFromFileEnvelopes(BSTR filename)
Печать конвертов из файла формата EnvelopesData

2
PrintFromFileNotifications
HRESULT PrintFromFileNotifications(BSTR filename)
Печать уведомлений из файла

3
PrintFromFileInventories
HRESULT PrintFromFileInventories(BSTR filename)
Печать описей из файла InventoriesData

4
PrintFromFileRegisters
HRESULT PrintFromFileRegisters(BSTR filename)
Печать реестров из файла RegistersData

5
PrintFromXmlEnvelopes
HRESULT PrintFromXmlEnvelopes(BSTR xml_text)
Печать конвертов из XML-строки

6
PrintFromXmlNotifications
HRESULT PrintFromXmlNotifications(BSTR xml_text)
Печать уведомлений из XML-строки

7
PrintFromXmlInventories
HRESULT PrintFromXmlInventories(BSTR xml_text)
Печать описей из XML-строки

8
PrintFromXmlRegisters
HRESULT PrintFromXmlRegisters(BSTR xml_text)
Печать реестров из XML-строки

9
PrintEnvelope
HRESULT PrintEnvelope(BSTR from, BSTR fromAddress, BSTR fromZipCode, BSTR to, BSTR toAddress, BSTR toZipCode)
Печать одного конверта из шести строковых параметров (индекс передается отдельным третьим аргументом каждой стороны)

10
OpenDummyFormToSetAsForeground
BSTR OpenDummyFormToSetAsForeground()
Открыть вспомогательное немодальное окно и перехватить фокус переднего плана; возвращает заголовок окна, находившегося на переднем плане

11
CloseDummyFormAndSetAsForeground
HRESULT CloseDummyFormAndSetAsForeground(BSTR process)
Закрыть вспомогательное окно, вернув фокус вызывающему процессу

12
DatabaseIsLoaded
VARIANT_BOOL DatabaseIsLoaded()
Признак загруженной БД (пользовательские бланки доступны только при загруженной БД)

13
SafeLoadDatabase
VARIANT_BOOL SafeLoadDatabase()
Идемпотентная безопасная загрузка БД; обязательна перед операциями с пользовательскими бланками

14
GetFlagParseZipIndexFromAddress
VARIANT_BOOL GetFlagParseZipIndexFromAddress()
Прочитать флаг «выкусывать индекс из строки адреса»

15
SetFlagParseZipIndexFromAddress
VARIANT_BOOL SetFlagParseZipIndexFromAddress(VARIANT_BOOL f)
Установить флаг разбора индекса

16
GetFlagShowFormsAsModal
VARIANT_BOOL GetFlagShowFormsAsModal()
Прочитать флаг модального показа окон

17
SetFlagShowFormsAsModal
VARIANT_BOOL SetFlagShowFormsAsModal(VARIANT_BOOL f)
Установить модальный/немодальный режим (для автоматизации — немодальный)

18
GetProductVersion
BSTR GetProductVersion()
Версия продукта (строка вида «2.1.0.6»)

19
GetAPIVersion
long GetAPIVersion()
Версия контракта автоматизации (целое, сквозная нумерация)

20
GetRegisterTemplateNames
SAFEARRAY(BSTR) GetRegisterTemplateNames()
Список пользовательских шаблонов реестров

21
PrintFromXmlRegistersWithoutForm
HRESULT PrintFromXmlRegistersWithoutForm(BSTR xml_text, BSTR nametemplate, BSTR resultFullPath)
Печать реестра без показа формы с сохранением результата по указанному пути (для автоматизации)

22
GetWarningType
long GetWarningType()
Код результата последней операции (см. таблицу 9.2)

23
GetStdRegisterTemplateNames
SAFEARRAY(BSTR) GetStdRegisterTemplateNames()
Список стандартных шаблонов реестров

Таблица 9.2. Коды успешности GetWarningType (long)

Код
Значение

0
Печать реестра или получение списка шаблонов выполнено без ошибок

1
База данных отсутствует или некорректна

2
Произошла ошибка при чтении XML-данных

3
Не удалось сформировать документ реестра (внутренняя ошибка конвейера документов)

4
Отсутствует файл шаблона

9.2. Правила поведения COM-сервера
Отказоустойчивость: ни одно исключение внутри сервера не должно аварийно завершать процесс; все ошибки транслируются в коды GetWarningType и флаги.

Отсутствующий параметр = пустое значение: чтение настроек печати из реестра (ключи FromName, FromAddr, FromIndex, ToName, ToAddr, ToIndex) выполняется по образцу отказоустойчивого скрипта — отсутствие ключа возвращает пустую строку, а не исключение.

Слепой запуск: автоматическая печать обязана работать без единого клика оператора — без модальных окон и ожидания ввода; при флаге ShowFormsAsModal = false окна не блокируют вызывающий поток.

Активация окна: перед печатью из внешнего процесса сервер вызывает OpenDummyFormToSetAsForeground (перехват фокуса вспомогательным окном), после — CloseDummyFormAndSetAsForeground возвращает фокус вызывающему; иначе автопечать уходит в невидимый фон.

Single-instance: повторный запуск сервера активирует уже работающий экземпляр, а не поднимает вторую копию БД.

DPI-осведомленность: сервер обязан выставлять DPI-aware для корректного предпросмотра на современных мониторах.

Печать как факт учета: PrintEnvelopeAndCreateOutcoming и PrintNotificationAndCreateOutcoming внутреннего слоя одновременно создают запись об отправлении — печать становится фактом учета, а не только выводом.

9.3. Версионирование контракта автоматизации
Две независимые оси версий: версия продукта (строка) и версия автоматизации (целое, GetAPIVersion, сквозная нумерация). Правило совместимости: новые методы только добавляются, DISPID существующих не меняются, сигнатуры не изменяются; контракт меняется реже продукта. Интегратор вызывает GetAPIVersion() и по своему коду решает, какие методы доступны.

Историческая матрица соответствия (для планирования собственной нумерации; фактические версии новой программы нумеруются с 1):

API v0 — базовая печать из файла/XML для четырех семейств документов;

API v1 — служебные методы: списки шаблонов, печать без формы, работа с БД, флаги, версии, активация окон;

API v2 — расширение XML-конвертов и реестров полями Phone, Number, Note;

API v3 — импорт контрагентов, создание писем из файла/XML, флаг обновления контрагентов.

9.4. Форматы обмена (4 XML-контракта)
Полные схемы — см. EVIDENCE/automation_data_formats.txt. Поддерживаются на импорт и экспорт:

EnvelopesData / EnvelopeData → From(Name, Address, ZipCode, Phone) / To(Name, Address, ZipCode, Phone) / Number, Note — печать конвертов и уведомлений;

RegistersData / RegisterData → Number, From, Packages → Package(Number, To, Note) — реестры;

InventoriesData / InventoryData → From(Name), To(Name, Address), InventoryItems → InventoryItem(ItemName) — описи;

EconomicMembers → LegalPerson(Name, ShortName, Note, Email, Address(ZipCode, Province, City, AddressLine)) / NaturalPerson(LastName, MiddleName, FirstName, Note, Email, Address) — импорт контрагентов.

Требования к сериализации: каждая сущность домена обязана уметь WriteToXml / ReadListFromXml / WriteListToXml (сериализация — часть контракта, а не «экспорт на стороне»); запись потоковая, в три фазы (открыть элемент → текст → закрыть), без построения полного DOM для больших партий; входной недоверенный XML проходит санитайзер и вариант чтения «и починить для печати» (усечение, нормализация индексов, выкусывание индекса из адреса под флагом); вложенные zip-пакеты (бланки, клипарт) могут передаваться внутри XML и извлекаются штатным средством.

9.5. Клиент-серверный режим
На сервере: СУБД (PostgreSQL), папка БД, папка-эталон шаблонов. На клиенте: копия программы, копия папки шаблонов (после правки шаблона на одном рабочем месте шаблоны необходимо копировать на остальные — файловая природа шаблонов требует ручной/скриптовой синхронизации, это явно описывается в справке).

Выбор сервера: список серверов в отдельном справочнике servers.ini; выбор «этот компьютер / IP-адрес / имя сервера»; логин и пароль у клиента не настраиваются — единые реквизиты хранятся в защищенном файле подключения.

Поведение при недоступности: периодические повторы соединения; режим «только чтение» не предусматривается — операции записи блокируются с понятным сообщением; проверка сети и брандмауэра — подсказка в сообщении (порт по умолчанию 5432).

Ограничения: имя БД без кириллицы; имя БД можно менять только до первого подключения; перенос программы = копирование всей папки целиком.

9.6. E-mail как канал интеграции
SMTP-рассылка (Функции 92–100): сервер/порт/логин/пароль, SSL (STARTTLS/ImplicitTls), опциональный прокси с авторизацией, простой текст или HTML (встроенный WYSIWYG-редактор, шаблон письма и стили — файлы рядом с программой), вложения с копированием в хранилище отправителя, отдельные From и ReplyTo. Пароли SMTP и прокси хранятся исключительно в DPAPI — хранение открытым текстом запрещено (известный дефект предшественников, не воспроизводится). Библиотека HTML-редактора поставляется по LGPL: файл лицензии обязан присутствовать в поставке рядом с редактором.

9.7. Импорт из 1С и редактор сопоставления полей
Поддерживаемые версии выгрузек: 1С 7.7 и 8.x (XML и текстовые форматы справочника «Контрагенты»), а также Excel/XML/CSV и выгрузки в формате EconomicMembers через COM.

Обязательный порядок импорта: (1) подготовка файла — поля должны быть разделены; если поля «слиты» без единых разделителей, программа обязана сообщить о невозможности корректной загрузки до начала; (2) экран сопоставления полей файла полям БД (по единому словарю §3.2, например ind_to ↔ колонка №2); (3) интерактивное сопоставление с сохранением профиля для повторного импорта; (4) ограничение длины полей 255 символов с предупреждением об усечении; (5) фолбэк XLSX → CSV/HTML с понятным сообщением при несовместимой версии файла.

Политики добавления: «только проверенные записи» / «только записи без ошибок (проблемные — в CSV-лог)» / «все с усечением». Отчет о проверке импорта обязателен: число добавленных/обновленных/пропущенных/ошибочных; источник данных каждого контрагента фиксируется в карточке.

9.8. Миграция с существующих систем
Скрипты-импортёры двух схем-предшественниц (Функции 117–118): MySQL-схема из 8 таблиц (см. EVIDENCE/schema_reference.sql) и файловая схема из 14 таблиц (см. EVIDENCE/database_schema_notes.txt). Переносится: адресная книга с реквизитами и пользовательскими полями, группы, история печати (как неизменяемые снимки), кэш QR, описи, переносимые ключи настроек. Не переносится: подключение к серверу прежней системы, внутренние форматы отчетов прежней системы (заменяются пакетами .lbk новой поставки).

10. Инфраструктура и эксплуатация
10.1. Стек и версии
ОС: Windows 10/11 x64. Платформа: .NET 8 (Windows), C# 12. UI: WinForms (привычность для операторов, плотные гриды) — WPF допускается по результату прототипа таблиц на 100 000+ строк. БД: PostgreSQL 16 (основная, клиент-сервер) и SQLite 3 (локальный/переносимый режим), доступ через EF Core + Dapper, явные миграции с таблицей schema_version. Печать: слой поверх System.Drawing.Printing / Win32 (DocumentPropertiesW, DeviceCapabilitiesW, winspool.drv); собственный драйвер принтера не пишется; собственный рендер PDF не пишется — используется компонент генерации PDF 1.4 с фильтрами RLE/LZW/JPEG/G3FAX/G4FAX и метаданными (Title/Author/Subject/Keywords/Creator/Producer). Растровые экспорты: PNG и TIFF (CCITT G3/G4, LZW, JPEG, PACKBITS, DEFLATE, ZIP; EXIF-ориентация).

10.2. СУБД и миграции
Единая схема для PostgreSQL и SQLite (диалектные отличия SQL — в слое запросов, см. §4). Миграции forward-only, встроенные в сборку Infrastructure, применяются автоматически при старте; каждая миграция атомарна и фиксируется в schema_version. Кодировки: Unicode/UTF-8 везде; импорт legacy с автоопределением cp1251/UTF-8/UTF-16; конвертация при экспорте.

10.3. Логирование и каналы
Структурированный лог (Serilog) с ротацией по размеру и дню; независимые каналы: print (все печатные сессии в формате §6.5, включая отклоненные), errors (исключения приложения, Warning+), audit (изменения справочников), email (отправки с кодами SMTP). Расположение: <Data>\logs\. Сбой файловой системы логов не должен ронять приложение (деградация в память).

10.4. Автоматическое архивирование
Расписание: до трех точек в сутки (по умолчанию 09:00 / 13:05 / 16:05). Содержимое: полный образ папки БД (или файла SQLite) в ZIP, совместимом со «Сжатыми папками» Windows; имя Base_ГГММДДЧЧММ.zip. Пароль архива — опционально, с кнопкой показа. Хранение: два режима — «за каждый день» (с автоудалением старше N дней) и «только последняя копия» (предыдущая удаляется только после успешного создания новой). Целевая папка — любая папка Windows, включая сетевые и подключённые облачные (отдельной облачной интеграции нет). Вход: Сервис → Архивация базы адресов; бэкап и восстановление — одна форма.

10.5. Восстановление
Из той же формы: выбор архива, пароль, предпросмотр (дата, версия схемы, объем), автоматическая страховочная копия текущего состояния, атомарная замена, предложение перезапуска. Архив, созданный на другой машине, обязан разворачиваться на чистой установке без ручных правок.

10.6. Установка
Установщик (MSI/Inno-подобный) не требует отключения Защитника Windows и не просит прав администратора для локального режима (регистрация COM и меню Проводника — в HKCU). Переносимость: установка на USB-флешку в двух вариантах (полная / только БД). Мастер первого запуска: выбор чистой/демо-базы, настройка архивирования (рекомендация сразу после установки), настройка подключения.

10.7. Лицензирование
Модель: три редакции (Free / Home / Professional) с разными лимитами; пробный период с ограничением срока (по умолчанию 10 дней) и объема (число контрагентов/документов); состояние лицензии — один автомат, вычисляемый при старте (Trial / Licensed / Expired), все проверки UI читают его. Привязка к оборудованию: MachineID несколькими вариантами (серийный номер тома / UUID платы / составной отпечаток), значение — в реестре HKCU. Активация: онлайн (ключ + MachineID → подписанный ответ с локальной проверкой подписи) и офлайн-цепочка с файлом запроса строгого имени На_регистрацию_ПК!{uuid}_{date}-{n}-{n}. Данные лицензии шифруются (AES-256; ключ производен от MachineID и не хранится рядом). Деградация корректная: истечение/снятие ключа не удаляет данные; чтение доступно, создание и печать партий блокируются с сообщением и числом оставшихся дней; окно преимуществ редакции показывает список получаемых возможностей.

ЯВНЫЙ ЗАПРЕТ (архитектурное требование): не реализовывать агрессивную самозащиту процесса — перехват отладчика, анти-мониторинговые хуки, обращение к портам ввода-вывода для детекции отладки/виртуализации, любые приёмы, конфликтующие с корпоративными EDR/антивирусами. Защита ограничивается подписью ключа, шифрованием данных лицензии, привязкой к железу и проверкой целостности собственных сборок при старте. Детекция виртуальной машины — отдельная мягкая проверка (предупреждение/демо-режим, не блокировка).

10.8. Локализация
Русский — основной; английский — вторая очередь без переписывания форм. Интерфейсные строки и словари числительных/валют — файлы (lang/<код>.xml, lng/<валюта>.lng), набор расширяем пользователем; переключение языка без перезапуска. Словарь суммы прописью (ключи One, Ten, Twen, Hndr, Thou, Mill, Bill, Thrill, Currency, Cop, Comma, CurrencyMale, Month1, Month2, DayOfWeek — состав по EVIDENCE/localization_dictionaries.txt); поставляются Россия/Беларусь (новая и старая)/Румыния/Украина/доллар США; Казахстан — пустой шаблон (заполнение — открытым вопросом §13).

11. Тестируемость
11.1. Уровни тестов
Юнит-тест: изолированная проверка доменных правил и чистых функций (валидация ИНН/индекса, автоподбор кегля на измеренной метрике шрифта, склонение числительных, AABB-пересечения, sanitize XML, генерация номеров). Без БД, без принтера, без UI.

Интеграционный тест: связка «слой данных + слой печати» на встроенной SQLite в памяти и мок-принтере (DEVMODE-профиль фиксирован): выборка писем → рендер метафайла → сверка содержимого страниц; конвейер импорта CSV/XLSX; генерация XLSX/DOCX; COM-сервер в out-of-proc тесте.

Приёмочный тест: скриптовый прогон на физическом стенде трех принтеров (A4-офисный, лотковый конвертный, термопринтер) с физической сверкой совпадения ±1 мм; сценарии мастера импорта; восстановление архива на чистой машине.

11.2. Мок-объекты
IPrinterDevice / DEVMODE-мок: детерминированный набор «бумаг» и сообщаемых размеров, включая преднамеренное расхождение metrics.PaperSize и драйверного PaperSize (проверка срабатывания BLOCKED_SIZE и режима «Зафиксировать A4»);

БД-мок: SQLite in-memory с сеяными данными демо-базы;

COM-мок: локальный заглушечный сервер автоматизации для проверки вызывающего кода внешних интеграций;

Трекер Почты России-мок: HTTP-заглушка с фиксированными ответами партии/вручений.

11.3. Таблица «функция → способ проверки» (без дублей; пофункциональные критерии — в §8)

Диапазон функций
Область
Способ проверки

1–19
Адресная книга
Юнит (валидация полей, адрес 6 строк) + интеграционные тесты БД (пагинация 100k, фильтры) + UI-автотест карточки

20–35
Письма и рассылки
Интеграционные: жизненный цикл статусов, нумерация без коллизий при параллельных вызовах

36–49
Бланки
Юнит сериализации XML/ZIP (round-trip .lbk), интеграционный прогон 8 встроенных бланков на мок-принтере

50–64
Реестры и отчеты
Интеграционные: сверка итогов Ф.103/ф.107 с тестовыми данными; XLSX/DOCX открываются без Office (валидация OpenXML SDK)

65–77
Шаблоны и экспорт
Юнит XSLT-трансформаций; интеграционный импорт эталонных выгрузок 1С

78–91
Печать
Приёмочные на стенде 3 принтеров ±1 мм; юнит валидатора геометрии на мок-DEVMODE; журнал print обязателен в каждом прогоне

92–100
E-mail
Интеграционные против локального SMTP-сервера ( Papercut-подобный), включая SSL и прокси

101–112
Администрирование
Интеграционные: два клиента к одному серверу PostgreSQL; восстановление архива на чистой машине

113–118
Автоматизация
Интеграционные: out-of-proc COM-тесты 23 методов, включая коды ошибок 1–4 и слепую печать

119–126
Лицензирование
Юнит автомата состояний; интеграционный тест офлайн-активации с подменой времени

127–151
Операционные функции
Интеграционные: расписание бэкапа с ускоренным таймером; диагностика службы; поиск/подбор значений — UI-автотесты

152–163
Почтовые приложения
UI-автотесты вкладок/мастеров; приёмочный сценарий «демо-база → рассылка → пробная печать → партия → отчет»

11.4. Критерий «приёмка закрыта»
Все 11 критериев §7.2 заказа подтверждены протоколами стендовых прогонов; покрытие §8 — все 163 функции имеют статусы проверки по способам из §11.3; журнал печати содержит записи обо всех тестовых прогонах (включая отклоненные); экспорт/импорт проходят на эталонных файлах; офлайн-активация и восстановление архива воспроизведены на чистой виртуальной машине.

12. План реализации по этапам
Принцип: риск-первым. Самое дорогое и непредсказуемое — печать конверта; этапы идут по рискам, не по функциям. Каждый этап заканчивается работающим вертикальным срезом.

Этап 0. Прототип печати (2–3 недели)
Состав: функции 78, 79, 80, 81, 119 (ограниченно: trial-каркас). Пустой конверт C6, одна страница, поле индекса шрифтом ZIPcode, сверка metrics.PaperSize против принтера, блокировка при несовпадении, «Зафиксировать A4», предпросмотр, печать одного конверта, запись PrintJob.
Критерий выхода: конверт на трех разных принтерах (A4, лотковый конвертный, термопринтер) совпадает с предпросмотром до ±1 мм; предупреждение о несовпадении срабатывает; журнал полон. Артефакт — тест-лист трех принтеров; без него дальше не идти.
Риски: расхождение драйверных метрик → мера: двойная проверка §6.2 обязательна; отсутствие шрифта ZIPcode → мера: поставка шрифта в установщике (лицензия — §13).

Этап 1. Ядро данных и адресная книга (3–4 недели)
Состав: функции 1, 3, 4, 5, 10, 11, 12, 17, 18, 20, 22, 25, 71, 72, 73, 74, 75, 77, 137, 138, 139, 148, 152, 153, 154, 157, 159, 160. Схема БД по §3, словарь имен полей (первым делом), главное окно «3+2», демо-база, импорт/экспорт, разбор индекса.
Зависимости: после Этапа 0 (окно предпросмотра уже существует).
Критерий выхода: 100 000 контрагентов листаются без просадки (память ≤ 150 МБ на страницу); импорт выгрузки 1С через маппинг без ручной правки; ширина колонок переживает перезапуск.
Риски: расхождение словаря имен полей → мера: единый генератор словаря для БД/шаблонов/импорта из одного исходника.

Этап 2. Шаблоны, бланки и печать (4–5 недели)
Состав: функции 23, 24, 26, 27, 28, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 143, 144, 145, 146, 147, 149, 158. Формат .lbk, редактор бланков, библиотека 8 встроенных + конверты, пресеты разметки, полный контур печати §6, журнал печати.
Зависимости: жестко требует завершенного Этапа 0 и схемы Этапа 1.
Критерий выхода: пользователь без программиста создает бланк и печатает партию; предупреждения формата не дают выпустить брак; каждая печать в журнале.
Риски: партии конвертов с полей смещены на 1–2 мм → мера: пробная печать Функции 143 обязательна перед партией.

Этап 3. Группы, реестры, отчеты (3–4 недели)
Состав: функции 21, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 112, 140, 141, 142, 150, 151, 155, 156. Ф.103/ф.107/журналы, слой запросов §4, XSLT-экспорт без Office, drag&drop рассылок, языки, сумма прописью и локализация словарями.
Зависимости: схема Этапа 1, слой печати Этапа 2.
Критерий выхода: Ф.103 соответствует требованиям; экспорт Word/Excel на машине без Office; массовое редактирование откатываемо.
Риски: расхождение проекций отчетов с бланками → мера: проекции §4 закреплены словарем полей.

Этап 4. Периферия и эксплуатация (3–4 недели)
Состав: функции 2, 6, 7, 8, 9, 13, 14, 15, 16, 19, 76, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 120, 121, 123, 124, 125, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 161, 162, 163. Клиент-сервер, архивы (2 режима), настройка таблиц, три механизма поиска, лицензия (без антиотладки), трекер, обновления.
Зависимости: схема Этапа 1.
Критерий выхода: два рабочих места на одной БД; архив восстанавливается на чистой машине; поиск работает тремя способами.
Риски: конфликты служб СУБД → мера: диагностический доктор Функции 136.

Этап 5. Автоматизация и e-mail (2–3 недели, опционально по срокам, не по важности)
Состав: функции 92, 93, 94, 95, 96, 97, 98, 99, 100, 113, 114, 115, 116, 117, 118, 122, 126. COM-мост 23 методов, SMTP+SSL+прокси с паролями в DPAPI, миграция двух схем-предшественниц.
Зависимости: XML-модель Этапа 2 (иначе COM переписывать), схема Этапа 1.
Критерий выхода: сценарий «из 1С → письмо → конверт → учет отправления» без участия оператора; пароль SMTP не в открытом виде.

Этап 6. Сканирование и входящие (2–3 недели, опционально)
Состав: функция 29 (и операционный контур 30). TWAIN и WIA, явный выбор устройства, прогресс и отмена, EXIF-ориентация (90/180/270), привязка скана к входящему документу. Явно не делается: deskew и автосклейка многолистовых сканов — их не было ни в одной исходной системе, это отдельный проект.
Критерий выхода: скан выбран явно, ориентация исправлена, скан привязан к документу и открывается из карточки.

Карта зависимостей: Этап 0 → Этап 2; Этап 1 → Этапы 3, 4; Этап 2 (XML-модель) → Этап 5; Этап 4 → Этап 6.

13. Открытые вопросы и допущения
Каждый пункт: вопрос → выбранное допущение → влияние на реализацию.

Толщина линии кодового штампа: в эталонных шаблонах-предшественниках встречается 0.625 мм при норме ГОСТ 0.2–0.5 мм. Допущение: значение по умолчанию 0.45 мм (внутри нормы), при выходе за норму — предупреждение, не блокировка. Влияние: валидатор §6.3 и свойство поля.

Лицензия на распространение шрифта ZIPcode.ttf: условия redistribution неизвестны из материалов. Допущение: до включения в поставку проверить лицензию; при невозможности — собственный генератор глифов OCR-B с теми же метриками. Влияние: состав установщика, этап 0.

Словарь валют Казахстана пуст в исходных материалах. Допущение: поставляется пустой шаблон .lng; заполнение контента вынесено за рамки разработки. Влияние: Функции 150–151, поставка lang-файлов.

Срок и лимиты пробного периода и пороги редакций Free/Home/Professional: конкретные числа в материалах не заданы (у предшественников встречался срок 10 дней). Допущение: trial 10 дней, лимиты редакций выносятся в конфигурацию и утверждаются заказчиком до Этапа 4. Влияние: конфигурируемость LicensedLimits, а не константы.

URL сервера активации, URL обновлений, имя утилиты удалённого доступа: не определены. Допущение: адреса — настройки администратора с плейсхолдерами по умолчанию. Влияние: Функции 109, 122, 163.

Формат обмена с трекером Почты России (API-эндпоинты, схема токена): в материалах описаны поля, но не протокол. Допущение: реализуется адаптер с конфигурируемым базовым URL и версией API; при изменении API меняется только адаптер. Влияние: Функция 110.

Число копий при печати «всех выделенных» и порядок обхода выборки: порядок не нормирован. Допущение: порядок по возрастанию id (стабильный, совпадает с реестром). Влияние: Функция 79.

Пароль к архиву БД: восстановление при утере пароля невозможно по построению. Допущение: предупреждение о невозстановимости в диалоге; поле пароля опционально. Влияние: Функции 103, 104.

Совместимость со старыми файлами шаблонов-предшественников (.fr3): требуется ли конвертер — бизнес-решение. Допущение: конвертер не входит в объём; перенос — через экспорт данных и новые пакеты .lbk; решение пересматривается до Этапа 2. Влияние: состав Этапа 2 (риск из §9.6 master-документа).

Разметка реальных разлинованных конвертов заказчика: партии различаются. Допущение: поставляются пресеты по стандарту + обязательная пробная печать; сбор образцов партий — на стороне заказчика. Влияние: Функции 89, 143.

Порог подтверждения группового редактирования (>N строк): число не задано. Допущение: N = 10, конфигурируется. Влияние: Функция 18.

Кодировка файлов обмена по умолчанию (CSV): материалы фиксируют UTF-8 и cp1251 как встречающиеся. Допущение: автоопределение BOM/статистикой с ручным выбором в мастере. Влияние: Функции 71–73.

14. Приложения
Приложение A. Схема БД (сводный DDL PostgreSQL)
Полный DDL хранится миграциями (§10.2); ниже — сводка таблиц и ключевых ограничений. Соответствие словарю имен — §3.2; сверка с EVIDENCE/schema_reference.sql выполнена (колонки-контракты ind_to, str_to1..6, prim, copyes, vip сохранены как имена проекций слоя запросов).

counterparties — §3.1.1; counterparty_fields — §3.1.9; groups — §3.1.2; mails — §3.1.3; folders — §3.1.11; distributions — §3.1.10; print_templates — §3.1.4; blank_fields — §3.1.5; print_jobs — §3.1.6 (неизменяемый журнал); print_history — §3.1.7 (снимки адресов); qr_cache — §3.1.8; inventories / inventory_items — §3.1.12; post_registers — §3.1.13; document_movements — §3.1.14; mail_documents — §3.1.15; printer_profiles — §3.1.16; parameters — §3.1.17; schema_version — §3.1.18.

Ключевые ограничения (перечень): counterparties.group_id → groups(id) ON DELETE SET NULL; mails.counterparty_id/sender_id → counterparties(id); mails.distribution_id → distributions(id) ON DELETE SET NULL; mails.inventory_id → inventories(id); mails.post_register_id → post_registers(id); blank_fields.template_id → print_templates(id) ON DELETE CASCADE; print_history.print_job_id → print_jobs(id) ON DELETE CASCADE; print_history.mail_id → mails(id) ON DELETE SET NULL; inventory_items.inventory_id → inventories(id) ON DELETE CASCADE; UNIQUE(counterparty_id, field_name) в counterparty_fields; UNIQUE(payload_hash) в qr_cache; UNIQUE(printer_name, envelope_format) в printer_profiles; ровно одна groups.is_default = TRUE.

Приложение B. Примеры XML-контрактов обмена
B.1. Печать конвертов (вход):

<EnvelopesData>
  <EnvelopeData>
    <From>
      <Name>ООО «Техно»</Name>
      <Address>г. Минск, ул. Ленина, 5, оф. 210</Address>
      <ZipCode>220050</ZipCode>
      <Phone>+375 17 000-00-00</Phone>
    </From>
    <To>
      <Name>ПАО «МосБанк»</Name>
      <Address>г. Москва, ул. Тверская, 12, корп. 2</Address>
      <ZipCode>101000</ZipCode>
    </To>
    <Number>ИСХ-00042</Number>
    <Note>Срочное уведомление о закрытии лицевого счета</Note>
  </EnvelopeData>
</EnvelopesData>

B.2. Реестр (вход, фрагмент):

<RegistersData>
  <RegisterData>
    <Number>Р-000017</Number>
    <From><Name>ООО «Техно»</Name><ZipCode>220050</ZipCode></From>
    <Packages>
      <Package>
        <Number>ИСХ-00042</Number>
        <To><Name>ПАО «МосБанк»</Name><Address>г. Москва, ул. Тверская, 12</Address><ZipCode>101000</ZipCode></To>
        <Note>Заказное с уведомлением</Note>
      </Package>
    </Packages>
  </RegisterData>
</RegistersData>

B.3. Импорт контрагентов (вход, фрагмент):

<EconomicMembers>
  <LegalPerson>
    <Name>ПАО «МосБанк»</Name><ShortName>МосБанк</ShortName>
    <Email>office@mosbank.example</Email>
    <Address><ZipCode>101000</ZipCode><Province>Москва</Province><City>г. Москва</City><AddressLine>ул. Тверская, 12, корп. 2</AddressLine></Address>
  </LegalPerson>
  <NaturalPerson>
    <LastName>Иванов</LastName><MiddleName>Иванович</MiddleName><FirstName>Иван</FirstName>
    <Address><ZipCode>350000</ZipCode><Province>Краснодарский край</Province><City>г. Краснодар</City><AddressLine>ул. Красная, д. 35, кв. 4</AddressLine></Address>
  </NaturalPerson>
</EconomicMembers>

Приложение C. Пример файла бланка и запроса
C.1. Структура пакета .lbk и blank.xml — §5.1. C.2. Формат query.xml и правила редактирования — §4.1. C.3. Физические параметры поля индекса — §6.3. Полные словари полей встроенных бланков — по EVIDENCE/blank_field_vocabulary.txt и EVIDENCE/report_field_projections.txt.

Приложение D. Карта функций (идентификатор → модуль → файл)
Ключ: D = src/Domain, A = src/Application, I = src/Infrastructure, P = src/Printing, U = src/UI, C = src/ComAutomation, L = src/Licensing.

Функции
Модули (слой)
Файлы реализации

1–7, 15–19
D.Entities/ValueObjects + A.Services + U.Forms
D/Entities/Counterparty.cs, D/ValueObjects/Address.cs, A/Services/{AddressParserService, RussianDeclensionEngine→I/Text, CustomFieldManager, CascadeAddressUpdater, BatchEditService, CounterpartyManager}.cs, U/Forms/FormCounterpartyEdit.cs

8–14
D.Entities + A.Services + I.Integration + U.Components
D/Entities/Group.cs, A/{Queries/CounterpartyFilterQuery, Services/GroupManager}.cs, I/{Persistence/VirtualDataBuffer, Integration/RegistryLookupClient, Integration/ExternalDatabaseLookup}.cs, U/Components/GroupableGrid.cs

20–35
D.Entities + A.Services + P.Engine + U.Forms
D/Entities/{Mail, MailFolder, Inventory, InventoryItem}.cs, A/Services/{MailFactory, DistributionService, RegistrationNumberGenerator, IncomingMailService, DocumentMovementTracker, DocumentBatchGenerator, RecentParametersStorage}.cs, P/{Elements/ServiceStampRenderer, Reports/RegistryF103Builder, Engine/TextFitter}.cs

36–49
I.Templates + P.Engine + D.Serialization + U.Forms
I/Templates/{BuiltinTemplatesCatalog, TemplatePackageRepository, IO/LbkPackageArchiver, SpecialFormsBuilder}.cs, P/{Engine/{EnvelopeInverter, SheetLayoutImposition, FeedAlignmentCalculator}, Elements/{PostIndexElement, LogoRenderer, QrCodeRenderer}}.cs, D/Serialization/BlankXmlSerializer.cs, U/Forms/FormBlankEditor.cs

50–64
P.Reports + A.Services
P/Reports/{Form103Report, ExtendedRegistryReport, SimpleRegistryReport, InventoryF107Report, IncomingJournalReport, OneCIncomingReport, AuxiliaryPostReports, StatsAndGroupReports}.cs, A/Services/{CustomReportManager, ReportDataDumper, RegistrySorter, SilentPrintService, HistoryTracker}.cs, A/Formatting/DocumentNumbersAggregator.cs

65–77
A.Services + I.Templating + I.Export + I.Migration
A/Services/{ArbitraryDocumentEngine, UniversalAddressExchange, OneCImportParser, ImportMergeService, ImportDeduplicator}.cs, I/{Templating/XsltTransformEngine, Export/{WordprocessingMlExporter, SpreadsheetMlExporter, NativeOpenXmlPackager}, Integration/ExternalDbImporter}.cs, D/Exchange/EnvelopesDataSerializer.cs, U/Forms/{FormDocTemplateWizard, FormCsvImportConfig}.cs

78–91
P.Devices + P.Validation + P.Engine + P.Audit + U.Forms
P/Devices/PrinterManager.cs, P/PrintSessionController.cs, P/Preview/PrintPreviewEngine.cs, P/Validation/PaperSizeValidator.cs, P/Engine/{FixA4Mode, DuplexController, PageParityFilter, PreprintedMode}.cs, P/Elements/PostIndexRenderer.cs, I/Fonts/ZipcodeFontGuard.cs, P/Audit/PrintAuditWriter.cs, U/Forms/FormEnvelopePrint.cs

92–100
A.Services + I.Mail + U.Editors
A/Services/{EmailDistributionService, AttachmentService}.cs, I/Mail/{SmtpOptions, SmtpClientFacade, ProxySettings}.cs, U/Editors/HtmlMailEditor.cs

101–112
I.Persistence + I.Backup + I.Deployment + I.Integration + I.Logging + I.Localization + U
I/Persistence/{ServerRegistry, ConnectionManager, DbServiceDoctor}.cs, I/Backup/{ScheduledBackupService, RestoreService, BackupPolicyService}.cs, I/Deployment/UsbInstaller.cs, I/Shell/ExplorerIntegration.cs, I/Migration/LegacyUpgradeImporter.cs, I/Integration/{WebLookupLauncher, RussianPostTracker, RemoteAssistantLauncher}.cs, I/Logging/LogHub.cs, I/Localization/LanguageManager.cs

113–118
C + I.Migration
C/{PEProApplication, ComRegistration, ApiLettersFacade}.cs, I/Migration/{LegacyMySqlImporter, LegacyAccessImporter}.cs

119–126
L + UI
L/{LicenseStateMachine, MachineIdentity, VmDetector, ActivationService, LicenseManager, LicenseCrypto, Editions}.cs, U/Forms/FormRegistration.cs

127–151
I.Persistence + I.Backup + I.Deployment + I.Text + D.Postal + U.Components + A.Services + A.Setup
(расширения модулей диапазонов 101–112) + I/Text/AmountInWords.cs, D/Postal/EnvelopeFormatCatalog.cs, P/Templates/StandardLayoutPresets.cs, P/Labels/LabelSheetComposer.cs, P/Warnings/FeedOrientationHint.cs, A/Services/{AddressHeuristicsService, CategoryTreeService, AgentService}.cs, A/Setup/DemoDatabaseSeeder.cs, U/{Shell/MainShell.cs, Forms/FormMailingWizard.cs, Components/{GridAppearanceManager, SearchController, FieldAutoComplete}}.cs

152–163
U.Shell + A.Services + A.Setup + I.Update + U.Forms
U/Shell/MainShell.cs, A/Services/{DistributionService, ExportWizard, ImportValidator}.cs, A/Setup/DemoDatabaseSeeder.cs, I/Update/UpdateChecker.cs, U/Forms/{FormTemplateGallery, FormImportWizard, ServiceDialogs}.cs

Приложение E. Глоссарий
Бланк (.lbk) — ZIP-пакет печатного макета: blank.xml (координатное дерево полей), query.xml (запрос проекции данных), фоновые растры. — Видимое/печатное (Visible/Printable) — два независимых флага элемента бланка: скрыт везде / виден на экране, но не печатается. — Кодовый штамп — машиночитаемое представление почтового индекса шрифтом ZIPcode с нормативной геометрией (3.7×5.5 мм, шаг 5.4 мм, линия 0.2–0.5 мм, 6 знаков; «555» для Мн). — «Зафиксировать A4» — штатный режим печати конверта по координатам листа A4 при несоответствии лотка. — Журнал печати (print_jobs) — неизменяемый реестр всех печатных сессий, включая отклоненные. — История печати (print_history) — бизнес-уровень: снимки адресов каждого напечатанного конверта. — Слой запросов — механизм хранения SQL-проекции в файле бланка/отчета, а не в коде. — Демо-база — отдельная копия данных первого запуска для освоения. — Агент (отправитель) — запись адресной книги с флагом «Это отправитель». — Вн/Мн — территория хождения конверта: внутренняя / международная (влияет на кодовый штамп и порядок адреса). — Пробная печать — одиночная печать шаблона для сверки с партией конвертов перед пакетной печатью. — Тонкое слияние — политика импорта «дополнять пустые поля существующих записей». — DPAPI — Windows ProtectedData, обязательное хранилище паролей SMTP/прокси/БД.
