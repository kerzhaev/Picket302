-- Описание: таблицы совместного режима (серверная служба Picket302)
-- Совместный режим: один серверный ПК и до пяти клиентских рабочих мест
-- работают с одной общей базой через типизированный API службы.
--
-- Таблицы этого режима не используются в одиночном режиме: они пусты и не
-- влияют на работу программы, которая открывает базу напрямую. Единый поток
-- миграций выбран сознательно — у одиночного режима и у службы не должно
-- быть расходящихся версий схемы.

-- Маркеры версий общих сущностей. Отдельная таблица, а не колонка в строках
-- сущностей: существующие репозитории и одиночный режим остаются
-- неизменными, а проверка версии работает только там, где она нужна.
-- Сравнение и увеличение версии выполняются одной командой
-- (INSERT … ON CONFLICT … DO UPDATE … WHERE version = @Expected RETURNING …),
-- поэтому две одновременные правки не могут обе завершиться успешно.
CREATE TABLE IF NOT EXISTS shared_row_versions (
    entity_kind INTEGER NOT NULL,
    entity_id   INTEGER NOT NULL,
    version     INTEGER NOT NULL DEFAULT 1,
    updated_at  TEXT NOT NULL,
    PRIMARY KEY (entity_kind, entity_id)
);

-- Журнал выполненных операций: основа идемпотентности. Повтор команды с тем же
-- идентификатором возвращает прежний результат, а не создаёт дубль.
CREATE TABLE IF NOT EXISTS shared_operations (
    operation_id TEXT NOT NULL PRIMARY KEY,
    device_id    TEXT NOT NULL,
    command_name TEXT NOT NULL,
    request_hash TEXT NOT NULL,
    status       TEXT NOT NULL,
    result_json  TEXT NULL,
    error_json   TEXT NULL,
    created_at   TEXT NOT NULL,
    completed_at TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_shared_operations_device ON shared_operations (device_id, created_at);

-- Сопряжённые устройства-клиенты. На сервере хранится только проверяемый
-- отпечаток секрета устройства, а не сам секрет.
CREATE TABLE IF NOT EXISTS shared_devices (
    device_id      TEXT NOT NULL PRIMARY KEY,
    name           TEXT NOT NULL,
    secret_hash    TEXT NOT NULL,
    created_at     TEXT NOT NULL,
    last_seen_at   TEXT NULL,
    revoked_at     TEXT NULL,
    revoked_reason TEXT NULL
);

-- Одноразовые коды сопряжения. Код нельзя получить удалённым запросом:
-- таблица заполняется локальным экраном администратора. Хранится отпечаток
-- кода, а не сам код.
CREATE TABLE IF NOT EXISTS shared_pairings (
    pairing_id TEXT NOT NULL PRIMARY KEY,
    code_hash  TEXT NOT NULL,
    nonce      TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    max_uses   INTEGER NOT NULL DEFAULT 1,
    attempts   INTEGER NOT NULL DEFAULT 0,
    used_at    TEXT NULL,
    created_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_shared_pairings_nonce ON shared_pairings (nonce);

-- Управляемые пакеты шаблонов (.lbk): содержимое хранится на сервере
-- вместе с версией и SHA-256, клиент получает конкретную версию.
CREATE TABLE IF NOT EXISTS shared_template_packages (
    package_id   TEXT NOT NULL PRIMARY KEY,
    name         TEXT NOT NULL,
    version      INTEGER NOT NULL DEFAULT 1,
    content_hash TEXT NOT NULL,
    size_bytes   INTEGER NOT NULL DEFAULT 0,
    file_name    TEXT NOT NULL,
    is_builtin   INTEGER NOT NULL DEFAULT 0,
    updated_at   TEXT NOT NULL
);

-- Служебные сведения: идентичность сервера и номер порта.
CREATE TABLE IF NOT EXISTS shared_service_meta (
    name  TEXT NOT NULL PRIMARY KEY,
    value TEXT NOT NULL
);

-- Общая семантика печатных действий: исход печати (подготовлено, передано
-- в локальный спулер, ошибка, неизвестный исход после обрыва) и устройство,
-- выполнившее печать. Сервер никогда не печатает сам.
ALTER TABLE print_history ADD COLUMN outcome TEXT NULL;
ALTER TABLE print_history ADD COLUMN device_id TEXT NULL;

CREATE INDEX IF NOT EXISTS idx_print_history_device ON print_history (device_id);
