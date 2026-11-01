-- Описание: Журнал отправки электронной почты (email_log)
-- Миграция выполняется только вперед и фиксируется в таблице schema_version.
--
-- ТЗ §8.7, функция 100: «Результат каждой отправки пишется в журнал email_log
-- (адрес, статус, код ответа сервера, дата)». Журнал ведётся в отдельном
-- канале «email» (ТЗ §10.3) и не связан с бумажными отправлениями.

CREATE TABLE IF NOT EXISTS email_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    recipient TEXT NOT NULL,
    status TEXT NOT NULL,
    smtp_code INTEGER NULL,
    server_response TEXT NULL,
    error TEXT NULL,
    sent_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_email_log_recipient ON email_log (recipient);
