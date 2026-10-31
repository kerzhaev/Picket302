-- Описание: Исправление пресечения циклов в дереве папок и допустимого режима печати print_mode = test
-- Требуется: foreign_keys=OFF
-- Миграция выполняется только вперед и фиксируется в таблице schema_version.
--
-- ТЗ §3.1.11: «Ограничение: циклические ссылки parent_id пресекаются триггером/
-- валидатором домена». ТЗ §8.1, функция 143: «журнал print_jobs (print_mode = test)»,
-- при этом §3.1.6 перечисляет только selected, batch_preview, batch_direct —
-- расхождение зафиксировано в docs/OPEN_QUESTIONS_LOG.md (допущение S2.20).

-- ---------------------------------------------------------------------------
-- 1. Пресечение циклов в дереве папок
-- ---------------------------------------------------------------------------
-- Дефект 1: триггер вставки работал как BEFORE INSERT, где NEW.id при
-- автоназначении идентификатора равен -1. Условие «NEW.parent_id = NEW.id»
-- ложно, рекурсивный обход с корнем id = -1 пуст — вставка папки, которой
-- назначается идентификатор, совпадающий с её родителем, создавала цикл.
-- Решение: проверка переносится в AFTER INSERT, где NEW.id — фактически
-- назначенный идентификатор строки.
--
-- Дефект 2: рекурсивный обход выполнялся с UNION ALL, поэтому на уже
-- циклическом графе обход не завершался — обновление parent_id зависало
-- намертво, удерживая блокировку базы. Решение: UNION вместо UNION ALL —
-- повторно посещённые узлы отбрасываются, обход всегда конечен.

DROP TRIGGER trg_folders_no_cycle_insert;
DROP TRIGGER trg_folders_no_cycle_update;

-- Вставка папки: цикл отклоняется после назначения идентификатора строки.
CREATE TRIGGER trg_folders_no_cycle_insert
AFTER INSERT ON folders
WHEN NEW.parent_id IS NOT NULL
BEGIN
    SELECT RAISE (ABORT, 'Циклическая ссылка в дереве папок запрещена.')
    WHERE NEW.parent_id = NEW.id
       OR NEW.parent_id IN (
            WITH RECURSIVE subtree(id) AS (
                SELECT id FROM folders WHERE id = NEW.id
                UNION
                SELECT child.id FROM folders child JOIN subtree ON child.parent_id = subtree.id
            )
            SELECT id FROM subtree
       );
END;

-- Перемещение папки: родителем нельзя назначить саму папку или её потомка.
-- UNION (без ALL) гарантирует конечность обхода даже на повреждённом дереве.
CREATE TRIGGER trg_folders_no_cycle_update
BEFORE UPDATE OF parent_id ON folders
WHEN NEW.parent_id IS NOT NULL
BEGIN
    SELECT RAISE (ABORT, 'Циклическая ссылка в дереве папок запрещена.')
    WHERE NEW.parent_id = NEW.id
       OR NEW.parent_id IN (
            WITH RECURSIVE subtree(id) AS (
                SELECT id FROM folders WHERE id = NEW.id
                UNION
                SELECT child.id FROM folders child JOIN subtree ON child.parent_id = subtree.id
            )
            SELECT id FROM subtree
       );
END;

-- ---------------------------------------------------------------------------
-- 2. Режим печати print_mode = test (ТЗ §8.1, функция 143)
-- ---------------------------------------------------------------------------
-- CHECK-ограничение не изменяется в SQLite, поэтому таблица print_jobs
-- пересобирается: данные копируются, ограничение расширяется, триггеры
-- неизменяемости и индекс пересоздаются. Пересборка выполняется при
-- отключённых внешних ключах (директива «Требуется: foreign_keys=OFF»):
-- иначе удаление print_jobs каскадно удалило бы записи print_history.

CREATE TABLE print_jobs_new (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at        TEXT NOT NULL DEFAULT (strftime ('%Y-%m-%d %H:%M:%f', 'now')),
    template_id       INTEGER NULL REFERENCES print_templates (id),
    printer_name      TEXT NOT NULL,
    paper_size_name   TEXT NOT NULL,
    paper_width_mm    NUMERIC NOT NULL,
    paper_height_mm   NUMERIC NOT NULL,
    font_info         TEXT NULL,
    copies            INTEGER NOT NULL,
    print_mode        TEXT NOT NULL CHECK (print_mode IN ('selected', 'batch_preview', 'batch_direct', 'test')),
    status            TEXT NOT NULL CHECK (status IN ('SUCCESS', 'ABORTED', 'FAILED', 'BLOCKED_SIZE')),
    error_text        TEXT NULL,
    devmode_snapshot  BLOB NULL
);

INSERT INTO print_jobs_new (
    id, created_at, template_id, printer_name, paper_size_name, paper_width_mm,
    paper_height_mm, font_info, copies, print_mode, status, error_text, devmode_snapshot)
SELECT
    id, created_at, template_id, printer_name, paper_size_name, paper_width_mm,
    paper_height_mm, font_info, copies, print_mode, status, error_text, devmode_snapshot
FROM print_jobs;

DROP TABLE print_jobs;

ALTER TABLE print_jobs_new RENAME TO print_jobs;

-- Неизменяемый реестр сессий печати (ТЗ §3.1.6) сохраняется после пересборки.
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

CREATE INDEX idx_print_jobs_template_id ON print_jobs (template_id);
