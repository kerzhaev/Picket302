-- Описание: движения входящих документов (функция 30)
-- S33 (ТЗ §8.2, функция 30): история движения входящего документа —
-- приём/передача/в архив. История только дополнением: UPDATE/DELETE не
-- предусмотрены (критерий «история не переписывается»).
CREATE TABLE IF NOT EXISTS incoming_movements (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    incoming_id INTEGER NOT NULL REFERENCES incoming_mails (id),
    moved_at    TEXT NOT NULL,
    action      INTEGER NOT NULL,
    department  TEXT NOT NULL DEFAULT '',
    executor    TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_incoming_movements_incoming ON incoming_movements (incoming_id, moved_at);
