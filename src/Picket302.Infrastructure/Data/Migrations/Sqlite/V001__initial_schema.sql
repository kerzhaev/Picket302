-- Описание: Начальная схема базы данных: сущности ТЗ §3.1.1–3.1.18, индексы §3.1.19
-- Диалект: SQLite 3 (переносимый режим). Аналог для PostgreSQL 16 поставляется отдельно.
-- Миграция выполняется только вперед и фиксируется в таблице schema_version.

CREATE TABLE groups (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT NOT NULL UNIQUE,
    note        TEXT NULL,
    is_default  INTEGER NOT NULL DEFAULT 0 CHECK (is_default IN (0, 1)),
    is_closed   INTEGER NOT NULL DEFAULT 0 CHECK (is_closed IN (0, 1)),
    sort_order  INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE counterparties (
    id                   INTEGER PRIMARY KEY AUTOINCREMENT,
    type                 INTEGER NOT NULL CHECK (type IN (0, 1)),
    name                 TEXT NOT NULL,
    short_name           TEXT NULL,
    inn                  TEXT NULL,
    kpp                  TEXT NULL,
    ogrn                 TEXT NULL,
    okfs                 TEXT NULL,
    okpo                 TEXT NULL,
    okved                TEXT NULL,
    bik                  TEXT NULL,
    ks                   TEXT NULL,
    rs                   TEXT NULL,
    rkc                  TEXT NULL,
    bank_name            TEXT NULL,
    passport_series      TEXT NULL,
    passport_number      TEXT NULL,
    passport_issued_by   TEXT NULL,
    passport_issued_date TEXT NULL,
    birth_date           TEXT NULL,
    birth_place          TEXT NULL,
    position             TEXT NULL,
    director_fio         TEXT NULL,
    email                TEXT NULL,
    phone                TEXT NULL,
    special_notes        TEXT NULL,
    legal_zip            TEXT NULL,
    legal_str1           TEXT NULL,
    legal_str2           TEXT NULL,
    legal_str3           TEXT NULL,
    legal_str4           TEXT NULL,
    legal_str5           TEXT NULL,
    legal_str6           TEXT NULL,
    legal_raw            TEXT NULL,
    post_zip             TEXT NULL,
    post_str1            TEXT NULL,
    post_str2            TEXT NULL,
    post_str3            TEXT NULL,
    post_str4            TEXT NULL,
    post_str5            TEXT NULL,
    post_str6            TEXT NULL,
    post_raw             TEXT NULL,
    fact_zip             TEXT NULL,
    fact_str1            TEXT NULL,
    fact_str2            TEXT NULL,
    fact_str3            TEXT NULL,
    fact_str4            TEXT NULL,
    fact_str5            TEXT NULL,
    fact_str6            TEXT NULL,
    fact_raw             TEXT NULL,
    address_main_type    INTEGER NOT NULL DEFAULT 1 CHECK (address_main_type IN (0, 1, 2)),
    group_id             INTEGER NULL REFERENCES groups (id) ON DELETE SET NULL,
    is_vip               INTEGER NOT NULL DEFAULT 0 CHECK (is_vip IN (0, 1)),
    is_sender            INTEGER NOT NULL DEFAULT 0 CHECK (is_sender IN (0, 1)),
    is_active            INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
    is_deleted           INTEGER NOT NULL DEFAULT 0 CHECK (is_deleted IN (0, 1)),
    created_at           TEXT NOT NULL DEFAULT (strftime ('%Y-%m-%d %H:%M:%f', 'now')),
    updated_at           TEXT NOT NULL DEFAULT (strftime ('%Y-%m-%d %H:%M:%f', 'now'))
);

CREATE TABLE distributions (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    name               TEXT NOT NULL,
    sender_id          INTEGER NULL REFERENCES counterparties (id),
    kind               INTEGER NOT NULL DEFAULT 0 CHECK (kind IN (0, 1)),
    note               TEXT NULL,
    state              INTEGER NOT NULL DEFAULT 0 CHECK (state IN (0, 1, 2)),
    envelopes_required INTEGER NULL,
    created_at         TEXT NOT NULL DEFAULT (strftime ('%Y-%m-%d %H:%M:%f', 'now')),
    dispatched_at      TEXT NULL
);

CREATE TABLE inventories (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    name            TEXT NOT NULL,
    counterparty_id INTEGER NULL REFERENCES counterparties (id),
    destination     TEXT NULL,
    note            TEXT NULL,
    created_at      TEXT NOT NULL DEFAULT (strftime ('%Y-%m-%d %H:%M:%f', 'now'))
);

CREATE TABLE post_registers (
    id                      INTEGER PRIMARY KEY AUTOINCREMENT,
    register_number         TEXT NOT NULL,
    batch_number            TEXT NULL,
    sending_date            TEXT NULL,
    shipping_point          TEXT NULL,
    track_login             TEXT NULL,
    track_password_protected BLOB NULL,
    track_token             TEXT NULL,
    mail_ids                TEXT NULL,
    created_at              TEXT NOT NULL DEFAULT (strftime ('%Y-%m-%d %H:%M:%f', 'now'))
);

CREATE TABLE mails (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    counterparty_id    INTEGER NOT NULL REFERENCES counterparties (id),
    sender_id          INTEGER NOT NULL REFERENCES counterparties (id),
    distribution_id    INTEGER NULL REFERENCES distributions (id) ON DELETE SET NULL,
    envelope_type      TEXT NULL,
    notice_type        TEXT NULL,
    copy_count         INTEGER NOT NULL DEFAULT 1 CHECK (copy_count >= 1),
    is_vip             INTEGER NOT NULL DEFAULT 0 CHECK (is_vip IN (0, 1)),
    note               TEXT NULL,
    declared_value     NUMERIC NOT NULL DEFAULT 0.00,
    weight             NUMERIC NOT NULL DEFAULT 0.000,
    fee                NUMERIC NOT NULL DEFAULT 0.00,
    status             INTEGER NOT NULL DEFAULT 0 CHECK (status IN (0, 1, 2, 3, 4)),
    need_notification  INTEGER NOT NULL DEFAULT 0 CHECK (need_notification IN (0, 1)),
    need_return_address INTEGER NOT NULL DEFAULT 1 CHECK (need_return_address IN (0, 1)),
    mail_category      INTEGER NOT NULL DEFAULT 0 CHECK (mail_category IN (0, 1, 2, 3)),
    date_created       TEXT NOT NULL DEFAULT (strftime ('%Y-%m-%d %H:%M:%f', 'now')),
    date_printed       TEXT NULL,
    date_dispatched    TEXT NULL,
    date_confirmation  TEXT NULL,
    date_canceled      TEXT NULL,
    incoming_number    TEXT NULL,
    outgoing_number    TEXT NULL,
    post_register_id   INTEGER NULL REFERENCES post_registers (id),
    russian_post_id    TEXT NULL,
    russian_post_batch TEXT NULL,
    inventory_id       INTEGER NULL REFERENCES inventories (id),
    to_zip             TEXT NOT NULL,
    to_str1            TEXT NULL,
    to_str2            TEXT NULL,
    to_str3            TEXT NULL,
    to_str4            TEXT NULL,
    to_str5            TEXT NULL,
    to_str6            TEXT NULL
);

CREATE TABLE folders (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT NOT NULL,
    parent_id   INTEGER NULL REFERENCES folders (id),
    note        TEXT NULL,
    sort_order  INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE print_templates (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    name         TEXT NOT NULL,
    kind         TEXT NOT NULL CHECK (kind IN ('envelope', 'blank', 'report', 'label', 'notification')),
    format       TEXT NOT NULL,
    width_mm     NUMERIC NOT NULL,
    height_mm    NUMERIC NOT NULL,
    orientation  INTEGER NOT NULL DEFAULT 0 CHECK (orientation IN (0, 1)),
    file_path    TEXT NOT NULL,
    is_builtin   INTEGER NOT NULL DEFAULT 0 CHECK (is_builtin IN (0, 1)),
    country      TEXT NOT NULL DEFAULT 'RUS'
);

CREATE TABLE blank_fields (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    template_id        INTEGER NOT NULL REFERENCES print_templates (id) ON DELETE CASCADE,
    name               TEXT NOT NULL,
    kind               TEXT NOT NULL CHECK (kind IN ('text', 'post_index', 'qr', 'picture', 'barcode')),
    page               INTEGER NOT NULL DEFAULT 1,
    x_mm               NUMERIC NOT NULL,
    y_mm               NUMERIC NOT NULL,
    length_mm          NUMERIC NOT NULL,
    height_mm          NUMERIC NOT NULL,
    font_name          TEXT NOT NULL DEFAULT 'Tahoma',
    font_height_pt     NUMERIC NOT NULL DEFAULT 10.0,
    font_height_min_pt NUMERIC NOT NULL DEFAULT 6.0,
    font_height_max_pt NUMERIC NOT NULL DEFAULT 14.0,
    is_bold            INTEGER NOT NULL DEFAULT 0 CHECK (is_bold IN (0, 1)),
    is_italic          INTEGER NOT NULL DEFAULT 0 CHECK (is_italic IN (0, 1)),
    color_rgb          INTEGER NOT NULL DEFAULT 0,
    align              INTEGER NOT NULL DEFAULT 0 CHECK (align IN (0, 1, 2, 3)),
    max_chars          INTEGER NOT NULL DEFAULT 255,
    is_visible         INTEGER NOT NULL DEFAULT 1 CHECK (is_visible IN (0, 1)),
    is_printable       INTEGER NOT NULL DEFAULT 1 CHECK (is_printable IN (0, 1)),
    zone_role          TEXT NULL
);

CREATE TABLE print_jobs (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at        TEXT NOT NULL DEFAULT (strftime ('%Y-%m-%d %H:%M:%f', 'now')),
    template_id       INTEGER NULL REFERENCES print_templates (id),
    printer_name      TEXT NOT NULL,
    paper_size_name   TEXT NOT NULL,
    paper_width_mm    NUMERIC NOT NULL,
    paper_height_mm   NUMERIC NOT NULL,
    font_info         TEXT NULL,
    copies            INTEGER NOT NULL,
    print_mode        TEXT NOT NULL CHECK (print_mode IN ('selected', 'batch_preview', 'batch_direct')),
    status            TEXT NOT NULL CHECK (status IN ('SUCCESS', 'ABORTED', 'FAILED', 'BLOCKED_SIZE')),
    error_text        TEXT NULL,
    devmode_snapshot  BLOB NULL
);

CREATE TABLE print_history (
    id                       INTEGER PRIMARY KEY AUTOINCREMENT,
    printed_at               TEXT NOT NULL DEFAULT (strftime ('%Y-%m-%d %H:%M:%f', 'now')),
    mail_id                  INTEGER NULL REFERENCES mails (id) ON DELETE SET NULL,
    recipient_address_snapshot TEXT NOT NULL,
    sender_address_snapshot  TEXT NOT NULL,
    copy_count               INTEGER NOT NULL,
    is_vip                   INTEGER NOT NULL DEFAULT 0 CHECK (is_vip IN (0, 1)),
    note                     TEXT NULL,
    template_description     TEXT NOT NULL,
    print_job_id             INTEGER NULL REFERENCES print_jobs (id) ON DELETE CASCADE
);

CREATE TABLE qr_cache (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    payload_hash TEXT NOT NULL UNIQUE CHECK (length(payload_hash) = 64),
    image_blob   BLOB NOT NULL,
    created_at   TEXT NOT NULL DEFAULT (strftime ('%Y-%m-%d %H:%M:%f', 'now'))
);

CREATE TABLE counterparty_fields (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    counterparty_id INTEGER NOT NULL REFERENCES counterparties (id) ON DELETE CASCADE,
    field_name      TEXT NOT NULL,
    field_value     TEXT NULL,
    field_type      INTEGER NOT NULL DEFAULT 0 CHECK (field_type IN (0, 1, 2)),
    updated_at      TEXT NOT NULL DEFAULT (strftime ('%Y-%m-%d %H:%M:%f', 'now')),
    UNIQUE (counterparty_id, field_name)
);

CREATE TABLE inventory_items (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    inventory_id INTEGER NOT NULL REFERENCES inventories (id) ON DELETE CASCADE,
    name         TEXT NOT NULL,
    quantity     INTEGER NOT NULL CHECK (quantity > 0),
    cost         NUMERIC NOT NULL DEFAULT 0.00,
    note         TEXT NULL,
    created_at   TEXT NOT NULL DEFAULT (strftime ('%Y-%m-%d %H:%M:%f', 'now'))
);

CREATE TABLE document_movements (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    mail_id           INTEGER NOT NULL REFERENCES mails (id) ON DELETE CASCADE,
    moved_at          TEXT NOT NULL DEFAULT (strftime ('%Y-%m-%d %H:%M:%f', 'now')),
    target_department TEXT NOT NULL,
    executor          TEXT NULL,
    action            INTEGER NULL CHECK (action IN (0, 1, 2))
);

CREATE TABLE mail_documents (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    mail_id    INTEGER NOT NULL REFERENCES mails (id) ON DELETE CASCADE,
    doc_number TEXT NOT NULL
);

CREATE TABLE printer_profiles (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    printer_name     TEXT NOT NULL,
    envelope_format  TEXT NOT NULL,
    offset_x_mm      NUMERIC NOT NULL DEFAULT 0.0,
    offset_y_mm      NUMERIC NOT NULL DEFAULT 0.0,
    feed_alignment   INTEGER NOT NULL DEFAULT 0 CHECK (feed_alignment IN (0, 1, 2)),
    fix_a4           INTEGER NOT NULL DEFAULT 0 CHECK (fix_a4 IN (0, 1)),
    duplex_capable   INTEGER NOT NULL DEFAULT 0 CHECK (duplex_capable IN (0, 1)),
    is_default       INTEGER NOT NULL DEFAULT 0 CHECK (is_default IN (0, 1)),
    devmode_template BLOB NULL,
    UNIQUE (printer_name, envelope_format)
);

CREATE TABLE parameters (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT NOT NULL UNIQUE,
    value      TEXT NULL,
    updated_at TEXT NOT NULL DEFAULT (strftime ('%Y-%m-%d %H:%M:%f', 'now'))
);

CREATE TABLE schema_version (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    version     TEXT NOT NULL,
    description TEXT NULL,
    applied_at  TEXT NOT NULL DEFAULT (strftime ('%Y-%m-%d %H:%M:%f', 'now'))
);

-- Обязательные индексы ТЗ §3.1.19.
CREATE INDEX idx_counterparties_name ON counterparties (name);
CREATE INDEX idx_counterparties_inn ON counterparties (inn);
CREATE INDEX idx_counterparties_group_id ON counterparties (group_id);
CREATE INDEX idx_mails_counterparty_id ON mails (counterparty_id);
CREATE INDEX idx_mails_sender_id ON mails (sender_id);
CREATE INDEX idx_mails_status ON mails (status);
CREATE INDEX idx_mails_date_created ON mails (date_created);
CREATE INDEX idx_mails_post_register_id ON mails (post_register_id);
CREATE INDEX idx_print_history_printed_at ON print_history (printed_at);
CREATE INDEX idx_print_history_mail_id ON print_history (mail_id);
CREATE INDEX idx_inventory_items_inventory_id ON inventory_items (inventory_id);
CREATE INDEX idx_counterparty_fields_counterparty_id ON counterparty_fields (counterparty_id);

-- Дополнительные индексы по внешним ключам и выборкам.
CREATE INDEX idx_mails_distribution_id ON mails (distribution_id);
CREATE INDEX idx_mails_inventory_id ON mails (inventory_id);
CREATE INDEX idx_mails_to_zip ON mails (to_zip);
CREATE INDEX idx_mails_russian_post_id ON mails (russian_post_id);
CREATE INDEX idx_blank_fields_template_id ON blank_fields (template_id);
CREATE INDEX idx_print_jobs_template_id ON print_jobs (template_id);
CREATE INDEX idx_print_history_print_job_id ON print_history (print_job_id);
CREATE INDEX idx_document_movements_mail_id ON document_movements (mail_id);
CREATE INDEX idx_mail_documents_mail_id ON mail_documents (mail_id);
CREATE INDEX idx_distributions_sender_id ON distributions (sender_id);
CREATE INDEX idx_folders_parent_id ON folders (parent_id);
CREATE INDEX idx_inventories_counterparty_id ON inventories (counterparty_id);

-- Ровно одна группа по умолчанию (ТЗ §3.1.2): признак может быть выставлен
-- не более чем у одной группы.
CREATE UNIQUE INDEX ux_groups_single_default ON groups (is_default) WHERE is_default = 1;

-- Неизменяемый реестр сессий печати (ТЗ §3.1.6): изменение и удаление записей
-- запрещены на уровне схемы.
CREATE TRIGGER trg_print_jobs_no_update
BEFORE UPDATE ON print_jobs
BEGIN
    SELECT RAISE (ABORT, 'Запись реестра сессий печати (print_jobs) неизменяема.');
END;

CREATE TRIGGER trg_print_jobs_no_delete
BEFORE DELETE ON print_jobs
BEGIN
    SELECT RAISE (ABORT, 'Запись реестра сессий печати (print_jobs) неизменяема.');
END;
