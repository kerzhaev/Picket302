-- Описание: журнал входящей корреспонденции (функция 29)
-- S32 (ТЗ §8.2, функция 29): регистрация входящих отправлений — номер,
-- дата, отправитель, содержание, тип и путь к скану в профиле пользователя.
CREATE TABLE IF NOT EXISTS incoming_mails (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    reg_number  TEXT NOT NULL UNIQUE,
    reg_date    TEXT NOT NULL,
    sender      TEXT NOT NULL DEFAULT '',
    subject     TEXT NOT NULL DEFAULT '',
    doc_type    INTEGER NOT NULL DEFAULT 0,
    scan_path   TEXT NULL,
    created_at  TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_incoming_mails_reg_date ON incoming_mails (reg_date DESC);
